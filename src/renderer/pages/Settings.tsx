import { useEffect, useState } from "react";
import type { SyncState, WidgetSettings } from "../../shared/api.js";
import { formatDate } from "../util.js";

function WidgetCard() {
  const [w, setW] = useState<WidgetSettings | null>(null);
  useEffect(() => { window.campus.widgetSettings().then(setW); return window.campus.onWidgetSettings(setW); }, []);
  if (!w) return null;
  const set = (p: Partial<WidgetSettings>) => window.campus.setWidgetSettings(p).then(setW);
  const sec = (k: keyof WidgetSettings["sections"], label: string) => (
    <label className="chk"><input type="checkbox" checked={w.sections[k]} onChange={(e) => set({ sections: { ...w.sections, [k]: e.target.checked } })} />{label}</label>
  );
  return (
    <section className="card">
      <h2>Widget de bureau</h2>
      <p className="muted small">Il reste sur le bureau, sous tes fenêtres, même quand cette fenêtre est fermée. Il n'utilise que les données locales.</p>
      <label className="chk"><input type="checkbox" checked={w.enabled} onChange={(e) => set({ enabled: e.target.checked })} />Afficher le widget</label>
      <div className="grid2">
        <div>
          <div className="lbl">Informations affichées</div>
          {sec("assignments", "Devoirs à venir")}{sec("work", "TD / TP récents")}{sec("recent", "Dernières publications")}{sec("progress", "Progression")}
        </div>
        <div>
          <div className="lbl">Comportement</div>
          <label className="chk"><input type="checkbox" checked={w.autoHideFullscreen} onChange={(e) => set({ autoHideFullscreen: e.target.checked })} />Masquer quand une appli est en plein écran</label>
          <label className="chk"><input type="checkbox" checked={w.clickThrough} onChange={(e) => set({ clickThrough: e.target.checked })} />Clic à travers (Ctrl+Alt+X pour basculer)</label>
          <label className="chk"><input type="checkbox" checked={w.blur} onChange={(e) => set({ blur: e.target.checked })} />Flou Windows (acrylique)</label>
        </div>
      </div>
      <div className="lbl">Opacité du fond : {w.opacity < 0.15 ? "entièrement transparent" : `${Math.round(w.opacity * 100)} %`}</div>
      <input type="range" min={0} max={100} value={Math.round(w.opacity * 100)} onChange={(e) => set({ opacity: Number(e.target.value) / 100 })} />
      <div className="row-gap" style={{ marginTop: 10 }}>
        <button className={`btn ${w.theme === "dark" ? "primary" : "ghost"}`} onClick={() => set({ theme: "dark" })}>Sombre</button>
        <button className={`btn ${w.theme === "light" ? "primary" : "ghost"}`} onClick={() => set({ theme: "light" })}>Clair</button>
        <button className="btn ghost" onClick={() => window.campus.widget("show")}>Afficher maintenant</button>
      </div>
      <p className="muted small">Déplacer : glisser l'en-tête. Redimensionner : poignée en bas à droite. Ctrl+Alt+W affiche/masque, Ctrl+Alt+C rouvre cette fenêtre.</p>
    </section>
  );
}

function AutostartCard() {
  const [on, setOn] = useState<boolean | null>(null);
  useEffect(() => { window.campus.autostart().then(setOn); }, []);
  if (on == null) return null;
  return (
    <section className="card">
      <h2>Démarrage de Windows</h2>
      <label className="chk">
        <input type="checkbox" checked={on} onChange={(e) => window.campus.setAutostart(e.target.checked).then(setOn)} />
        Lancer Campus Manager à l'ouverture de session (widget seul, sans fenêtre)
      </label>
    </section>
  );
}

export function Settings({ sync, onSync, theme, setTheme }: {
  sync: SyncState; onSync: (relogin?: boolean) => void; theme: string; setTheme: (t: string) => void;
}) {
  const [s, setS] = useState<{ root: string; dataDir: string } | null>(null);
  useEffect(() => { window.campus.settings().then(setS); }, []);
  return (
    <div className="page settings">
      <section className="card">
        <h2>Bibliothèque</h2>
        <p className="muted small">Tes fichiers sont de vrais fichiers Windows dans ce dossier.</p>
        <div className="kv"><code>{s?.root}</code><button className="btn ghost" onClick={() => window.campus.openRoot()}>Ouvrir le dossier</button></div>
        <p className="muted small">Base de données et journaux : <code>{s?.dataDir}</code></p>
        <p className="muted small">Changer de dossier racine : prévu à une étape suivante (déplacement complet de la bibliothèque).</p>
      </section>
      <section className="card">
        <h2>Connexion EPREL</h2>
        <p className="small">Le jeton de connexion est chiffré par Windows. Ton mot de passe n'est jamais enregistré.</p>
        <p className="muted small">Dernière synchronisation : {formatDate(sync.lastAt, true)}{sync.message ? ` · ${sync.message}` : ""}</p>
        <div className="row-gap">
          <button className="btn primary" disabled={sync.running} onClick={() => onSync(false)}>{sync.running ? "Synchronisation…" : "Synchroniser maintenant"}</button>
          <button className="btn ghost" disabled={sync.running} onClick={() => onSync(true)}>Se reconnecter</button>
        </div>
      </section>
      <WidgetCard />
      <section className="card">
        <h2>Apparence</h2>
        <div className="row-gap">
          <button className={`btn ${theme === "dark" ? "primary" : "ghost"}`} onClick={() => setTheme("dark")}>Onyx (sombre)</button>
          <button className={`btn ${theme === "light" ? "primary" : "ghost"}`} onClick={() => setTheme("light")}>Clair</button>
        </div>
      </section>
      <AutostartCard />
      <section className="card">
        <h2>Application</h2>
        <p className="muted small">Fermer cette fenêtre (croix, Alt+F4) ne quitte pas Campus Manager : le widget reste sur le bureau et la synchronisation continue. Ctrl+Alt+C, le raccourci du Bureau ou le menu ⋯ du widget la rouvrent.</p>
        <button className="btn ghost danger-text" onClick={() => window.campus.widget("quit")}>Quitter complètement (ferme aussi le widget)</button>
      </section>
    </div>
  );
}
