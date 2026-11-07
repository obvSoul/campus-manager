/** Requêtes de l'interface sur une vraie base SQLite et de vrais fichiers temporaires. */
import { mkdtempSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { openDb, type Db } from "../src/core/db/database.js";
import { Library } from "../src/core/library/library.js";
import { dashboard, listAssignments, listFiles, setAssignment, tree } from "../src/core/queries.js";

let dir: string, db: Db, lib: Library, c1: number, c2: number;
const add = (courseId: number, category: string, name: string, data: string, key = name) =>
  lib.importFile({ courseId, category, name, remoteKey: key, remoteModified: 1, remoteSize: data.length, fetchBytes: async () => new TextEncoder().encode(data) });

beforeEach(async () => {
  dir = mkdtempSync(join(tmpdir(), "q-"));
  db = openDb(); lib = new Library(db, dir);
  c1 = Number(db.prepare("INSERT INTO courses(remote_id,name,semester,folder) VALUES(1,'BUT INFO : R1.01 - Dev','Semestre 1','R1.01 - Dev')").run().lastInsertRowid);
  c2 = Number(db.prepare("INSERT INTO courses(remote_id,name,semester,folder) VALUES(2,'Infos','Autres matières','Infos')").run().lastInsertRowid);
  await add(c1, "TD", "td1_boucles.pdf", "A");
  await add(c1, "TP", "tp1.pdf", "B");
  await add(c2, "À classer", "guide.pdf", "C");
});
afterEach(() => rmSync(dir, { recursive: true, force: true }));

describe("queries", () => {
  it("arborescence : semestres, matières, catégories avec compteurs, « Autres » en dernier", () => {
    const t = tree(db);
    expect(t.map((s) => s.name)).toEqual(["Semestre 1", "Autres matières"]);
    expect(t[0]!.courses[0]!.categories).toEqual(expect.arrayContaining([{ name: "TD", count: 1 }, { name: "TP", count: 1 }]));
  });
  it("filtres et recherche (nom, matière, catégorie)", () => {
    expect(listFiles(db, { courseId: c1 }).map((f) => f.name)).toEqual(["td1_boucles.pdf", "tp1.pdf"]);
    expect(listFiles(db, { search: "boucles" }).map((f) => f.name)).toEqual(["td1_boucles.pdf"]);
    expect(listFiles(db, { search: "r1.01 tp" }).map((f) => f.name)).toEqual(["tp1.pdf"]);
    expect(listFiles(db, { categories: ["TD", "TP"] })).toHaveLength(2);
  });
  it("favoris et corbeille : un fichier supprimé disparaît et n'est pas retéléchargé", async () => {
    const [f] = listFiles(db, { search: "tp1" });
    lib.setFavorite(f!.id, true);
    expect(listFiles(db, { favorites: true }).map((x) => x.id)).toEqual([f!.id]);
    lib.markDeleted(f!.id);
    expect(listFiles(db, { search: "tp1" })).toEqual([]);
    let dl = false;
    const r = await lib.importFile({ courseId: c1, category: "TP", name: "tp1.pdf", remoteKey: "tp1.pdf", remoteModified: 2, remoteSize: 2, fetchBytes: async () => { dl = true; return new Uint8Array(2); } });
    expect(r.status).toBe("unchanged");
    expect(dl).toBe(false);
  });
  it("déplacer = correction manuelle retenue", () => {
    db.prepare("INSERT INTO resources(id,course_id,remote_id,name,category) VALUES(9,?,99,'guide','À classer')").run(c2);
    const [f] = listFiles(db, { search: "guide" });
    db.prepare("UPDATE files SET resource_id=9 WHERE id=?").run(f!.id);
    lib.moveFile(f!.id, { category: "Cours" });
    expect(db.prepare("SELECT category, category_source FROM resources WHERE id=9").get()).toEqual({ category: "Cours", category_source: "manual" });
  });
  it("année universitaire : début au 15 août ; échéances antérieures = anciens devoirs", async () => {
    const { academicYearStart } = await import("../src/core/queries.js");
    const t = (s: string) => Math.floor(new Date(s).getTime() / 1000);
    expect(academicYearStart(t("2026-11-07T17:00:00"))).toBe(t("2026-08-15T00:00:00"));
    expect(academicYearStart(t("2027-03-01T10:00:00"))).toBe(t("2026-08-15T00:00:00"));
    expect(academicYearStart(t("2026-08-10T10:00:00"))).toBe(t("2025-08-15T00:00:00"));
    db.prepare("INSERT INTO assignments(remote_id,course_id,name,due_at) VALUES(50,?,'TP 2025',?), (51,?,'TD 2026',?)")
      .run(c1, t("2025-10-05T23:59:00"), c1, t("2026-11-07T23:59:00"));
    const as = listAssignments(db, t("2026-11-08T10:00:00"));
    expect(as.map((a) => [a.name, a.old, a.late])).toEqual([["TP 2025", true, false], ["TD 2026", false, true]]);
  });
  it("devoirs : retard calculé depuis la vraie date, statut local", () => {
    const now = 1_000_000;
    db.prepare("INSERT INTO assignments(remote_id,course_id,name,due_at) VALUES(1,?,'Passé',?), (2,?,'Futur',?), (3,?,'Sans date',NULL)").run(c1, now - 10, c1, now + 3600, c1);
    let as = listAssignments(db, now);
    expect(as.map((a) => [a.name, a.late])).toEqual([["Passé", true], ["Futur", false], ["Sans date", false]]);
    setAssignment(db, as[0]!.id, { status: "done" });
    as = listAssignments(db, now);
    expect(as[0]!.late).toBe(false);
    expect(() => setAssignment(db, 1, { status: "x" as never })).toThrow();
    const d = dashboard(db, now);
    expect(d.counts).toMatchObject({ files: 3, td: 1, tp: 1, assignmentsDone: 1, assignmentsOpen: 2, unsorted: 1 });
    expect(d.upcoming.map((a) => a.name)).toEqual(["Futur", "Sans date"]);
  });
});
