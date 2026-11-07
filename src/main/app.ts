/**
 * Campus Manager : processus principal (un seul processus).
 *  - La fenêtre principale est créée à la demande et DÉTRUITE à la fermeture (libère la mémoire).
 *  - Le widget et la synchronisation continuent sans elle.
 *  - Ctrl+Alt+C rouvre l'interface ; Ctrl+Alt+W affiche/masque le widget.
 * Options : --hidden (démarrer sans fenêtre principale), --page=<vue>, --screenshot=<png>, --eval=<js> (dév.).
 */
import { app, BrowserWindow, globalShortcut, net, protocol } from "electron";
import { writeFileSync } from "node:fs";
import { join } from "node:path";
import { pathToFileURL } from "node:url";
import { repointAutostart, setAutostart } from "./autostart.js";
import { registerIpc } from "./ipc.js";
import { Services } from "./services.js";
import { Widget } from "./widget.js";

app.setName("Campus Manager");
app.setPath("userData", join(app.getPath("appData"), "Campus Manager"));

protocol.registerSchemesAsPrivileged([{ scheme: "campus", privileges: { standard: true, secure: true, supportFetchAPI: true, stream: true } }]);

const arg = (name: string) => process.argv.find((a) => a.startsWith(`--${name}=`))?.split("=").slice(1).join("=");
const shotMode = !!arg("screenshot");

if (!shotMode && !app.requestSingleInstanceLock()) app.quit();

let main: BrowserWindow | null = null;
let services: Services;
let widget: Widget;

export function openMain(hash?: string) {
  if (main && !main.isDestroyed()) {
    if (hash) main.webContents.executeJavaScript(`location.hash = ${JSON.stringify(hash)}`).catch(() => {});
    if (main.isMinimized()) main.restore();
    main.show(); main.focus();
    return;
  }
  main = new BrowserWindow({
    width: 1280, height: 800, minWidth: 900, minHeight: 560, show: false, backgroundColor: "#070709",
    title: "Campus Manager", autoHideMenuBar: true, titleBarStyle: "hidden", icon: join(__dirname, "..", "build", "icon.png"),
    titleBarOverlay: { color: "#00000000", symbolColor: "#9aa3b2", height: 40 },
    webPreferences: { preload: join(__dirname, "preload.cjs"), contextIsolation: true, sandbox: true, plugins: true },
  });
  const page = hash ?? arg("page");
  void main.loadFile(join(__dirname, "renderer", "index.html"), page ? { hash: page } : undefined);
  main.once("ready-to-show", () => main?.show());
  main.webContents.setWindowOpenHandler(() => ({ action: "deny" }));
  main.webContents.on("will-navigate", (e) => e.preventDefault());
  main.on("closed", () => { main = null; }); // fermer = libérer la fenêtre, PAS quitter l'appli

  if (shotMode) {
    const m = main;
    m.webContents.once("did-finish-load", () => setTimeout(async () => {
      const js = arg("eval");
      if (js) { await m.webContents.executeJavaScript(js).catch(() => {}); await new Promise((r) => setTimeout(r, 2000)); }
      writeFileSync(arg("screenshot")!, (await m.webContents.capturePage()).toPNG());
      app.exit(0);
    }, 2500));
  }
}

app.on("second-instance", () => openMain());

app.whenReady().then(() => {
  services = new Services();
  protocol.handle("campus", (req) => {
    const u = new URL(req.url);
    const fid = Number(u.pathname.replace(/^\//, ""));
    if (u.host !== "file" || !Number.isInteger(fid)) return new Response("Introuvable", { status: 404 });
    try {
      const f = services.lib.getFile(fid); // seuls les fichiers connus de la bibliothèque sont servis
      return net.fetch(pathToFileURL(services.lib.abs(f.local_path)).toString());
    } catch { return new Response("Introuvable", { status: 404 }); }
  });
  widget = new Widget(services);
  registerIpc(services, {
    widget, openMain,
    quit: () => { widget.destroy(); app.exit(0); },
  });

  if (shotMode) { openMain(); return; }

  // Toute autre fenêtre de l'appli qui prend le focus ne doit pas faire remonter le widget.
  app.on("browser-window-focus", (_e, w) => { if (w !== widget.win) { widget.sendBack(); setTimeout(() => widget.sendBack(), 150); } });

  // Démarrage avec Windows activé par défaut (une seule fois) ; désactivable dans Paramètres.
  if (!services.db.prepare("SELECT 1 FROM settings WHERE key='autostart'").get()) {
    const on = setAutostart(true);
    services.db.prepare("INSERT INTO settings(key,value) VALUES('autostart',?)").run(on ? "1" : "0");
    services.log(`démarrage avec Windows : ${on ? "activé" : "échec"}`);
  }

  // Version installée : si le démarrage avec Windows est activé, on repointe l'entrée vers cet exécutable.
  else repointAutostart();

  if (widget.get().enabled) widget.create();
  const wshot = arg("widget-shot"); // développement : capture du widget puis sortie
  if (wshot) setTimeout(async () => { if (widget.win) writeFileSync(wshot, (await widget.win.webContents.capturePage()).toPNG()); app.exit(0); }, 4000);
  if (!process.argv.includes("--hidden")) openMain();

  globalShortcut.register("CommandOrControl+Alt+C", () => openMain());
  globalShortcut.register("CommandOrControl+Alt+W", () => {
    if (widget.win?.isVisible()) widget.hide(); else widget.show();
  });
  // Clic à travers : quand il est actif, le widget ne reçoit plus de clics ; ce raccourci le désactive.
  globalShortcut.register("CommandOrControl+Alt+X", () => widget.update({ clickThrough: !widget.get().clickThrough }));
});

// L'application continue de tourner (widget, synchronisation) quand toutes les fenêtres sont fermées.
app.on("window-all-closed", () => {});
app.on("will-quit", () => globalShortcut.unregisterAll());
