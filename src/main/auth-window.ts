/**
 * Fenêtre de connexion SSO. Elle ne sert QU'À obtenir un jeton : on charge la page de connexion UPEC,
 * on n'injecte rien et on ne lit pas la saisie, on intercepte seulement la redirection finale.
 * Elle se ferme dès que le jeton est reçu. Aucune page EPREL n'est utilisée ensuite.
 */
import { BrowserWindow } from "electron";
import { buildLaunchUrl, isTokenRedirect, newPassport, parseTokenUrl, URL_SCHEME } from "../core/sync/sso.js";

export function ssoLogin(base: string, log: (s: string) => void): Promise<{ token: string; privatetoken?: string }> {
  const passport = newPassport();
  const origin = new URL(base).origin;
  return new Promise((resolve, reject) => {
    const win = new BrowserWindow({
      width: 520, height: 740, title: "Connexion EPREL (UPEC)", autoHideMenuBar: true,
      webPreferences: { partition: "persist:campus-sso", contextIsolation: true, sandbox: true },
    });
    let done = false;
    const finish = (fn: () => void) => { if (!done) { done = true; fn(); if (!win.isDestroyed()) win.destroy(); } };
    const onToken = (url: string) => {
      try { const t = parseTokenUrl(url, base, passport); finish(() => resolve(t)); }
      catch (e) { finish(() => reject(e)); }
    };
    const guard = (e: { preventDefault(): void }, url: string) => { if (isTokenRedirect(url)) { e.preventDefault(); onToken(url); } };

    const wc = win.webContents;
    // Journal : seulement les pages web (origine + chemin). Une URL d'application (moodlemobile://token=...)
    // contient le jeton : elle n'est JAMAIS écrite.
    const path = (url: string) => {
      try {
        const u = new URL(url);
        return u.protocol === "https:" || u.protocol === "http:" ? `${u.origin}${u.pathname.slice(0, 60)}` : "(redirection vers l'application)";
      } catch { return "?"; }
    };
    wc.on("did-start-navigation", (_e, url, _inPlace, isMain) => { if (isMain) log(`  → ${path(url)}`); });
    wc.on("did-redirect-navigation", (_e, url, _inPlace, isMain) => { if (isMain) log(`  ↪ redirection ${path(url)}`); });
    wc.on("did-fail-load", (_e, code, desc, url, isMain) => { if (isMain) log(`  ✘ chargement échoué (${code} ${desc}) ${path(url)}`); });

    let relaunches = 0;
    win.webContents.on("did-navigate", (_e, url) => {
      const u = new URL(url);
      log(`  page : ${u.origin}${u.pathname}`);   // jamais les paramètres (ils peuvent contenir des secrets)
      const loggedInLanding = u.origin === origin && !u.pathname.startsWith("/login") && !u.pathname.includes("/mobile/launch");
      if (loggedInLanding && relaunches < 2) {  // connecté mais resté sur le tableau de bord : on réclame le jeton
        relaunches++;
        log("  connecté : demande du jeton…");
        void win.loadURL(buildLaunchUrl(base, passport));
      }
    });
    win.webContents.on("did-finish-load", async () => {
      const u = new URL(win.webContents.getURL());
      if (!u.pathname.includes("/mobile/launch")) return;
      // Si Moodle affiche une page au lieu de rediriger, on cherche le lien du jeton dans la page.
      const info = await win.webContents.executeJavaScript(`({
        title: document.title,
        text: document.body.innerText.slice(0, 160).replace(/\\s+/g, " "),
        link: (document.querySelector('a[href^="${URL_SCHEME}://"]') || {}).href || null
      })`).catch(() => null);
      if (!info) return;
      log(`  page jeton : « ${info.title} » | ${info.text} | lien : ${info.link ? "oui" : "non"}`);
      if (info.link) onToken(info.link);
    });
    win.webContents.on("will-redirect", guard);
    win.webContents.on("will-navigate", guard);
    win.webContents.setWindowOpenHandler(({ url }) => { if (isTokenRedirect(url)) onToken(url); return { action: "deny" }; });
    win.on("closed", () => finish(() => reject(new Error("Connexion annulée"))));
    void win.loadURL(buildLaunchUrl(base, passport)).catch((e) => finish(() => reject(e)));
  });
}
