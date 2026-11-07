/**
 * Premier vrai essai de bout en bout : connexion SSO -> synchronisation -> fichiers réels sur le disque.
 * Lancer avec `npm run sync`. Rapport (sans secret) dans sync-report.txt.
 * `--relogin` force une nouvelle connexion.
 */
import { app } from "electron";
import { mkdirSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { openDb } from "../core/db/database.js";
import { Library } from "../core/library/library.js";
import { syncAll } from "../core/sync/engine.js";
import { MoodleClient, MoodleError } from "../core/sync/moodle.js";
import { ssoLogin } from "./auth-window.js";
import { clearToken, loadToken, saveToken } from "./token-store.js";

const BASE = process.env.EPREL_URL ?? "https://eprel.u-pec.fr";
app.setName("Campus Manager");
app.setPath("userData", join(app.getPath("appData"), "Campus Manager"));

const lines: string[] = [];
const log = (s: string) => { console.log(s); lines.push(s); };

async function run() {
  const data = app.getPath("userData");
  mkdirSync(data, { recursive: true });
  let token = process.argv.includes("--relogin") ? null : loadToken();
  let api: MoodleClient | null = null;

  if (token) {
    api = new MoodleClient(BASE, token);
    try { const i = await api.siteInfo(); log(`✔ Session enregistrée valide (Moodle ${i.release ?? "?"}).`); }
    catch (e) {
      if (e instanceof MoodleError && e.isAuthError) { log("Session expirée : nouvelle connexion."); clearToken(); token = null; }
      else throw e;
    }
  }
  if (!token) {
    log("Ouverture de la fenêtre de connexion UPEC : clique sur « Compte UPEC » et connecte-toi.");
    token = (await ssoLogin(BASE, log)).token;
    saveToken(token);
    log("✔ Jeton reçu et enregistré (chiffré par Windows, non affiché).");
    api = new MoodleClient(BASE, token);
  }

  const info = await api!.siteInfo();
  const have = new Set(info.functions.map((f) => f.name));
  for (const f of ["core_enrol_get_users_courses", "core_course_get_contents", "mod_assign_get_assignments"])
    log(`${have.has(f) ? "✔" : "✘"} fonction ${f}`);

  const root = process.env.CAMPUS_ROOT ?? join(app.getPath("documents"), "Campus Manager");
  const db = openDb(join(data, "library.db"));
  const lib = new Library(db, root);
  log(`Téléchargement vers : ${root}`);
  const rep = await syncAll(api!, db, lib);
  log(`✔ ${rep.courses} cours · ${rep.added} fichiers ajoutés · ${rep.updated} mis à jour · ${rep.unchanged} inchangés · ${rep.duplicates} doublons évités · ${rep.assignments} devoirs`);
  if (rep.conflicts.length) log(`⚠ ${rep.conflicts.length} conflit(s) (versions EPREL enregistrées à côté)`);
  for (const e of rep.errors) log(`✘ ${e}`);
  if (rep.authExpired) log("✘ Jeton refusé : relance avec --relogin.");
  const cats = db.prepare("SELECT c.semester, c.name, f.category, COUNT(*) n FROM files f JOIN courses c ON c.id=f.course_id GROUP BY 1,2,3 ORDER BY 1,2,3").all() as
    { semester: string; name: string; category: string; n: number }[];
  for (const c of cats) log(`   ${c.semester} / ${c.name} / ${c.category} : ${c.n}`);
}

// Fermer la fenêtre de connexion ne doit pas quitter le programme : la synchronisation continue.
app.on("window-all-closed", () => {});

app.whenReady().then(async () => {
  try { await run(); }
  catch (e) { log(`✘ ${(e as Error).message}`); process.exitCode = 1; }
  writeFileSync(join(process.cwd(), "sync-report.txt"), lines.join("\n"), "utf8");
  app.quit();
});
