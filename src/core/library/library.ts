/**
 * Bibliothèque physique : les fichiers existent réellement dans l'arborescence
 *   <racine>/<Semestre>/<Matière>/<Catégorie>/<fichier>
 * et la base ne fait que les indexer. Toute opération (import, déplacement,
 * renommage) agit d'abord sur le disque, puis met la base à jour.
 *
 * Garanties :
 *  - écriture atomique (fichier .part, vérifié, puis renommé) ;
 *  - un fichier modifié par l'utilisateur n'est JAMAIS écrasé : la nouvelle version
 *    d'EPREL est enregistrée à côté et signalée en « conflit » ;
 *  - rien n'est retéléchargé si la taille et la date côté EPREL n'ont pas changé ;
 *  - un fichier déplacé hors de l'appli est retrouvé par son empreinte (reconcile).
 */
import { createHash } from "node:crypto";
import { copyFileSync, existsSync, mkdirSync, readdirSync, readFileSync, renameSync, rmdirSync, rmSync, statSync, writeFileSync } from "node:fs";
import { basename, dirname, extname, isAbsolute, join, relative } from "node:path";
import type { Db } from "../db/database.js";
import { sanitizeName, uniquePath } from "./names.js";

export type ImportStatus = "added" | "unchanged" | "updated" | "conflict" | "duplicate";
export interface ImportResult { status: ImportStatus; fileId: number; path: string }

export interface ImportRequest {
  courseId: number;
  resourceId?: number;
  category: string;
  name: string;
  remoteKey?: string;            // identité côté EPREL ; absent = fichier personnel
  remoteModified?: number;
  remoteSize?: number;
  /** Appelé seulement si le téléchargement est nécessaire. */
  fetchBytes: () => Promise<Uint8Array>;
}

export interface FileRow {
  id: number; course_id: number; category: string; local_path: string; original_name: string;
  size: number; sha256: string; downloaded_sha256: string | null; remote_modified: number | null;
  remote_size: number | null; remote_key: string | null; missing: number;
}

export const sha256 = (data: Uint8Array) => createHash("sha256").update(data).digest("hex");

export class Library {
  constructor(private readonly db: Db, readonly root: string) {
    mkdirSync(root, { recursive: true });
    db.exec("CREATE TABLE IF NOT EXISTS remote_aliases(remote_key TEXT PRIMARY KEY, file_id INTEGER NOT NULL, remote_modified INTEGER, remote_size INTEGER)");
  }

  abs(rel: string) { return join(this.root, rel); }
  private rel(abs: string) { return relative(this.root, abs); }

  private course(courseId: number) {
    const c = this.db.prepare("SELECT semester, folder FROM courses WHERE id=?").get(courseId) as { semester: string; folder: string } | undefined;
    if (!c) throw new Error(`Matière inconnue (${courseId})`);
    return c;
  }

  /** Crée (si besoin) et retourne le dossier réel d'une matière/catégorie. */
  courseDir(courseId: number, category: string, subfolder?: string): string {
    const c = this.course(courseId);
    const parts = [sanitizeName(c.semester), sanitizeName(c.folder), sanitizeName(category)];
    if (subfolder) parts.push(...subfolder.split(/[\\/]/).filter(Boolean).map((p) => sanitizeName(p)));
    const dir = join(this.root, ...parts);
    mkdirSync(dir, { recursive: true });
    return dir;
  }

  getFile(id: number): FileRow {
    const f = this.db.prepare("SELECT * FROM files WHERE id=?").get(id) as FileRow | undefined;
    if (!f) throw new Error(`Fichier inconnu (${id})`);
    return f;
  }

  private writeAtomic(dest: string, bytes: Uint8Array) {
    const tmp = `${dest}.part`;
    writeFileSync(tmp, bytes);
    if (statSync(tmp).size !== bytes.byteLength) { rmSync(tmp, { force: true }); throw new Error("Écriture incomplète"); }
    renameSync(tmp, dest);
  }

  isLocallyModified(f: FileRow): boolean {
    const p = this.abs(f.local_path);
    return !!f.downloaded_sha256 && existsSync(p) && sha256(readFileSync(p)) !== f.downloaded_sha256;
  }

