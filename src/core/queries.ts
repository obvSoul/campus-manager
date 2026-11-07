/** Lectures pour l'interface et le widget : uniquement la base locale, jamais EPREL. */
import { extname } from "node:path";
import type { Db } from "./db/database.js";
import type { AssignmentItem, AssignmentStatus, Dashboard, FileItem, FileQuery, SemesterNode, WidgetData } from "../shared/api.js";

const FILE_SELECT = `
  SELECT f.id, f.original_name, f.local_path, f.category, f.course_id, f.size, f.added_at, f.favorite, f.missing, f.remote_deleted,
         COALESCE(c.folder, '') AS course, COALESCE(c.semester, '') AS semester
  FROM files f LEFT JOIN courses c ON c.id = f.course_id
  LEFT JOIN resources r ON r.id = f.resource_id`;

type FileRow = { id: number; original_name: string; local_path: string; category: string; course_id: number | null; size: number;
  added_at: number; favorite: number; missing: number; remote_deleted: number; course: string; semester: string };

export function toFileItem(r: FileRow): FileItem {
  const name = r.local_path.split(/[\\/]/).pop() ?? r.original_name;
  return { id: r.id, name, ext: extname(name).slice(1).toLowerCase(), category: r.category, courseId: r.course_id,
    course: r.course, semester: r.semester, size: r.size, addedAt: r.added_at, favorite: !!r.favorite,
    missing: !!r.missing, remoteDeleted: !!r.remote_deleted, path: r.local_path };
}

export function listFiles(db: Db, q: FileQuery = {}): FileItem[] {
  const where = ["f.user_deleted = 0"];
  const args: (string | number)[] = [];
  if (q.courseId != null) { where.push("f.course_id = ?"); args.push(q.courseId); }
  if (q.category) { where.push("f.category = ?"); args.push(q.category); }
  if (q.categories?.length) { where.push(`f.category IN (${q.categories.map(() => "?").join(",")})`); args.push(...q.categories); }
  if (q.favorites) where.push("f.favorite = 1");
  if (q.search?.trim()) {
    // Recherche dans le nom, le chemin, la matière, la catégorie, le titre et la description EPREL.
    const terms = q.search.trim().toLowerCase().split(/\s+/);
    for (const t of terms) {
      where.push("(lower(f.local_path) LIKE ? OR lower(c.name) LIKE ? OR lower(f.category) LIKE ? OR lower(COALESCE(r.name,'')) LIKE ? OR lower(COALESCE(r.description,'')) LIKE ?)");
      args.push(...Array(5).fill(`%${t}%`));
    }
  }
  const order = q.recent ? "f.added_at DESC, f.id DESC" : "c.semester, c.folder, f.category, f.local_path";
  const sql = `${FILE_SELECT} WHERE ${where.join(" AND ")} ORDER BY ${order} LIMIT ?`;
  return (db.prepare(sql).all(...args, q.limit ?? 2000) as unknown as FileRow[]).map(toFileItem);
}

export function tree(db: Db): SemesterNode[] {
  const courses = db.prepare("SELECT id, name, folder, semester FROM courses ORDER BY semester, folder").all() as
    { id: number; name: string; folder: string; semester: string }[];
  const cats = db.prepare("SELECT course_id, category, COUNT(*) n FROM files WHERE user_deleted = 0 GROUP BY course_id, category").all() as
    { course_id: number; category: string; n: number }[];
  const sems = new Map<string, SemesterNode>();
  for (const c of courses) {
    const node = { ...c, categories: cats.filter((x) => x.course_id === c.id).map((x) => ({ name: x.category, count: x.n })) };
    if (!sems.has(c.semester)) sems.set(c.semester, { name: c.semester, courses: [] });
    sems.get(c.semester)!.courses.push(node);
  }
  // « Autres matières » en dernier.
  return [...sems.values()].sort((a, b) => (a.name === "Autres matières" ? 1 : b.name === "Autres matières" ? -1 : a.name.localeCompare(b.name)));
}

/** Début de l'année universitaire en cours : 15 août (heure locale). */
export function academicYearStart(now: number): number {
  const d = new Date(now * 1000);
  const year = d.getMonth() >= 7 && !(d.getMonth() === 7 && d.getDate() < 15) ? d.getFullYear() : d.getFullYear() - 1;
  return Math.floor(new Date(year, 7, 15).getTime() / 1000);
}

