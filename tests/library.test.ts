/** Tests sur de VRAIS fichiers dans un dossier temporaire du disque (pas de simulation du système de fichiers). */
import { existsSync, mkdtempSync, readFileSync, renameSync, rmSync, writeFileSync, mkdirSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { openDb, type Db } from "../src/core/db/database.js";
import { Library } from "../src/core/library/library.js";
import { sanitizeName, uniquePath } from "../src/core/library/names.js";

let dir: string, db: Db, lib: Library, course: number;
const bytes = (s: string) => new TextEncoder().encode(s);
const req = (o: Partial<Parameters<Library["importFile"]>[0]> & { data?: string }) => ({
  courseId: course, category: "TD", name: "td1.pdf", remoteKey: "k1", remoteModified: 100, remoteSize: (o.data ?? "v1").length,
  fetchBytes: async () => bytes(o.data ?? "v1"), ...o,
});

beforeEach(() => {
  dir = mkdtempSync(join(tmpdir(), "campus-"));
  db = openDb();
  lib = new Library(db, join(dir, "Campus Manager"));
  course = Number(db.prepare("INSERT INTO courses(remote_id,name,semester,folder) VALUES(1,'Programmation C','Semestre 1','Programmation C')").run().lastInsertRowid);
});
afterEach(() => rmSync(dir, { recursive: true, force: true }));

describe("noms Windows", () => {
  it("nettoie les caractères interdits et les noms réservés", () => {
    expect(sanitizeName('TD1: "a/b"?.pdf')).toBe("TD1_ _a_b__.pdf");
    expect(sanitizeName("con.txt")).toBe("_con.txt");
    expect(sanitizeName("fin.")).toBe("fin");
    expect(sanitizeName("a".repeat(300) + ".pdf").length).toBeLessThanOrEqual(120);
  });
  it("uniquePath ajoute (2), (3)", () => {
    writeFileSync(join(dir, "a.pdf"), "x"); writeFileSync(join(dir, "a (2).pdf"), "x");
    expect(uniquePath(dir, "a.pdf")).toBe(join(dir, "a (3).pdf"));
  });
});

describe("Library", () => {
  it("écrit réellement le fichier dans Semestre/Matière/Catégorie", async () => {
    const r = await lib.importFile(req({}));
    expect(r.status).toBe("added");
    expect(r.path).toBe(join("Semestre 1", "Programmation C", "TD", "td1.pdf"));
    expect(readFileSync(lib.abs(r.path), "utf8")).toBe("v1");
  });

  it("ne retélécharge pas un fichier inchangé", async () => {
    await lib.importFile(req({}));
    let downloaded = false;
    const r = await lib.importFile(req({ fetchBytes: async () => { downloaded = true; return bytes("v1"); } }));
    expect(r.status).toBe("unchanged");
    expect(downloaded).toBe(false);
  });

  it("met à jour un fichier modifié côté EPREL si l'utilisateur n'y a pas touché", async () => {
    const a = await lib.importFile(req({}));
    const r = await lib.importFile(req({ data: "v2!", remoteModified: 200 }));
    expect(r.status).toBe("updated");
    expect(readFileSync(lib.abs(a.path), "utf8")).toBe("v2!");
  });

  it("n'écrase JAMAIS un fichier modifié par l'utilisateur : conflit, version à côté", async () => {
    const a = await lib.importFile(req({}));
    writeFileSync(lib.abs(a.path), "mes annotations");
    const r = await lib.importFile(req({ data: "v2!", remoteModified: 200 }));
    expect(r.status).toBe("conflict");
    expect(readFileSync(lib.abs(a.path), "utf8")).toBe("mes annotations");
    expect(readFileSync(lib.abs(r.path), "utf8")).toBe("v2!");
    expect(r.path).toContain("version EPREL");
  });

  it("évite les doublons de contenu dans une même matière/catégorie", async () => {
    const a = await lib.importFile(req({}));
    const b = await lib.importFile(req({ remoteKey: "k2", name: "copie.pdf" }));
    expect(b.status).toBe("duplicate");
    expect(b.fileId).toBe(a.fileId);
    expect(existsSync(lib.abs(join("Semestre 1", "Programmation C", "TD", "copie.pdf")))).toBe(false);
    // et la 2e synchronisation ne retélécharge pas non plus
    let dl = false;
    await lib.importFile(req({ remoteKey: "k2", name: "copie.pdf", fetchBytes: async () => { dl = true; return bytes("v1"); } }));
    expect(dl).toBe(false);
  });

  it("rejette un téléchargement incomplet sans rien écrire", async () => {
    await expect(lib.importFile(req({ remoteSize: 50 }))).rejects.toThrow(/incomplet/);
    expect(db.prepare("SELECT COUNT(*) n FROM files").get()).toEqual({ n: 0 });
  });

  it("deux fichiers de même nom mais contenus différents coexistent", async () => {
    const a = await lib.importFile(req({ data: "AAA" }));
    const b = await lib.importFile(req({ data: "BBB", remoteKey: "k2" }));
    expect(b.path).toContain("td1 (2).pdf");
    expect(a.path).not.toBe(b.path);
  });

  it("déplacer et renommer agissent sur le disque ET la base", async () => {
    const a = await lib.importFile(req({}));
    const m = lib.moveFile(a.fileId, { category: "Corrections", subfolder: "Série 1" });
    expect(existsSync(lib.abs(a.path))).toBe(false);
    expect(readFileSync(lib.abs(m.local_path), "utf8")).toBe("v1");
    expect(m.category).toBe("Corrections");
    const n = lib.renameFile(a.fileId, "correction:td1.pdf");
    expect(n.local_path.endsWith("correction_td1.pdf")).toBe(true);
    expect(existsSync(lib.abs(n.local_path))).toBe(true);
  });

  it("retrouve un fichier déplacé dans l'Explorateur Windows (reconcile)", async () => {
    const a = await lib.importFile(req({}));
    const elsewhere = join(lib.root, "Archives", "ancien");
    mkdirSync(elsewhere, { recursive: true });
    renameSync(lib.abs(a.path), join(elsewhere, "renommé.pdf"));
    expect(lib.reconcile()).toEqual({ relinked: 1, missing: 0 });
    expect(lib.getFile(a.fileId).local_path).toBe(join("Archives", "ancien", "renommé.pdf"));
  });

  it("glisser-déposer : un fichier extérieur est COPIÉ dans la matière (l'original reste)", () => {
    const ext = join(dir, "mon_compte_rendu.docx");
    writeFileSync(ext, "CR");
    const r = lib.drop([ext], { courseId: course });
    expect(r).toMatchObject({ imported: 1, moved: 0 });
    expect(readFileSync(lib.abs(join("Semestre 1", "Programmation C", "Personnel", "mon_compte_rendu.docx")), "utf8")).toBe("CR");
    expect(existsSync(ext)).toBe(true);
    // déposé une 2e fois au même endroit : doublon évité
    expect(lib.drop([ext], { courseId: course }).duplicates).toBe(1);
  });

  it("glisser-déposer : un fichier de la bibliothèque est DÉPLACÉ (pas copié)", async () => {
    const a = await lib.importFile(req({}));
    const other = Number(db.prepare("INSERT INTO courses(remote_id,name,semester,folder) VALUES(2,'Maths','Semestre 1','Maths')").run().lastInsertRowid);
    // sur une catégorie : change de catégorie
    expect(lib.drop([lib.abs(a.path)], { courseId: course, category: "TP" }).moved).toBe(1);
    expect(lib.getFile(a.fileId).category).toBe("TP");
    // sur une autre matière sans catégorie : garde sa catégorie
    const r = lib.drop([lib.abs(lib.getFile(a.fileId).local_path)], { courseId: other });
    expect(r.moved).toBe(1);
    expect(lib.getFile(a.fileId).local_path).toBe(join("Semestre 1", "Maths", "TP", "td1.pdf"));
    expect(db.prepare("SELECT COUNT(*) n FROM files").get()).toEqual({ n: 1 });
  });

  it("glisser-déposer : dossiers et fichiers introuvables ignorés et signalés", () => {
    mkdirSync(join(dir, "un dossier"));
    const r = lib.drop([join(dir, "un dossier"), join(dir, "absent.pdf")], { courseId: course });
    expect(r.skipped).toEqual(["un dossier (dossier)", "absent.pdf (introuvable)"]);
  });

  it("signale (sans supprimer) un fichier réellement disparu", async () => {
    const a = await lib.importFile(req({}));
    rmSync(lib.abs(a.path));
    expect(lib.reconcile()).toEqual({ relinked: 0, missing: 1 });
    expect(lib.getFile(a.fileId).missing).toBe(1);
  });
});