  async importFile(req: ImportRequest): Promise<ImportResult> {
    const now = Math.floor(Date.now() / 1000);
    if (req.remoteKey) {
      const alias = this.db.prepare("SELECT * FROM remote_aliases WHERE remote_key=?").get(req.remoteKey) as
        { file_id: number; remote_modified: number | null; remote_size: number | null } | undefined;
      if (alias && alias.remote_modified === (req.remoteModified ?? null) && alias.remote_size === (req.remoteSize ?? null)) {
        return { status: "duplicate", fileId: alias.file_id, path: this.getFile(alias.file_id).local_path };
      }
    }
    const existing = req.remoteKey
      ? (this.db.prepare("SELECT * FROM files WHERE remote_key=?").get(req.remoteKey) as FileRow | undefined) : undefined;

    // Supprimé volontairement par l'utilisateur : on respecte son choix, pas de nouveau téléchargement.
    if (existing && (existing as FileRow & { user_deleted?: number }).user_deleted) {
      return { status: "unchanged", fileId: existing.id, path: existing.local_path };
    }
    if (existing && !existing.missing && existing.remote_modified === (req.remoteModified ?? null)
        && existing.remote_size === (req.remoteSize ?? null)) {
      return { status: "unchanged", fileId: existing.id, path: existing.local_path };
    }

    const bytes = await req.fetchBytes();
    if (req.remoteSize != null && bytes.byteLength !== req.remoteSize) {
      throw new Error(`Téléchargement incomplet : ${bytes.byteLength}/${req.remoteSize} octets`);
    }
    const hash = sha256(bytes);

    if (existing) {
      const dest = this.abs(existing.local_path);
      if (hash === existing.downloaded_sha256 && !existing.missing) {
        this.db.prepare("UPDATE files SET remote_modified=?, remote_size=? WHERE id=?").run(req.remoteModified ?? null, req.remoteSize ?? null, existing.id);
        return { status: "unchanged", fileId: existing.id, path: existing.local_path };
      }
      if (!existing.missing && this.isLocallyModified(existing)) {
        // Conflit : on garde la version de l'utilisateur et on range l'autre à côté.
        const d = new Date((req.remoteModified ?? now) * 1000).toISOString().slice(0, 10);
        const ext = extname(existing.original_name);
        const side = uniquePath(dirname(dest), `${basename(existing.original_name, ext)} (version EPREL ${d})${ext}`);
        mkdirSync(dirname(side), { recursive: true });
        this.writeAtomic(side, bytes);
        const r = this.db.prepare(
          `INSERT INTO files(resource_id,course_id,category,remote_key,local_path,original_name,size,sha256,downloaded_sha256,remote_modified,remote_size,added_at)
           VALUES(?,?,?,?,?,?,?,?,?,?,?,?)`).run(req.resourceId ?? null, existing.course_id, existing.category,
          `${req.remoteKey}#${req.remoteModified}`, this.rel(side), basename(side), bytes.byteLength, hash, hash,
          req.remoteModified ?? null, req.remoteSize ?? null, now);
        return { status: "conflict", fileId: Number(r.lastInsertRowid), path: this.rel(side) };
      }
      mkdirSync(dirname(dest), { recursive: true });
      this.writeAtomic(dest, bytes);
      this.db.prepare("UPDATE files SET size=?, sha256=?, downloaded_sha256=?, remote_modified=?, remote_size=?, missing=0, remote_deleted=0 WHERE id=?")
        .run(bytes.byteLength, hash, hash, req.remoteModified ?? null, req.remoteSize ?? null, existing.id);
      return { status: "updated", fileId: existing.id, path: existing.local_path };
    }

    // Même contenu déjà présent dans cette matière/catégorie : pas de second exemplaire.
    const dup = this.db.prepare("SELECT * FROM files WHERE course_id=? AND category=? AND sha256=? AND missing=0 LIMIT 1")
      .get(req.courseId, req.category, hash) as FileRow | undefined;
    if (dup) {
      if (req.remoteKey) {
        this.db.prepare("INSERT OR REPLACE INTO remote_aliases(remote_key,file_id,remote_modified,remote_size) VALUES(?,?,?,?)")
          .run(req.remoteKey, dup.id, req.remoteModified ?? null, req.remoteSize ?? null);
      }
      return { status: "duplicate", fileId: dup.id, path: dup.local_path };
    }

    const dir = this.courseDir(req.courseId, req.category);
    const dest = uniquePath(dir, sanitizeName(req.name));
    this.writeAtomic(dest, bytes);
    const r = this.db.prepare(
      `INSERT INTO files(resource_id,course_id,category,remote_key,local_path,original_name,size,sha256,downloaded_sha256,remote_modified,remote_size,added_at)
       VALUES(?,?,?,?,?,?,?,?,?,?,?,?)`).run(req.resourceId ?? null, req.courseId, req.category, req.remoteKey ?? null,
      this.rel(dest), req.name, bytes.byteLength, hash, req.remoteKey ? hash : null, req.remoteModified ?? null,
      req.remoteSize ?? null, now);
    return { status: "added", fileId: Number(r.lastInsertRowid), path: this.rel(dest) };
  }

