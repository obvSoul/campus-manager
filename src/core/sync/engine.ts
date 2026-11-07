/**
 * Moteur de synchronisation : EPREL -> disque + base locale. Rien ne remonte vers EPREL.
 * Règles : n'écrase jamais un fichier modifié par l'utilisateur, ne supprime jamais une copie locale
 * quand une ressource disparaît d'EPREL (elle est seulement signalée), une erreur sur un fichier
 * n'arrête pas les autres, les erreurs réseau sont réessayées.
 */
import type { Db } from "../db/database.js";
import { classify, UNSORTED } from "../classify/classifier.js";
import { sanitizeName } from "../library/names.js";
import type { Library } from "../library/library.js";
import { MoodleError, type MoodleApi, type MoodleCourse } from "./moodle.js";

export interface SyncReport {
  courses: number; added: number; updated: number; unchanged: number; duplicates: number;
  conflicts: string[]; errors: string[]; remoteGone: number; assignments: number; authExpired: boolean;
}

const sleep = (ms: number) => new Promise((r) => setTimeout(r, ms));

export async function withRetry<T>(fn: () => Promise<T>, tries = 3, baseMs = 1000): Promise<T> {
  for (let i = 1; ; i++) {
    try { return await fn(); }
    catch (e) {
      if (!(e instanceof MoodleError && e.isTransient) || i >= tries) throw e;
      await sleep(baseMs * 2 ** (i - 1));
    }
  }
}

/** Déduit le semestre seulement s'il est écrit dans le nom ; sinon « Autres matières » (corrigeable). */
export function guessSemester(c: Pick<MoodleCourse, "fullname" | "shortname">): string {
  const t = `${c.fullname} ${c.shortname}`;
  // Codification du BUT : R1.01, SAE1.02, SAÉ 2.03 -> le premier chiffre est le semestre.
  const but = t.match(/\b(?:R|SA[EÉ])\s?([1-6])\.\d{1,2}/i);
  const m = but ?? t.match(/\b(?:semestre\s*|s)([1-6])\b/i);
  return m ? `Semestre ${m[1]}` : "Autres matières";
}

/** Nom de dossier lisible : sans le préfixe commun « BUT INFO : ». */
export function courseFolderName(fullname: string): string {
  return sanitizeName(fullname.replace(/^\s*BUT\s+INFO\s*:\s*/i, "").replace(/\s+/g, " "), 80);
}

const stripQuery = (u: string) => u.split("?")[0]!;
/** mod/assign/view.php?id=123 -> 123 */
const cmidOf = (url?: string) => { try { return url ? Number(new URL(url).searchParams.get("id")) || null : null; } catch { return null; } };
const text = (html?: string) => (html ?? "").replace(/<[^>]+>/g, " ").replace(/\s+/g, " ").trim();

