/**
 * Services du processus principal : base, bibliothèque, synchronisation.
 * Un seul propriétaire de la base (ce processus) ; l'interface passe par l'IPC.
 */
import { app, BrowserWindow } from "electron";
import { appendFileSync, mkdirSync } from "node:fs";
import { join } from "node:path";
import { openDb, type Db } from "../core/db/database.js";
import { Library } from "../core/library/library.js";
import { syncAll } from "../core/sync/engine.js";
import { MoodleClient, MoodleError } from "../core/sync/moodle.js";
import type { SyncState } from "../shared/api.js";
import { ssoLogin } from "./auth-window.js";
import { clearToken, loadToken, saveToken } from "./token-store.js";

export const EPREL = process.env.EPREL_URL ?? "https://eprel.u-pec.fr";

export class Services {
  readonly db: Db;
  readonly lib: Library;
  readonly dataDir: string;
  state: SyncState = { running: false, message: "", lastAt: null, needsLogin: false };
  private listeners = new Set<(s: SyncState) => void>();

  constructor() {
    this.dataDir = app.getPath("userData");
    mkdirSync(join(this.dataDir, "logs"), { recursive: true });
    this.db = openDb(join(this.dataDir, "library.db"));
    const saved = this.db.prepare("SELECT value FROM settings WHERE key='root'").get() as { value: string } | undefined;
    const root = saved?.value ?? process.env.CAMPUS_ROOT ?? join(app.getPath("documents"), "Campus Manager");
    this.lib = new Library(this.db, root);
    this.db.prepare("INSERT OR IGNORE INTO settings(key,value) VALUES('root',?)").run(root);
    const last = this.db.prepare("SELECT finished_at FROM sync_log WHERE finished_at IS NOT NULL ORDER BY id DESC LIMIT 1").get() as { finished_at: number } | undefined;
    this.state.lastAt = last?.finished_at ?? null;
    // Au démarrage : retrouver les fichiers déplacés dans l'Explorateur pendant que l'appli était fermée.
    try { const r = this.lib.reconcile(); if (r.relinked || r.missing) this.log(`reconcile : ${r.relinked} retrouvés, ${r.missing} absents`); }
    catch (e) { this.log(`reconcile : ${(e as Error).message}`); }
  }

  /** Journal technique local (jamais de jeton). */
  log(msg: string) {
    appendFileSync(join(this.dataDir, "logs", "campus.log"), `${new Date().toISOString()} ${msg}\n`);
  }

  onState(cb: (s: SyncState) => void) { this.listeners.add(cb); return () => this.listeners.delete(cb); }
  private set(p: Partial<SyncState>) {
    this.state = { ...this.state, ...p };
    for (const l of this.listeners) l(this.state);
    for (const w of BrowserWindow.getAllWindows()) if (!w.isDestroyed()) w.webContents.send("campus:syncState", this.state);
  }

  async sync(relogin = false): Promise<SyncState> {
    if (this.state.running) return this.state;
    this.set({ running: true, message: "Connexion à EPREL…", needsLogin: false });
    try {
      let token = relogin ? null : loadToken();
      if (token) {
        try { await new MoodleClient(EPREL, token).siteInfo(); }
        catch (e) { if (e instanceof MoodleError && e.isAuthError) { clearToken(); token = null; } else throw e; }
      }
      if (!token) {
        this.set({ message: "Connecte-toi dans la fenêtre UPEC…" });
        token = (await ssoLogin(EPREL, (s) => this.log(`sso ${s}`))).token;
        saveToken(token);
      }
      this.set({ message: "Synchronisation des cours…" });
      const rep = await syncAll(new MoodleClient(EPREL, token), this.db, this.lib);
      this.log(`sync : ${rep.added} ajoutés, ${rep.updated} maj, ${rep.unchanged} inchangés, ${rep.errors.length} erreurs`);
      for (const e of rep.errors) this.log(`sync erreur : ${e}`);
      const msg = rep.authExpired ? "Session EPREL expirée : reconnecte-toi."
        : `${rep.added} nouveau(x) fichier(s), ${rep.updated} mis à jour${rep.errors.length ? `, ${rep.errors.length} erreur(s)` : ""}${rep.conflicts.length ? `, ${rep.conflicts.length} conflit(s)` : ""}.`;
      this.set({ running: false, message: msg, lastAt: Math.floor(Date.now() / 1000), needsLogin: rep.authExpired });
    } catch (e) {
      const m = (e as Error).message;
      this.log(`sync échec : ${m}`);
      const offline = e instanceof MoodleError && e.isTransient;
      this.set({ running: false, message: offline ? "Hors ligne : tes fichiers restent disponibles, nouvelle tentative plus tard." : m,
        needsLogin: m === "Connexion annulée" });
    }
    return this.state;
  }
}
