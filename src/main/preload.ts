/** Pont sécurisé : l'interface n'a accès qu'à ces fonctions précises (pas de Node, pas de disque direct). */
import { contextBridge, ipcRenderer, webUtils } from "electron";
import type { CampusApi, SyncState, WidgetSettings } from "../shared/api.js";

const call = (ch: string, ...a: unknown[]) => ipcRenderer.invoke(`campus:${ch}`, ...a);

const api: CampusApi = {
  tree: () => call("tree"),
  files: (q) => call("files", q),
  details: (id) => call("details", id),
  open: (id) => call("open", id),
  reveal: (id) => call("reveal", id),
  rename: (id, name) => call("rename", id, name),
  move: (id, to) => call("move", id, to),
  favorite: (id, on) => call("favorite", id, on),
  trash: (id) => call("trash", id),
  copyPath: (id) => call("copyPath", id),
  createFolder: (c, cat, n) => call("createFolder", c, cat, n),
  assignments: () => call("assignments"),
  setAssignment: (id, p) => call("setAssignment", id, p),
  dashboard: () => call("dashboard"),
  settings: () => call("settings"),
  openRoot: () => call("openRoot"),
  sync: (relogin) => call("sync", relogin),
  syncState: () => call("syncState"),
  onSyncState: (cb) => {
    const h = (_e: unknown, s: SyncState) => cb(s);
    ipcRenderer.on("campus:syncState", h);
    return () => ipcRenderer.removeListener("campus:syncState", h);
  },
  previewUrl: (id) => `campus://file/${id}`,
  widgetData: () => call("widgetData"),
  widgetSettings: () => call("widgetSettings"),
  setWidgetSettings: (p) => call("setWidgetSettings", p),
  openMain: (hash) => call("openMain", hash),
  widget: (cmd) => call("widget", cmd),
  widgetResize: (w, h) => call("widgetResize", w, h),
  pathForFile: (f) => webUtils.getPathForFile(f),
  drop: (paths, target) => call("drop", paths, target),
  startDrag: (id) => ipcRenderer.send("campus:startDrag", id),
  autostart: () => call("autostart"),
  setAutostart: (on) => call("setAutostart", on),
  onChanged: (cb) => {
    const h = () => cb();
    ipcRenderer.on("campus:changed", h);
    return () => ipcRenderer.removeListener("campus:changed", h);
  },
  onWidgetSettings: (cb) => {
    const h = (_e: unknown, s: WidgetSettings) => cb(s);
    ipcRenderer.on("campus:widgetSettings", h);
    return () => ipcRenderer.removeListener("campus:widgetSettings", h);
  },
};
contextBridge.exposeInMainWorld("campus", api);
