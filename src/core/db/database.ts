/** Base SQLite locale (module `node:sqlite` intégré : aucune dépendance native). */
import { DatabaseSync } from "node:sqlite";

export type Db = DatabaseSync;

const SCHEMA = `
CREATE TABLE IF NOT EXISTS settings(key TEXT PRIMARY KEY, value TEXT NOT NULL);

CREATE TABLE IF NOT EXISTS courses(
  id INTEGER PRIMARY KEY,
  remote_id INTEGER UNIQUE,            -- id Moodle ; NULL pour une matière créée à la main
  name TEXT NOT NULL,
  shortname TEXT,
  semester TEXT NOT NULL DEFAULT 'Autres matières',
  folder TEXT NOT NULL                 -- nom du dossier de la matière sur le disque
);

CREATE TABLE IF NOT EXISTS resources(
  id INTEGER PRIMARY KEY,
  course_id INTEGER NOT NULL REFERENCES courses(id),
  remote_id INTEGER,                   -- id du module Moodle
  name TEXT NOT NULL,
  modname TEXT,
  section TEXT,
  description TEXT,
  category TEXT NOT NULL,
  category_source TEXT NOT NULL DEFAULT 'auto',  -- 'auto' | 'manual'
  confidence REAL NOT NULL DEFAULT 0,
  published_at INTEGER,
  remote_deleted INTEGER NOT NULL DEFAULT 0,
  UNIQUE(course_id, remote_id)
);

CREATE TABLE IF NOT EXISTS files(
  id INTEGER PRIMARY KEY,
  resource_id INTEGER REFERENCES resources(id),
  course_id INTEGER REFERENCES courses(id),
  category TEXT NOT NULL,
  remote_key TEXT UNIQUE,              -- identité côté EPREL (NULL = fichier personnel)
  local_path TEXT NOT NULL,            -- relatif à la racine de la bibliothèque
  original_name TEXT NOT NULL,
  size INTEGER NOT NULL,
  sha256 TEXT NOT NULL,                -- empreinte actuelle connue
  downloaded_sha256 TEXT,              -- empreinte au moment du téléchargement
  remote_modified INTEGER,
  remote_size INTEGER,
  favorite INTEGER NOT NULL DEFAULT 0,
  missing INTEGER NOT NULL DEFAULT 0,
  remote_deleted INTEGER NOT NULL DEFAULT 0,
  added_at INTEGER NOT NULL
);
CREATE TABLE IF NOT EXISTS assignments(
  id INTEGER PRIMARY KEY,
  remote_id INTEGER UNIQUE,            -- NULL = tâche personnelle
  course_id INTEGER REFERENCES courses(id),
  name TEXT NOT NULL,
  description TEXT,
  published_at INTEGER,
  due_at INTEGER,                      -- date limite réelle donnée par EPREL (NULL = aucune)
  status TEXT NOT NULL DEFAULT 'todo', -- statut PERSONNEL : todo | doing | done (jamais envoyé à EPREL)
  priority INTEGER NOT NULL DEFAULT 0,
  notes TEXT,
  remote_deleted INTEGER NOT NULL DEFAULT 0,
  remote_modified INTEGER
);

CREATE TABLE IF NOT EXISTS sync_log(
  id INTEGER PRIMARY KEY, started_at INTEGER NOT NULL, finished_at INTEGER,
  ok INTEGER NOT NULL DEFAULT 0, summary TEXT
);
CREATE INDEX IF NOT EXISTS files_sha ON files(sha256);
CREATE INDEX IF NOT EXISTS files_course ON files(course_id, category);
`;

export function openDb(path = ":memory:"): Db {
  const db = new DatabaseSync(path);
  db.exec("PRAGMA journal_mode = WAL; PRAGMA foreign_keys = ON;");
  db.exec(SCHEMA);
  // Migrations douces des bases existantes.
  const cols = (db.prepare("PRAGMA table_info(courses)").all() as { name: string }[]).map((c) => c.name);
  if (!cols.includes("layout_manual")) db.exec("ALTER TABLE courses ADD COLUMN layout_manual INTEGER NOT NULL DEFAULT 0");
  const fcols = (db.prepare("PRAGMA table_info(files)").all() as { name: string }[]).map((c) => c.name);
  // Fichier mis à la corbeille par l'utilisateur : la sync ne doit pas le retélécharger.
  if (!fcols.includes("user_deleted")) db.exec("ALTER TABLE files ADD COLUMN user_deleted INTEGER NOT NULL DEFAULT 0");
  if (!cols.includes("startdate")) db.exec("ALTER TABLE courses ADD COLUMN startdate INTEGER");
  const acols = (db.prepare("PRAGMA table_info(assignments)").all() as { name: string }[]).map((c) => c.name);
  for (const [c, def] of [["cmid", "INTEGER"], ["base_due_at", "INTEGER"], ["submitted", "INTEGER NOT NULL DEFAULT 0"],
    ["status_source", "TEXT NOT NULL DEFAULT 'auto'"]] as const) {
    if (!acols.includes(c)) db.exec(`ALTER TABLE assignments ADD COLUMN ${c} ${def}`);
  }
  return db;
}