export function listAssignments(db: Db, now = Math.floor(Date.now() / 1000)): AssignmentItem[] {
  const rows = db.prepare(`SELECT a.id, a.name, a.description, a.due_at, a.published_at, a.status, a.priority, a.notes, a.course_id,
      a.submitted, c.startdate, COALESCE(c.folder, 'Personnel') AS course FROM assignments a LEFT JOIN courses c ON c.id = a.course_id
      WHERE a.remote_deleted = 0 ORDER BY a.due_at IS NULL, a.due_at`).all() as
    { id: number; name: string; description: string | null; due_at: number | null; published_at: number | null; status: AssignmentStatus;
      priority: number; notes: string | null; course_id: number | null; course: string; submitted: number; startdate: number | null }[];
  const yearStart = academicYearStart(now);
  return rows.map((r) => {
    // Échéance d'une année universitaire précédente (les espaces EPREL sont réutilisés d'une année sur l'autre),
    // ou antérieure au début du cours : affiché à part, jamais compté en retard.
    const old = r.due_at != null && (r.due_at < yearStart || (r.startdate != null && r.startdate > 0 && r.due_at < r.startdate));
    return { id: r.id, name: r.name, course: r.course, courseId: r.course_id, description: r.description,
      dueAt: r.due_at, publishedAt: r.published_at, status: r.status, priority: r.priority, notes: r.notes,
      submitted: !!r.submitted, old,
      // « En retard » est calculé à partir de la vraie date limite, jamais stocké ni inventé.
      late: !old && r.status !== "done" && r.due_at != null && r.due_at < now };
  });
}

export function setAssignment(db: Db, id: number, patch: { status?: AssignmentStatus; priority?: number; notes?: string | null }) {
  if (patch.status && !["todo", "doing", "done"].includes(patch.status)) throw new Error("Statut invalide");
  // Un statut choisi à la main n'est plus jamais modifié par la synchronisation.
  if (patch.status) db.prepare("UPDATE assignments SET status=?, status_source='manual' WHERE id=?").run(patch.status, id);
  if (patch.priority != null) db.prepare("UPDATE assignments SET priority=? WHERE id=?").run(patch.priority, id);
  if (patch.notes !== undefined) db.prepare("UPDATE assignments SET notes=? WHERE id=?").run(patch.notes, id);
}

/** Données du widget : uniquement la base locale (fonctionne hors ligne). */
export function widgetData(db: Db, now = Math.floor(Date.now() / 1000)): WidgetData {
  const as = listAssignments(db, now).filter((a) => !a.old);
  const open = as.filter((a) => a.status !== "done");
  const last = db.prepare("SELECT finished_at FROM sync_log WHERE finished_at IS NOT NULL ORDER BY id DESC LIMIT 1").get() as { finished_at: number } | undefined;
  const work = listFiles(db, { categories: ["TD", "TP", "Exercices"], recent: true, limit: 5 });
  const shown = new Set(work.map((f) => f.id));
  return {
    assignments: open.filter((a) => a.late).concat(open.filter((a) => !a.late)).slice(0, 6),
    work,
    // Pas de doublon avec la section TD/TP.
    recent: listFiles(db, { recent: true, limit: 20 }).filter((f) => !shown.has(f.id)).slice(0, 5),
    progress: { done: as.length - open.length, open: open.length, late: open.filter((a) => a.late).length },
    lastSync: last?.finished_at ?? null,
  };
}

export function dashboard(db: Db, now = Math.floor(Date.now() / 1000)): Dashboard {
  const as = listAssignments(db, now).filter((a) => !a.old);
  const open = as.filter((a) => a.status !== "done");
  const n = (sql: string) => (db.prepare(sql).get() as { n: number }).n;
  const perCourse = new Map<string, { done: number; total: number }>();
  for (const a of as) {
    const p = perCourse.get(a.course) ?? { done: 0, total: 0 };
    p.total++; if (a.status === "done") p.done++;
    perCourse.set(a.course, p);
  }
  const last = db.prepare("SELECT finished_at, ok, summary FROM sync_log WHERE finished_at IS NOT NULL ORDER BY id DESC LIMIT 1").get() as
    { finished_at: number; ok: number; summary: string } | undefined;
  let lastSync: Dashboard["lastSync"] = null;
  if (last) {
    const s = JSON.parse(last.summary ?? "{}") as { added?: number; errors?: unknown[] };
    lastSync = { at: last.finished_at, ok: !!last.ok, added: s.added ?? 0, errors: s.errors?.length ?? 0 };
  }
  return {
    // Les retards d'abord (à traiter), puis les prochaines échéances.
    upcoming: open.filter((a) => a.late).slice(0, 3).concat(open.filter((a) => !a.late).slice(0, 6)),
    recentFiles: listFiles(db, { recent: true, limit: 8 }),
    counts: {
      files: n("SELECT COUNT(*) n FROM files WHERE user_deleted=0"), courses: n("SELECT COUNT(*) n FROM courses"),
      td: n("SELECT COUNT(*) n FROM files WHERE user_deleted=0 AND category='TD'"), tp: n("SELECT COUNT(*) n FROM files WHERE user_deleted=0 AND category='TP'"),
      assignmentsOpen: open.length, assignmentsDone: as.length - open.length, assignmentsLate: open.filter((a) => a.late).length,
      unsorted: n("SELECT COUNT(*) n FROM files WHERE user_deleted=0 AND category='À classer'"),
    },
    perCourse: [...perCourse.entries()].map(([course, v]) => ({ course, ...v })),
    lastSync,
  };
}