  /** Déplace un fichier (autre catégorie, matière ou sous-dossier) : disque d'abord, base ensuite. */
  moveFile(id: number, to: { courseId?: number; category?: string; subfolder?: string }): FileRow {
    const f = this.getFile(id);
    const courseId = to.courseId ?? f.course_id;
    const category = to.category ?? f.category;
    const dest = uniquePath(this.courseDir(courseId, category, to.subfolder), basename(f.local_path));
    this.moveOnDisk(this.abs(f.local_path), dest);
    this.db.prepare("UPDATE files SET course_id=?, category=?, local_path=? WHERE id=?").run(courseId, category, this.rel(dest), id);
    // Correction manuelle de classement : retenue pour les prochaines synchronisations.
    if (to.category) this.db.prepare("UPDATE resources SET category=?, category_source='manual' WHERE id=(SELECT resource_id FROM files WHERE id=?)").run(category, id);
    return this.getFile(id);
  }

  renameFile(id: number, newName: string): FileRow {
    const f = this.getFile(id);
    const dest = uniquePath(dirname(this.abs(f.local_path)), sanitizeName(newName));
    this.moveOnDisk(this.abs(f.local_path), dest);
    this.db.prepare("UPDATE files SET local_path=? WHERE id=?").run(this.rel(dest), id);
    return this.getFile(id);
  }

  /** Fichier de la bibliothèque situé à ce chemin absolu (comparaison insensible à la casse, comme Windows). */
  findByAbsPath(abs: string): FileRow | undefined {
    const rel = relative(this.root, abs);
    if (rel.startsWith("..") || isAbsolute(rel)) return undefined;
    return this.db.prepare("SELECT * FROM files WHERE lower(local_path)=lower(?) AND user_deleted=0").get(rel) as FileRow | undefined;
  }

  /** Copie un fichier personnel (glissé depuis Windows) dans la bibliothèque. L'original n'est pas touché. */
  importLocal(src: string, courseId: number, category: string): ImportResult {
    const bytes = readFileSync(src);
    const hash = sha256(bytes);
    const dup = this.db.prepare("SELECT * FROM files WHERE course_id=? AND category=? AND sha256=? AND missing=0 AND user_deleted=0 LIMIT 1")
      .get(courseId, category, hash) as FileRow | undefined;
    if (dup) return { status: "duplicate", fileId: dup.id, path: dup.local_path };
    const dest = uniquePath(this.courseDir(courseId, category), sanitizeName(basename(src)));
    this.writeAtomic(dest, bytes);
    const r = this.db.prepare(
      `INSERT INTO files(course_id,category,local_path,original_name,size,sha256,added_at) VALUES(?,?,?,?,?,?,?)`)
      .run(courseId, category, this.rel(dest), basename(src), bytes.byteLength, hash, Math.floor(Date.now() / 1000));
    return { status: "added", fileId: Number(r.lastInsertRowid), path: this.rel(dest) };
  }

  /**
   * Dépôt de fichiers sur une matière (et éventuellement une catégorie) :
   *  - fichier déjà dans la bibliothèque -> déplacé (garde sa catégorie si aucune n'est visée) ;
   *  - fichier extérieur -> copié comme document personnel (catégorie visée, sinon « Personnel »).
   * Les dossiers sont ignorés (signalés).
   */
  drop(paths: string[], target: { courseId: number; category?: string }): { imported: number; moved: number; duplicates: number; skipped: string[] } {
    const out = { imported: 0, moved: 0, duplicates: 0, skipped: [] as string[] };
    this.course(target.courseId); // vérifie que la matière existe
    for (const p of paths) {
      const name = basename(p);
      try {
        if (!existsSync(p)) { out.skipped.push(`${name} (introuvable)`); continue; }
        if (statSync(p).isDirectory()) { out.skipped.push(`${name} (dossier)`); continue; }
        const known = this.findByAbsPath(p);
        if (known) {
          const category = target.category ?? known.category;
          if (known.course_id === target.courseId && known.category === category) continue; // déjà au bon endroit
          this.moveFile(known.id, { courseId: target.courseId, category: category !== known.category ? category : undefined });
          out.moved++;
        } else {
          const r = this.importLocal(p, target.courseId, target.category ?? "Personnel");
          if (r.status === "duplicate") out.duplicates++; else out.imported++;
        }
      } catch (e) { out.skipped.push(`${name} (${(e as Error).message})`); }
    }
    return out;
  }

