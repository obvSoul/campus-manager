/** Gestionnaires IPC : chaque entrée est validée ; toute action agit sur le disque ET sur la base. */
import { BrowserWindow, clipboard, dialog, ipcMain, nativeImage, shell } from "electron";
import { existsSync, statSync } from "node:fs";
import { isAbsolute, join } from "node:path";
import { dashboard, listAssignments, listFiles, setAssignment, toFileItem, tree, widgetData } from "../core/queries.js";
import type { FileDetails, FileQuery, WidgetSettings } from "../shared/api.js";
import type { Services } from "./services.js";
import type { Widget } from "./widget.js";
import { getAutostart, setAutostart } from "./autostart.js";

const id = (v: unknown) => { const n = Number(v); if (!Number.isInteger(n) || n <= 0) throw new Error("Identifiant invalide"); return n; };
const str = (v: unknown, max = 200) => { if (typeof v !== "string" || !v.trim() || v.length > max) throw new Error("Texte invalide"); return v.trim(); };

export interface AppCtx { widget: Widget; openMain: (hash?: string) => void; quit: () => void }

// Actions qui modifient les données : toutes les fenêtres (interface + widget) sont prévenues.
const MUTATING = new Set(["rename", "move", "favorite", "trash", "createFolder", "setAssignment", "drop"]);
export const broadcast = (ch: string, payload?: unknown) => {
  for (const w of BrowserWindow.getAllWindows()) if (!w.isDestroyed()) w.webContents.send(ch, payload);
};

