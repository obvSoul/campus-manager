/**
 * ATTENTION : l'API Moodle est ici SIMULÉE (format documenté). Ces tests prouvent la logique de sync
 * (téléchargement réel sur disque, classement, doublons, conflits, suppression côté EPREL, reprises),
 * pas que le vrai EPREL expose ces données. Seul un essai avec ton compte le prouvera.
 */
import { createHash } from "node:crypto";
import { existsSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { openDb, type Db } from "../src/core/db/database.js";
import { Library } from "../src/core/library/library.js";
import { courseFolderName, guessSemester, syncAll } from "../src/core/sync/engine.js";
import { MoodleClient, MoodleError, type Assignment, type CourseSection, type MoodleApi } from "../src/core/sync/moodle.js";
import { buildLaunchUrl, newPassport, parseTokenUrl } from "../src/core/sync/sso.js";

let dir: string, db: Db, lib: Library;
let sections: CourseSection[], assigns: Assignment[], downloads: string[], failNext: Error | null;

const file = (filename: string, content: string, t = 100) => ({ type: "file", filename, filesize: content.length, timemodified: t, fileurl: `https://e/pluginfile.php/1/mod_resource/content/1/${filename}?forcedownload=1` });
const store: Record<string, string> = {};
const api: MoodleApi = {
  courses: async () => [{ id: 7, fullname: "Programmation C S1", shortname: "R1.01" }],
  contents: async () => sections,
  assignments: async () => assigns,
  download: async (url) => {
    if (failNext) { const e = failNext; failNext = null; throw e; }
    downloads.push(url);
    return new TextEncoder().encode(store[url.split("?")[0]!.split("/").pop()!]);
  },
};
const put = (name: string, content: string) => { store[name] = content; };

beforeEach(() => {
  dir = mkdtempSync(join(tmpdir(), "sync-"));
  db = openDb(); lib = new Library(db, join(dir, "Campus Manager"));
  downloads = []; failNext = null; assigns = [];
  for (const k of Object.keys(store)) delete store[k];
  put("cm1.pdf", "COURS1"); put("td1.pdf", "TD1"); put("td1_corrige.pdf", "CORR");
  sections = [
    { id: 1, name: "Cours magistraux", modules: [{ id: 11, name: "CM1", modname: "resource", contents: [file("cm1.pdf", "COURS1")] }] },
    { id: 2, name: "Travaux dirigés", modules: [
      { id: 12, name: "TD1", modname: "resource", contents: [file("td1.pdf", "TD1")] },
      { id: 13, name: "Correction du TD1", modname: "resource", contents: [file("td1_corrige.pdf", "CORR")] },
    ] },
  ];
});
afterEach(() => rmSync(dir, { recursive: true, force: true }));

describe("syncAll", () => {
  it("télécharge réellement les fichiers et les range par semestre/matière/catégorie", async () => {
    const r = await syncAll(api, db, lib, 1);
    expect(r).toMatchObject({ courses: 1, added: 3, errors: [] });
    const base = join(lib.root, "Semestre 1", "Programmation C S1");
    expect(readFileSync(join(base, "Cours", "cm1.pdf"), "utf8")).toBe("COURS1");
    expect(readFileSync(join(base, "TD", "td1.pdf"), "utf8")).toBe("TD1");
    expect(readFileSync(join(base, "Corrections", "td1_corrige.pdf"), "utf8")).toBe("CORR");
  });

  it("2e synchronisation : rien n'est retéléchargé", async () => {
    await syncAll(api, db, lib, 1);
    downloads.length = 0;
    const r = await syncAll(api, db, lib, 1);
    expect(r).toMatchObject({ added: 0, unchanged: 3 });
    expect(downloads).toEqual([]);
  });

  it("ressource disparue d'EPREL : signalée, copie locale conservée", async () => {
    await syncAll(api, db, lib, 1);
    sections[1]!.modules.pop();
    const r = await syncAll(api, db, lib, 1);
    expect(r.remoteGone).toBe(1);
    expect(existsSync(join(lib.root, "Semestre 1", "Programmation C S1", "Corrections", "td1_corrige.pdf"))).toBe(true);
    expect(db.prepare("SELECT COUNT(*) n FROM files WHERE remote_deleted=1").get()).toEqual({ n: 1 });
  });

  it("fichier modifié par l'utilisateur puis mis à jour sur EPREL : conflit, rien d'écrasé", async () => {
    await syncAll(api, db, lib, 1);
    const p = join(lib.root, "Semestre 1", "Programmation C S1", "TD", "td1.pdf");
    writeFileSync(p, "MES NOTES");
    put("td1.pdf", "TD1 v2!"); sections[1]!.modules[0]!.contents = [file("td1.pdf", "TD1 v2!", 200)];
    const r = await syncAll(api, db, lib, 1);
    expect(r.conflicts).toHaveLength(1);
    expect(readFileSync(p, "utf8")).toBe("MES NOTES");
  });

  it("erreur réseau transitoire : nouvelle tentative automatique", async () => {
    failNext = new MoodleError("coupure", "network");
    const r = await syncAll(api, db, lib, 1);
    expect(r.errors).toEqual([]);
    expect(r.added).toBe(3);
  });

  it("jeton expiré : signalé, les données locales restent", async () => {
    await syncAll(api, db, lib, 1);
    const bad = { ...api, courses: async () => { throw new MoodleError("jeton", "invalidtoken"); } };
    const r = await syncAll(bad, db, lib, 1);
    expect(r.authExpired).toBe(true);
    expect(db.prepare("SELECT COUNT(*) n FROM files").get()).toEqual({ n: 3 });
  });

  it("devoirs : date limite réelle, jamais inventée, statut perso conservé", async () => {
    assigns = [
      { id: 1, cmid: 5, course: 7, name: "Rendu du TD#3", intro: "<p>Rendre le <b>TD</b></p>", duedate: 1790000000, allowsubmissionsfromdate: 0, timemodified: 1 },
      { id: 2, cmid: 6, course: 7, name: "Sans date", duedate: 0, allowsubmissionsfromdate: 0, timemodified: 1 },
    ];
    await syncAll(api, db, lib, 1);
    db.prepare("UPDATE assignments SET status='done' WHERE remote_id=1").run();
    assigns[0]!.duedate = 1790086400; // l'enseignant décale la date
    await syncAll(api, db, lib, 1);
    expect(db.prepare("SELECT due_at, status, description FROM assignments WHERE remote_id=1").get()).toEqual({ due_at: 1790086400, status: "done", description: "Rendre le TD" });
    expect(db.prepare("SELECT due_at FROM assignments WHERE remote_id=2").get()).toEqual({ due_at: null });
  });

  it("devoirs : date de la chronologie (prolongation), rendu -> Terminé, choix manuel jamais écrasé", async () => {
    assigns = [
      { id: 48508, cmid: 900, course: 7, name: "Rendu du TD#3", duedate: 1762901940, allowsubmissionsfromdate: 0, timemodified: 1 },
      { id: 2, cmid: 901, course: 7, name: "TP rendu", duedate: 1790000000, allowsubmissionsfromdate: 0, timemodified: 1 },
    ];
    const subs: Record<number, string> = { 48508: "new", 2: "submitted" };
    const withExtras: MoodleApi = { ...api,
      submissionStatus: async (id) => subs[id] ?? null,
      actionEvents: async () => [{ modulename: "assign", instance: 417783, timesort: 1794092340, url: "https://e/mod/assign/view.php?id=900" }] };
    await syncAll(withExtras, db, lib, 1);
    expect(db.prepare("SELECT due_at, base_due_at, status FROM assignments WHERE remote_id=48508").get())
      .toEqual({ due_at: 1794092340, base_due_at: 1762901940, status: "todo" });
    expect(db.prepare("SELECT status, submitted FROM assignments WHERE remote_id=2").get()).toEqual({ status: "done", submitted: 1 });
    // choix manuel : « en cours » malgré le rendu -> conservé
    const { setAssignment } = await import("../src/core/queries.js");
    const row = db.prepare("SELECT id FROM assignments WHERE remote_id=2").get() as { id: number };
    setAssignment(db, row.id, { status: "doing" });
    await syncAll(withExtras, db, lib, 1);
    expect(db.prepare("SELECT status FROM assignments WHERE remote_id=2").get()).toEqual({ status: "doing" });
  });

  it("devoir antérieur au début du cours : « ancien », jamais compté en retard", async () => {
    const { listAssignments } = await import("../src/core/queries.js");
    const withStart: MoodleApi = { ...api, courses: async () => [{ id: 7, fullname: "Programmation C S1", shortname: "R1.01", startdate: 1788000000 }] };
    assigns = [{ id: 5, cmid: 5, course: 7, name: "Vieux TP", duedate: 1759000000, allowsubmissionsfromdate: 0, timemodified: 1 }];
    await syncAll(withStart, db, lib, 1);
    const [a] = listAssignments(db, 1800000000);
    expect(a).toMatchObject({ old: true, late: false });
  });

  it("une correction manuelle de catégorie est conservée aux synchronisations suivantes", async () => {
    await syncAll(api, db, lib, 1);
    db.prepare("UPDATE resources SET category='Projets', category_source='manual' WHERE remote_id=11").run();
    await syncAll(api, db, lib, 1);
    expect(db.prepare("SELECT category FROM resources WHERE remote_id=11").get()).toEqual({ category: "Projets" });
  });

  it("codification BUT (vrais noms EPREL) : semestre et dossier sans préfixe", () => {
    expect(guessSemester({ fullname: "BUT INFO : R1.01 - Initiation au développement", shortname: "" })).toBe("Semestre 1");
    expect(guessSemester({ fullname: "BUT INFO : SAE1.07-2.07 - Portfolio", shortname: "" })).toBe("Semestre 1");
    expect(guessSemester({ fullname: "BUT INFO : Informations générales", shortname: "" })).toBe("Autres matières");
    expect(courseFolderName("BUT INFO : R1.03 -  Introduction à l'architecture")).toBe("R1.03 - Introduction à l'architecture");
  });

  it("rangement amélioré : les fichiers déjà téléchargés sont déplacés, pas retéléchargés", async () => {
    await syncAll(api, db, lib, 1);
    db.prepare("UPDATE courses SET semester='Autres matières', folder='BUT INFO _ vieux'").run();
    // simule l'ancien rangement sur le disque
    const { renameSync, mkdirSync } = await import("node:fs");
    mkdirSync(join(lib.root, "Autres matières"), { recursive: true });
    renameSync(join(lib.root, "Semestre 1", "Programmation C S1"), join(lib.root, "Autres matières", "BUT INFO _ vieux"));
    db.prepare("UPDATE files SET local_path=replace(local_path, ?, ?)").run(join("Semestre 1", "Programmation C S1"), join("Autres matières", "BUT INFO _ vieux"));
    downloads.length = 0;
    const r = await syncAll(api, db, lib, 1);
    expect(r.errors).toEqual([]);
    expect(downloads).toEqual([]);
    expect(readFileSync(join(lib.root, "Semestre 1", "Programmation C S1", "TD", "td1.pdf"), "utf8")).toBe("TD1");
    expect(existsSync(join(lib.root, "Autres matières"))).toBe(false);
  });

  it("le semestre n'est déduit que s'il est écrit dans le nom", () => {
    expect(guessSemester({ fullname: "Maths S2", shortname: "R2.01" })).toBe("Semestre 2");
    expect(guessSemester({ fullname: "Algorithmique", shortname: "ALGO" })).toBe("Autres matières");
  });
});

describe("MoodleClient", () => {
  it("jeton jamais dans l'URL de requête API, et erreur d'auth reconnue", async () => {
    const calls: { url: string; body: URLSearchParams }[] = [];
    const f = (async (url: string, init: RequestInit) => { calls.push({ url, body: init.body as URLSearchParams }); return new Response(JSON.stringify({ exception: "x", errorcode: "invalidtoken", message: "Jeton invalide" })); }) as unknown as typeof fetch;
    const e = await new MoodleClient("https://e", "SECRET", f).siteInfo().catch((x) => x);
    expect(e.isAuthError).toBe(true);
    expect(calls[0]!.url).not.toContain("SECRET");
  });
  it("téléchargement refusé en JSON -> erreur claire, jeton masqué", async () => {
    const f = (async () => new Response(JSON.stringify({ error: "Accès refusé", errorcode: "accessexception" }), { headers: { "content-type": "application/json" } })) as unknown as typeof fetch;
    const e = await new MoodleClient("https://e", "SECRET", f).download("https://e/pluginfile.php/a.pdf").catch((x) => x);
    expect(e.isAuthError).toBe(true);
  });
  it("URL de téléchargement : pas de /webservice doublé, jeton en paramètre", async () => {
    const seen: string[] = [];
    const f = (async (u: URL) => { seen.push(String(u)); return new Response("ok"); }) as unknown as typeof fetch;
    const c = new MoodleClient("https://e", "TOK", f);
    await c.download("https://e/webservice/pluginfile.php/1/a.pdf?forcedownload=1");
    await c.download("https://e/pluginfile.php/1/b.pdf");
    expect(seen[0]).toBe("https://e/webservice/pluginfile.php/1/a.pdf?forcedownload=1&token=TOK");
    expect(seen[1]).toBe("https://e/webservice/pluginfile.php/1/b.pdf?token=TOK");
  });
  it("réseau coupé -> transitoire sans fuite du jeton", async () => {
    const f = (async (u: URL) => { throw new Error(`échec ${u}`); }) as unknown as typeof fetch;
    const e = await new MoodleClient("https://e", "SECRET", f).download("https://e/pluginfile.php/a.pdf").catch((x) => x);
    expect(e.isTransient).toBe(true);
    expect(e.message).not.toContain("SECRET");
  });
});

describe("SSO", () => {
  const BASE = "https://eprel.example";
  const id = (p: string) => createHash("md5").update(BASE + p).digest("hex");
  const enc = (s: string) => `moodlemobile://token=${Buffer.from(s).toString("base64")}`;
  it("construit l'URL et vérifie le jeton reçu", () => {
    expect(new URL(buildLaunchUrl(BASE, "p")).searchParams.get("passport")).toBe("p");
    expect(parseTokenUrl(enc(`${id("p")}:::TOK:::PRIV`), BASE, "p")).toEqual({ token: "TOK", privatetoken: "PRIV" });
    expect(newPassport()).not.toBe(newPassport());
  });
  it("refuse un jeton d'une autre demande ou d'un autre site", () => {
    expect(() => parseTokenUrl(enc(`${id("autre")}:::TOK`), BASE, "p")).toThrow(MoodleError);
    expect(() => parseTokenUrl(enc(`${id("p")}:::TOK`), "https://evil", "p")).toThrow(/ne correspond pas/);
  });
});