  /** Après mise à la corbeille Windows (faite par l'appelant) : l'entrée est conservée mais masquée. */
  markDeleted(id: number): void {
    this.db.prepare("UPDATE files SET user_deleted=1 WHERE id=?").run(id);
  }

  setFavorite(id: number, on: boolean): void {
    this.db.prepare("UPDATE files SET favorite=? WHERE id=?").run(on ? 1 : 0, id);
  }

  createFolder(courseId: number, category: string, subfolder: string): string {
    return this.rel(this.courseDir(courseId, category, subfolder));
  }

  /** Change le semestre et/ou le nom de dossier d'une matière : tous ses fichiers sont déplacés sur le disque. */
  relocateCourse(courseId: number, semester: string, folder: string): void {
    const old = this.course(courseId);
    const oldDir = join(this.root, sanitizeName(old.semester), sanitizeName(old.folder));
    const files = this.db.prepare("SELECT id, local_path FROM files WHERE course_id=?").all(courseId) as { id: number; local_path: string }[];
    this.db.prepare("UPDATE courses SET semester=?, folder=? WHERE id=?").run(semester, folder, courseId);
    const newDir = join(this.root, sanitizeName(semester), sanitizeName(folder));
    for (const f of files) {
      const from = this.abs(f.local_path);
      if (!existsSync(from) || !from.startsWith(oldDir)) continue; // fichiers rangés ailleurs à la main : on n'y touche pas
      const to = uniquePath(join(newDir, dirname(relative(oldDir, from))), basename(from));
      this.moveOnDisk(from, to);
      this.db.prepare("UPDATE files SET local_path=? WHERE id=?").run(this.rel(to), f.id);
    }
    this.removeEmptyDirs(oldDir);
    this.removeEmptyDirs(dirname(oldDir));
  }

  private removeEmptyDirs(dir: string) {
    if (!existsSync(dir) || dir === this.root) return;
    for (const e of readdirSync(dir, { withFileTypes: true })) if (e.isDirectory()) this.removeEmptyDirs(join(dir, e.name));
    if (readdirSync(dir).length === 0) rmdirSync(dir); // ne supprime qu'un dossier VIDE
  }

  private moveOnDisk(from: string, to: string) {
    mkdirSync(dirname(to), { recursive: true });
    try { renameSync(from, to); }
    catch { copyFileSync(from, to); rmSync(from); } // autre volume
  }

  /**
   * Retrouve les fichiers déplacés/renommés depuis l'Explorateur Windows (par taille + empreinte)
   * et marque ceux qui ont vraiment disparu. Ne supprime rien.
   */
  reconcile(): { relinked: number; missing: number } {
    const rows = this.db.prepare("SELECT * FROM files").all() as unknown as FileRow[];
    const lost = rows.filter((f) => !existsSync(this.abs(f.local_path)));
    let relinked = 0, missing = 0;
    if (lost.length) {
      const bySize = new Map<number, string[]>();
      const walk = (d: string) => {
        for (const e of readdirSync(d, { withFileTypes: true })) {
          const p = join(d, e.name);
          if (e.isDirectory()) walk(p);
          else if (!e.name.endsWith(".part")) {
            const s = statSync(p).size;
            (bySize.get(s) ?? bySize.set(s, []).get(s)!).push(p);
          }
        }
      };
      walk(this.root);
      const taken = new Set(rows.filter((f) => existsSync(this.abs(f.local_path))).map((f) => f.local_path));
      for (const f of lost) {
        const hit = (bySize.get(f.size) ?? []).find((p) => !taken.has(this.rel(p)) && sha256(readFileSync(p)) === f.sha256);
        if (hit) {
          taken.add(this.rel(hit));
          this.db.prepare("UPDATE files SET local_path=?, missing=0 WHERE id=?").run(this.rel(hit), f.id);
          relinked++;
        } else {
          this.db.prepare("UPDATE files SET missing=1 WHERE id=?").run(f.id);
          missing++;
        }
      }
    }
    for (const f of rows) if (f.missing && existsSync(this.abs(f.local_path))) this.db.prepare("UPDATE files SET missing=0 WHERE id=?").run(f.id);
    return { relinked, missing };
  }
}
