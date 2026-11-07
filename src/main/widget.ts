/**
 * Widget de bureau : fenêtre sans cadre, transparente, absente de la barre des tâches et du
 * sélecteur Alt+Tab (fenêtre « outil »), rattachée au bureau Windows. Elle vit indépendamment
 * de la fenêtre principale et ne lit que la base locale.
 */
import { BrowserWindow, screen } from "electron";
import { join } from "node:path";
import { DEFAULT_WIDGET, type WidgetSettings } from "../shared/api.js";
import type { Services } from "./services.js";
import { attachToDesktop, hwndOf, isForegroundFullscreen, sendToBottom } from "./win32.js";

const COLLAPSED_H = 46;

export class Widget {
  win: BrowserWindow | null = null;
  private settings: WidgetSettings;
  private fsTimer: NodeJS.Timeout | null = null;
  private hiddenForFullscreen = false;
  private saveTimer: NodeJS.Timeout | null = null;
  attached = false;

  constructor(private s: Services) {
    const raw = s.db.prepare("SELECT value FROM settings WHERE key='widget'").get() as { value: string } | undefined;
    const saved = raw ? (JSON.parse(raw.value) as Partial<WidgetSettings>) : {};
    this.settings = { ...DEFAULT_WIDGET, ...saved, sections: { ...DEFAULT_WIDGET.sections, ...(saved.sections ?? {}) } };
  }

  get(): WidgetSettings { return this.settings; }

  update(p: Partial<WidgetSettings>): WidgetSettings {
    const before = this.settings;
    this.settings = { ...before, ...p, sections: { ...before.sections, ...(p.sections ?? {}) } };
    const op = Number(this.settings.opacity);
    this.settings.opacity = Math.min(1, Math.max(0, Number.isFinite(op) ? op : DEFAULT_WIDGET.opacity));
    this.s.db.prepare("INSERT OR REPLACE INTO settings(key,value) VALUES('widget',?)").run(JSON.stringify(this.settings));
    if (this.settings.enabled && !this.win) this.create();
    if (!this.settings.enabled && this.win) this.destroy();
    if (this.win) {
      if (before.clickThrough !== this.settings.clickThrough) this.win.setIgnoreMouseEvents(this.settings.clickThrough, { forward: true });
      if (before.collapsed !== this.settings.collapsed) this.applyCollapsed();
      if (before.blur !== this.settings.blur) this.win.setBackgroundMaterial(this.settings.blur ? "acrylic" : "none");
      this.win.webContents.send("campus:widgetSettings", this.settings);
    }
    for (const w of BrowserWindow.getAllWindows()) if (w !== this.win && !w.isDestroyed()) w.webContents.send("campus:widgetSettings", this.settings);
    this.watchFullscreen();
    return this.settings;
  }

  private bounds() {
    const raw = this.s.db.prepare("SELECT value FROM settings WHERE key='widgetBounds'").get() as { value: string } | undefined;
    const area = screen.getPrimaryDisplay().workArea;
    const def = { width: 340, height: 520, x: area.x + area.width - 340 - 24, y: area.y + 24 };
    if (!raw) return def;
    const b = JSON.parse(raw.value) as typeof def;
    // Si l'écran a changé (moniteur débranché), on revient à une position visible.
    const visible = screen.getAllDisplays().some((d) => b.x >= d.bounds.x - 50 && b.x < d.bounds.x + d.bounds.width - 50 && b.y >= d.bounds.y - 10 && b.y < d.bounds.y + d.bounds.height - 50);
    return visible ? b : def;
  }

  private saveBounds() {
    if (!this.win || this.settings.collapsed) return;
    if (this.saveTimer) clearTimeout(this.saveTimer);
    this.saveTimer = setTimeout(() => {
      if (!this.win || this.win.isDestroyed()) return;
      this.s.db.prepare("INSERT OR REPLACE INTO settings(key,value) VALUES('widgetBounds',?)").run(JSON.stringify(this.win.getBounds()));
    }, 400);
  }