export function registerIpc(s: Services, ctx: AppCtx) {
  const h = (name: string, fn: (...a: unknown[]) => unknown) =>
    ipcMain.handle(`campus:${name}`, async (_e, ...a) => {
      try {
        const r = await fn(...a);
        if (MUTATING.has(name)) broadcast("campus:changed");
        return r;
      } catch (e) { s.log(`ipc ${name} : ${(e as Error).message}`); throw e; }
    });
  s.onState((st) => { if (!st.running) broadcast("campus:changed"); });

  h("widgetData", () => widgetData(s.db));
  h("widgetSettings", () => ctx.widget.get());
  h("setWidgetSettings", (p) => ctx.widget.update((p ?? {}) as Partial<WidgetSettings>));
  h("openMain", (hash) => ctx.openMain(typeof hash === "string" && /^[\w/%.-]*$/.test(hash) ? hash : undefined));
  h("widgetResize", (w, hgt) => ctx.widget.resize(Number(w), Number(hgt)));
  h("drop", (paths, target) => {
    if (!Array.isArray(paths) || paths.length > 500 || !paths.every((p) => typeof p === "string" && isAbsolute(p))) throw new Error("Chemins invalides");
    const t = (target ?? {}) as { courseId?: unknown; category?: unknown };
    const r = s.lib.drop(paths as string[], { courseId: id(t.courseId), category: t.category != null ? str(t.category, 60) : undefined });
    s.log(`dépôt : ${r.imported} importés, ${r.moved} déplacés, ${r.duplicates} doublons, ${r.skipped.length} ignorés`);
    return r;
  });
  // Glisser un fichier HORS de l'appli (Explorateur, Bureau, pièce jointe…) : glisser natif Windows.
  const dragIcon = nativeImage.createFromPath(join(__dirname, "..", "build", "icon.png")).resize({ width: 32, height: 32 });
  ipcMain.on("campus:startDrag", (e, v) => {
    try {
      const abs = s.lib.abs(s.lib.getFile(id(v)).local_path);
      if (existsSync(abs)) e.sender.startDrag({ file: abs, icon: dragIcon });
    } catch (err) { s.log(`startDrag : ${(err as Error).message}`); }
  });
  h("autostart", () => getAutostart());
  h("setAutostart", (on) => {
    const r = setAutostart(!!on);
    s.db.prepare("INSERT OR REPLACE INTO settings(key,value) VALUES('autostart',?)").run(r ? "1" : "0");
    return r;
  });
  h("widget", (cmd) => {
    switch (cmd) {
      case "collapse": ctx.widget.update({ collapsed: true }); break;
      case "expand": ctx.widget.update({ collapsed: false }); break;
      case "hide": ctx.widget.hide(); break;
      case "show": ctx.widget.show(); break;
      case "quit": ctx.quit(); break;
      default: throw new Error("Commande inconnue");
    }
  });
  const item = (fid: number) => {
    const r = listFiles(s.db, { limit: 100000 }).find((f) => f.id === fid);
    if (!r) throw new Error("Fichier introuvable");
    return r;
  };

  h("tree", () => tree(s.db));
  h("files", (q) => listFiles(s.db, (q ?? {}) as FileQuery));
  h("details", (v): FileDetails => {
    const f = s.lib.getFile(id(v));
    const abs = s.lib.abs(f.local_path);
    const r = s.db.prepare(`SELECT f.*, COALESCE(c.folder,'') course, COALESCE(c.semester,'') semester, r.name rname, r.section, r.description
      FROM files f LEFT JOIN courses c ON c.id=f.course_id LEFT JOIN resources r ON r.id=f.resource_id WHERE f.id=?`).get(f.id) as Record<string, unknown>;
    const exists = existsSync(abs);
    return { ...toFileItem(r as never), modifiedAt: exists ? Math.floor(statSync(abs).mtimeMs / 1000) : null,
      locallyModified: exists && s.lib.isLocallyModified(f), resourceName: (r.rname as string) ?? null, section: (r.section as string) ?? null,
      description: (r.description as string) || null, fromEprel: !!f.remote_key, absPath: abs };
  });
  h("open", async (v) => {
    const f = s.lib.getFile(id(v));
    const err = await shell.openPath(s.lib.abs(f.local_path)); // logiciel Windows associé à l'extension
    return err || null;
  });
  h("reveal", (v) => shell.showItemInFolder(s.lib.abs(s.lib.getFile(id(v)).local_path)));
  h("rename", (v, name) => { const fid = id(v); s.lib.renameFile(fid, str(name)); return item(fid); });
  h("move", (v, to) => {
    const fid = id(v);
    const t = (to ?? {}) as { courseId?: unknown; category?: unknown };
    s.lib.moveFile(fid, { courseId: t.courseId != null ? id(t.courseId) : undefined, category: t.category != null ? str(t.category, 60) : undefined });
    return item(fid);
  });
  h("favorite", (v, on) => s.lib.setFavorite(id(v), !!on));
  h("trash", async (v) => {
    const f = s.lib.getFile(id(v));
    const win = BrowserWindow.getFocusedWindow();
    const opts = { type: "warning" as const, buttons: ["Mettre à la corbeille", "Annuler"], defaultId: 1, cancelId: 1,
      message: `Mettre « ${f.original_name} » à la corbeille Windows ?`,
      detail: "Tu pourras le restaurer depuis la Corbeille. La synchronisation ne le retéléchargera pas." };
    const { response } = win ? await dialog.showMessageBox(win, opts) : await dialog.showMessageBox(opts);
    if (response !== 0) return false;
    const abs = s.lib.abs(f.local_path);
    if (existsSync(abs)) await shell.trashItem(abs);
    s.lib.markDeleted(f.id);
    return true;
  });
  h("copyPath", (v) => clipboard.writeText(s.lib.abs(s.lib.getFile(id(v)).local_path)));
  h("createFolder", (c, cat, name) => { s.lib.createFolder(id(c), str(cat, 60), str(name, 120)); });
  h("assignments", () => listAssignments(s.db));
  h("setAssignment", (v, p) => setAssignment(s.db, id(v), (p ?? {}) as never));
  h("dashboard", () => dashboard(s.db));
  h("settings", () => ({ root: s.lib.root, dataDir: s.dataDir }));
  h("openRoot", () => shell.openPath(s.lib.root));
  h("sync", (relogin) => s.sync(!!relogin));
  h("syncState", () => s.state);
}
