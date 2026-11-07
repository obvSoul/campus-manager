/**
 * Démarrage avec Windows : entrée « Campus Manager » dans
 * HKCU\Software\Microsoft\Windows\CurrentVersion\Run (pas de droits administrateur).
 * Au démarrage du PC, l'appli se lance avec --hidden : seul le widget apparaît.
 */
import { app } from "electron";
import { execFileSync } from "node:child_process";

const RUN_KEY = "HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Run";

function loginItem() {
  // Version installée : l'exécutable seul. Version de développement : electron.exe + dossier du projet.
  const args = app.isPackaged ? ["--hidden"] : [app.getAppPath(), "--hidden"];
  return { path: process.execPath, args, name: "Campus Manager" };
}

export function getAutostart(): boolean {
  const { path, args, name } = loginItem();
  const s = app.getLoginItemSettings({ path, args });
  // L'entrée est enregistrée sous le nom « Campus Manager » : on la cherche par ce nom.
  return s.openAtLogin || (s.launchItems ?? []).some((i) => i.name === name && i.enabled);
}

/**
 * Version installée, au lancement : remet l'entrée « Campus Manager » en accord avec le choix de
 * l'utilisateur (réglage `autostart`). Elle peut manquer (une réinstallation passe par le
 * désinstalleur, qui la retire) ou pointer ailleurs (ancienne version de développement).
 * Electron ne voit que les entrées de son propre chemin, d'où la lecture directe du registre.
 */
export function ensureAutostart(wanted: boolean): void {
  if (!wanted || !app.isPackaged || process.platform !== "win32") return;
  let current = "";
  try {
    current = execFileSync("reg", ["query", RUN_KEY, "/v", "Campus Manager"], { encoding: "utf8", windowsHide: true });
  } catch { /* entrée absente : on la recrée */ }
  if (!current.toLowerCase().includes(process.execPath.toLowerCase())) setAutostart(true);
}

export function setAutostart(on: boolean): boolean {
  const it = loginItem();
  app.setLoginItemSettings({ openAtLogin: on, path: it.path, args: it.args, name: it.name });
  return getAutostart();
}