  create() {
    if (this.win) return;
    const b = this.bounds();
    const win = new BrowserWindow({
      ...b, height: this.settings.collapsed ? COLLAPSED_H : b.height, minWidth: 260, minHeight: COLLAPSED_H,
      frame: false, transparent: true, resizable: true, maximizable: false, minimizable: false, fullscreenable: false,
      skipTaskbar: true, type: "toolbar", hasShadow: false, show: false, title: "Campus Manager – widget",
      backgroundMaterial: this.settings.blur ? "acrylic" : "none",
      webPreferences: { preload: join(__dirname, "preload.cjs"), contextIsolation: true, sandbox: true },
    });
    this.win = win;
    void win.loadFile(join(__dirname, "renderer", "widget.html"));
    win.webContents.setWindowOpenHandler(() => ({ action: "deny" }));
    win.webContents.on("will-navigate", (e) => e.preventDefault());
    win.once("ready-to-show", () => {
      win.showInactive();
      this.attached = attachToDesktop(hwndOf(win.getNativeWindowHandle()));
      this.s.log(`widget : ${this.attached ? "rattaché au bureau" : "intégration bureau indisponible, fenêtre basse"}`);
      if (this.settings.clickThrough) win.setIgnoreMouseEvents(true, { forward: true });
    });
    // Cliquer sur le widget ne doit pas le faire passer devant les autres fenêtres.
    win.on("focus", () => setTimeout(() => !win.isDestroyed() && sendToBottom(hwndOf(win.getNativeWindowHandle())), 30));
    win.on("moved", () => this.saveBounds());
    win.on("resized", () => this.saveBounds());
    // Alt+F4 ou toute fermeture accidentelle ne doit pas faire disparaître le widget :
    // seuls « Quitter Campus Manager » et la désactivation dans les réglages le ferment.
    win.on("close", (e) => { if (!this.allowClose) e.preventDefault(); });
    win.on("closed", () => { this.win = null; });
    this.watchFullscreen();
  }

  private applyCollapsed() {
    if (!this.win) return;
    const b = this.win.getBounds();
    if (this.settings.collapsed) { this.saveBounds(); this.win.setBounds({ ...b, height: COLLAPSED_H }); }
    else this.win.setBounds({ ...b, height: this.bounds().height });
  }

  resize(width: number, height: number) {
    if (!this.win || this.settings.collapsed || !Number.isFinite(width) || !Number.isFinite(height)) return;
    const w = Math.round(Math.min(900, Math.max(260, width))), h = Math.round(Math.min(1400, Math.max(160, height)));
    const b = this.win.getBounds();
    this.win.setBounds({ ...b, width: w, height: h });
    this.saveBounds();
  }

  /** Renvoie le widget au niveau du bureau (Windows remonte toutes les fenêtres d'une appli activée). */
  sendBack() {
    if (this.win && !this.win.isDestroyed()) sendToBottom(hwndOf(this.win.getNativeWindowHandle()));
  }

  private allowClose = false;
  destroy() { this.allowClose = true; this.win?.destroy(); this.win = null; this.allowClose = false; }

  hide() { this.win?.hide(); }
  show() { if (!this.win) this.create(); else this.win.showInactive(); }

  /** Masque le widget quand une application passe en plein écran (jeu, vidéo…). */
  private watchFullscreen() {
    const want = this.settings.enabled && this.settings.autoHideFullscreen;
    if (!want && this.fsTimer) { clearInterval(this.fsTimer); this.fsTimer = null; if (this.hiddenForFullscreen) { this.hiddenForFullscreen = false; this.win?.showInactive(); } }
    if (!want || this.fsTimer) return;
    this.fsTimer = setInterval(() => {
      if (!this.win || this.win.isDestroyed()) return;
      const own = BrowserWindow.getAllWindows().map((w) => hwndOf(w.getNativeWindowHandle()));
      const fs = isForegroundFullscreen(own);
      if (fs && this.win.isVisible()) { this.hiddenForFullscreen = true; this.win.hide(); }
      else if (!fs && this.hiddenForFullscreen) { this.hiddenForFullscreen = false; this.win.showInactive(); }
    }, 1500);
  }
}
