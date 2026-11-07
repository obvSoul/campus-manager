/** Widget de bureau : lit uniquement les données locales (fonctionne hors ligne). */
import { useCallback, useEffect, useRef, useState } from "react";
import { createRoot } from "react-dom/client";
import type { SyncState, WidgetData, WidgetSettings } from "../shared/api.js";
import { FileIcon } from "./components/FileIcon.js";
import { Icon } from "./components/Icon.js";
import { formatDate, timeLeft, urgency, urgencyLabel } from "./util.js";

function WidgetApp() {
  const [d, setD] = useState<WidgetData | null>(null);
  const [s, setS] = useState<WidgetSettings | null>(null);
  const [sync, setSync] = useState<SyncState | null>(null);
  const [menu, setMenu] = useState(false);
  const [, tick] = useState(0);
  const load = useCallback(() => { window.campus.widgetData().then(setD); }, []);

  useEffect(() => {
    load();
    window.campus.widgetSettings().then(setS);
    window.campus.syncState().then(setSync);
    const offs = [window.campus.onChanged(load), window.campus.onWidgetSettings(setS), window.campus.onSyncState(setSync)];
    const t = setInterval(() => { tick((n) => n + 1); }, 60_000); // temps restant à jour chaque minute
    const r = setInterval(load, 10 * 60_000);
    return () => { offs.forEach((o) => o()); clearInterval(t); clearInterval(r); };
  }, [load]);

  useEffect(() => { if (s) document.documentElement.dataset.theme = s.theme; }, [s?.theme]);

  // Poignée de redimensionnement (bas droite).
  const drag = useRef<{ x: number; y: number; w: number; h: number } | null>(null);
  useEffect(() => {
    const move = (e: MouseEvent) => {
      const g = drag.current; if (!g) return;
      window.campus.widgetResize(g.w + e.screenX - g.x, g.h + e.screenY - g.y);
    };
    const up = () => { drag.current = null; };
    addEventListener("mousemove", move); addEventListener("mouseup", up);
    return () => { removeEventListener("mousemove", move); removeEventListener("mouseup", up); };
  }, []);

  if (!s) return null;
  const done = async (id: number) => { await window.campus.setAssignment(id, { status: "done" }); load(); };
  const total = d ? d.progress.done + d.progress.open : 0;

  return (
    // Entièrement transparent : fond invisible (1 % pour que Windows continue d'envoyer les clics
    // et le glisser au widget) et texte détouré par une ombre pour rester lisible sur tout fond d'écran.
    <div className={`w ${s.opacity < 0.15 ? "clear" : ""}`} style={{ ["--a" as string]: String(Math.max(0.01, s.opacity)) }} onClick={() => setMenu(false)}>
      <header className="w-head">
        <span className="logo">C</span>
        <div className="grow">
          <div className="w-title">Campus</div>
          {!s.collapsed && <div className="w-sub">{sync?.running ? "Synchronisation…" : d?.lastSync ? `Synchro ${formatDate(d.lastSync, true)}` : "Jamais synchronisé"}</div>}
        </div>
        {s.collapsed && d && <span className="w-badge" title="Devoirs à faire">{d.progress.open}</span>}
        <button className={`ib ${sync?.running ? "spin" : ""}`} title="Synchroniser avec EPREL" onClick={() => window.campus.sync()}><Icon name="sync" size={15} /></button>
        <button className="ib" title={s.collapsed ? "Déplier" : "Réduire"} onClick={() => window.campus.widget(s.collapsed ? "expand" : "collapse")}>
          <span style={{ display: "inline-flex", transform: `rotate(${s.collapsed ? 90 : -90}deg)` }}><Icon name="chevron" size={15} /></span>
        </button>
        <button className="ib" title="Menu" onClick={(e) => { e.stopPropagation(); setMenu(!menu); }}>⋯</button>
      </header>

      {menu && (
        <div className="w-menu" onClick={(e) => e.stopPropagation()}>
          <button onClick={() => { setMenu(false); window.campus.openMain(); }}>Ouvrir Campus Manager <kbd>Ctrl+Alt+C</kbd></button>
          <button onClick={() => { setMenu(false); window.campus.openMain("settings"); }}>Personnaliser le widget…</button>
          <button onClick={() => { setMenu(false); window.campus.setWidgetSettings({ clickThrough: true }); }}>Laisser passer les clics <kbd>Ctrl+Alt+X</kbd></button>
          <button onClick={() => { setMenu(false); window.campus.widget("hide"); }}>Masquer <kbd>Ctrl+Alt+W</kbd></button>
          <hr />
          <button className="danger" onClick={() => window.campus.widget("quit")}>Quitter Campus Manager</button>
        </div>
      )}

      {!s.collapsed && d && (
        <div className="w-body">
          {s.sections.assignments && (
            <section>
              <h4 onClick={() => window.campus.openMain("assignments")}>Devoirs à venir</h4>
              {d.assignments.length === 0 && <p className="w-empty">Aucun devoir en attente</p>}
              {d.assignments.map((a) => {
                const u = urgency(a);
                return (
                  <div key={a.id} className="w-row">
                    <input type="checkbox" title="Marquer comme terminé" onChange={() => done(a.id)} />
                    <button className="grow w-link" onClick={() => window.campus.openMain("assignments")} title={a.description ?? a.name}>
                      <div className="ellipsis">{a.name}</div>
                      <div className="w-meta ellipsis">{a.course}</div>
                    </button>
                    <div className="w-right">
                      <div className={`w-due u-${u}`}>{timeLeft(a.dueAt)}</div>
                      {urgencyLabel[u] && <span className={`w-pill u-bg-${u}`}>{urgencyLabel[u]}</span>}
                    </div>
                  </div>
                );
              })}
            </section>
          )}
          {s.sections.work && d.work.length > 0 && (
            <section>
              <h4 onClick={() => window.campus.openMain("work")}>TD / TP récents</h4>
              {d.work.map((f) => (
                <button key={f.id} className="w-file" onClick={() => window.campus.open(f.id)} title="Ouvrir">
                  <FileIcon ext={f.ext} size={16} /><span className="grow ellipsis">{f.name}</span><span className="w-meta">{f.category}</span>
                </button>
              ))}
            </section>
          )}
          {s.sections.recent && d.recent.length > 0 && (
            <section>
              <h4 onClick={() => window.campus.openMain("recent")}>Dernières publications</h4>
              {d.recent.map((f) => (
                <button key={f.id} className="w-file" onClick={() => window.campus.open(f.id)} title={`${f.course} · ouvrir`}>
                  <FileIcon ext={f.ext} size={16} /><span className="grow ellipsis">{f.name}</span><span className="w-meta ellipsis short">{f.course.split(" - ")[0]}</span>
                </button>
              ))}
            </section>
          )}
          {s.sections.progress && total > 0 && (
            <section>
              <h4>Progression</h4>
              <div className="w-prog"><b>{d.progress.done}</b> terminés · <b>{d.progress.open}</b> restants{d.progress.late ? <> · <b className="u-late">{d.progress.late}</b> en retard</> : null}</div>
              <div className="w-bar"><i style={{ width: `${(d.progress.done / total) * 100}%` }} /></div>
            </section>
          )}
        </div>
      )}
      {!s.collapsed && (
        <div className="w-grip" title="Redimensionner"
          onMouseDown={(e) => { drag.current = { x: e.screenX, y: e.screenY, w: innerWidth, h: innerHeight }; e.preventDefault(); }} />
      )}
    </div>
  );
}

createRoot(document.getElementById("root")!).render(<WidgetApp />);