export async function syncAll(api: MoodleApi, db: Db, lib: Library, retryMs = 1000): Promise<SyncReport> {
  const rep: SyncReport = { courses: 0, added: 0, updated: 0, unchanged: 0, duplicates: 0, conflicts: [], errors: [], remoteGone: 0, assignments: 0, authExpired: false };
  const log = db.prepare("INSERT INTO sync_log(started_at) VALUES(?)").run(Math.floor(Date.now() / 1000));
  try {
    const remoteCourses = await withRetry(() => api.courses(), 3, retryMs);
    rep.courses = remoteCourses.length;
    const idOf = new Map<number, number>();

    for (const c of remoteCourses) {
      const row = db.prepare("SELECT id, semester, folder, layout_manual FROM courses WHERE remote_id=?").get(c.id) as
        { id: number; semester: string; folder: string; layout_manual: number } | undefined;
      if (row) {
        db.prepare("UPDATE courses SET name=?, shortname=? WHERE id=?").run(c.fullname, c.shortname, row.id);
        // Rangement amélioré : on déplace (sans retélécharger), sauf si tu l'as réglé toi-même.
        const sem = guessSemester(c), folder = courseFolderName(c.fullname);
        if (!row.layout_manual && (sem !== row.semester || folder !== row.folder)) lib.relocateCourse(row.id, sem, folder);
        idOf.set(c.id, row.id);
      } else {
        const r = db.prepare("INSERT INTO courses(remote_id,name,shortname,semester,folder) VALUES(?,?,?,?,?)")
          .run(c.id, c.fullname, c.shortname, guessSemester(c), courseFolderName(c.fullname));
        idOf.set(c.id, Number(r.lastInsertRowid));
      }
    }

    for (const c of remoteCourses) {
      const courseId = idOf.get(c.id)!;
      const seenRes = new Set<number>(), seenKeys = new Set<string>();
      let sections;
      try { sections = await withRetry(() => api.contents(c.id), 3, retryMs); }
      catch (e) { rep.errors.push(`${c.shortname} : ${(e as Error).message}`); continue; }

      for (const s of sections) for (const m of s.modules) {
        if (!["resource", "folder", "assign"].includes(m.modname) && !(m.contents ?? []).some((f) => f.type === "file")) continue;
        seenRes.add(m.id);
        const files = (m.contents ?? []).filter((f) => f.type === "file");
        const existing = db.prepare("SELECT id, category, category_source FROM resources WHERE course_id=? AND remote_id=?").get(courseId, m.id) as
          { id: number; category: string; category_source: string } | undefined;
        const verdict = classify({ modname: m.modname, title: m.name, section: s.name, description: text(m.description), filename: files[0]?.filename });
        const category = existing?.category_source === "manual" ? existing.category : verdict.category;
        let resId: number;
        if (existing) {
          db.prepare("UPDATE resources SET name=?, modname=?, section=?, description=?, category=?, confidence=?, remote_deleted=0 WHERE id=?")
            .run(m.name, m.modname, s.name, text(m.description), category, verdict.confidence, existing.id);
          resId = existing.id;
        } else {
          resId = Number(db.prepare("INSERT INTO resources(course_id,remote_id,name,modname,section,description,category,confidence,published_at) VALUES(?,?,?,?,?,?,?,?,?)")
            .run(courseId, m.id, m.name, m.modname, s.name, text(m.description), category, verdict.confidence, m.added ?? null).lastInsertRowid);
        }
        // Les devoirs sont suivis dans la table assignments ; leurs fichiers vont dans « Devoirs ».
        for (const f of files) {
          const key = stripQuery(f.fileurl);
          seenKeys.add(key);
          try {
            const r = await withRetry(() => lib.importFile({
              courseId, resourceId: resId, category, name: f.filename, remoteKey: key,
              remoteModified: f.timemodified, remoteSize: f.filesize, fetchBytes: () => api.download(f.fileurl),
            }), 3, retryMs);
            if (r.status === "added") rep.added++;
            else if (r.status === "updated") rep.updated++;
            else if (r.status === "unchanged") rep.unchanged++;
            else if (r.status === "duplicate") rep.duplicates++;
            else rep.conflicts.push(r.path);
          } catch (e) {
            if (e instanceof MoodleError && e.isAuthError) throw e;
            rep.errors.push(`${f.filename} : ${(e as Error).message}`);
          }
        }
      }

      // Ressources/fichiers disparus d'EPREL : signalés, JAMAIS supprimés localement.
      const gone = db.prepare("SELECT id, remote_id FROM resources WHERE course_id=? AND remote_deleted=0").all(courseId) as { id: number; remote_id: number | null }[];
      for (const r of gone) if (r.remote_id != null && !seenRes.has(r.remote_id)) {
        db.prepare("UPDATE resources SET remote_deleted=1 WHERE id=?").run(r.id);
        db.prepare("UPDATE files SET remote_deleted=1 WHERE resource_id=?").run(r.id);
        rep.remoteGone++;
      }
    }

    for (const c of remoteCourses) db.prepare("UPDATE courses SET startdate=? WHERE id=?").run(c.startdate || null, idOf.get(c.id)!);

    const as = await withRetry(() => api.assignments(remoteCourses.map((c) => c.id)), 3, retryMs).catch((e) => { rep.errors.push(`devoirs : ${(e as Error).message}`); return []; });

    // Date limite RÉELLEMENT applicable : celle de la chronologie EPREL (prolongations, dérogations de groupe).
    const events = api.actionEvents
      ? await withRetry(() => api.actionEvents!(Math.floor(Date.now() / 1000) - 2 * 365 * 86400), 3, retryMs)
          .catch((e) => { rep.errors.push(`chronologie : ${(e as Error).message}`); return []; })
      : [];
    const effectiveDue = (a: { id: number; cmid: number }) => {
      const ev = events.find((e) => e.modulename === "assign" && (cmidOf(e.url) === a.cmid || e.instance === a.cmid || e.instance === a.id));
      return ev?.timesort;
    };

    for (const a of as) {
      const courseId = idOf.get(a.course);
      if (!courseId) continue;
      const base = a.duedate > 0 ? a.duedate : null; // 0 = pas de date limite : on n'en invente pas
      const due = effectiveDue(a) ?? base;
      let submitted: number | null = null;
      if (api.submissionStatus) {
        const st = await withRetry(() => api.submissionStatus!(a.id), 3, retryMs).catch(() => null);
        if (st != null) submitted = st === "submitted" ? 1 : 0;
      }
      const ex = db.prepare("SELECT id, status_source FROM assignments WHERE remote_id=?").get(a.id) as { id: number; status_source: string } | undefined;
      let id: number;
      if (ex) {
        db.prepare(`UPDATE assignments SET name=?, description=?, due_at=?, base_due_at=?, cmid=?, published_at=?, remote_modified=?, course_id=?,
          remote_deleted=0, submitted=COALESCE(?, submitted) WHERE id=?`)
          .run(a.name, text(a.intro), due, base, a.cmid, a.allowsubmissionsfromdate || null, a.timemodified, courseId, submitted, ex.id);
        id = ex.id;
      } else {
        id = Number(db.prepare(`INSERT INTO assignments(remote_id,course_id,name,description,due_at,base_due_at,cmid,published_at,remote_modified,submitted)
          VALUES(?,?,?,?,?,?,?,?,?,?)`).run(a.id, courseId, a.name, text(a.intro), due, base, a.cmid, a.allowsubmissionsfromdate || null, a.timemodified, submitted ?? 0).lastInsertRowid);
      }
      // Rendu sur EPREL -> « Terminé », sauf si tu as choisi toi-même un statut (jamais écrasé).
      if (submitted === 1) db.prepare("UPDATE assignments SET status='done' WHERE id=? AND status_source='auto'").run(id);
      rep.assignments++;
    }
  } catch (e) {
    if (e instanceof MoodleError && e.isAuthError) rep.authExpired = true;
    rep.errors.push((e as Error).message);
  }
  db.prepare("UPDATE sync_log SET finished_at=?, ok=?, summary=? WHERE id=?")
    .run(Math.floor(Date.now() / 1000), rep.errors.length === 0 && !rep.authExpired ? 1 : 0, JSON.stringify(rep), Number(log.lastInsertRowid));
  return rep;
}

export { UNSORTED };
