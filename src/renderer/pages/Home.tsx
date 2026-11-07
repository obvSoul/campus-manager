import { useEffect, useState } from "react";
import type { Dashboard, SyncState, View } from "../../shared/api.js";
import { FileIcon } from "../components/FileIcon.js";
import { formatDate, statusLabel, timeLeft, urgency, urgencyLabel } from "../util.js";

export function Home({ go, sync, version, onSync }: { go: (v: View) => void; sync: SyncState; version: number; onSync: () => void }) {
  const [d, setD] = useState<Dashboard | null>(null);
  useEffect(() => { window.campus.dashboard().then(setD); }, [version]);
  if (!d) return null;
  const c = d.counts;
  const total = c.assignmentsDone + c.assignmentsOpen;

  return (
    <div className="dash">
      <section className="card span2 clickable" onClick={() => go({ kind: "assignments" })}>
        <h2>Prochains devoirs</h2>
        {d.upcoming.length === 0 && <p className="muted">Aucun devoir en attente.</p>}
        {d.upcoming.map((a) => {
          const u = urgency(a);
          return (
            <div key={a.id} className="row-item">
              <span className={`dot u-${u}`} />
              <div className="grow">
                <div className="ellipsis strong">{a.name}</div>
                <div className="muted small ellipsis">{a.course}</div>
              </div>
              <div className="right-col">
                <div className={`small u-text-${u}`}>{timeLeft(a.dueAt)}</div>
                <div className="muted small">{a.dueAt ? formatDate(a.dueAt, true) : ""} · {statusLabel[a.status]}</div>
              </div>
              {urgencyLabel[u] && <span className={`pill u-${u}`}>{urgencyLabel[u]}</span>}
            </div>
          );
        })}
      </section>

      <section className="card">
        <h2>Travail</h2>
        <div className="stats">
          <button className="stat" onClick={() => go({ kind: "assignments" })}><b>{c.assignmentsOpen}</b><span>devoirs non terminés</span></button>
          <button className={`stat ${c.assignmentsLate ? "bad" : ""}`} onClick={() => go({ kind: "assignments" })}><b>{c.assignmentsLate}</b><span>en retard</span></button>
          <button className="stat" onClick={() => go({ kind: "work" })}><b>{c.td}</b><span>fichiers de TD</span></button>
          <button className="stat" onClick={() => go({ kind: "work" })}><b>{c.tp}</b><span>fichiers de TP</span></button>
        </div>
        <p className="muted small">Le suivi « fait / à faire » des TD et TP arrive à l'étape Gestion universitaire.</p>
      </section>

      <section className="card span2">
        <h2 className="clickable" onClick={() => go({ kind: "recent" })}>Dernières publications</h2>
        <div className="recent-grid">
          {d.recentFiles.map((f) => (
            <button key={f.id} className="recent" onClick={() => go({ kind: "course", courseId: f.courseId!, category: f.category })} onDoubleClick={() => window.campus.open(f.id)}>
              <FileIcon ext={f.ext} size={26} />
              <div className="grow">
                <div className="ellipsis">{f.name}</div>
                <div className="muted small ellipsis">{f.category} · {f.course}</div>
              </div>
            </button>
          ))}
        </div>
      </section>

      <section className="card">
        <h2>Progression des devoirs</h2>
        <div className="big-progress"><b>{c.assignmentsDone}</b> / {total} terminés</div>
        <div className="bar"><i style={{ width: `${total ? (c.assignmentsDone / total) * 100 : 0}%` }} /></div>
        <div className="per-course">
          {d.perCourse.map((p) => (
            <div key={p.course} className="pc">
              <span className="ellipsis small" title={p.course}>{p.course}</span>
              <div className="bar sm"><i style={{ width: `${(p.done / p.total) * 100}%` }} /></div>
              <span className="muted small">{p.done}/{p.total}</span>
            </div>
          ))}
        </div>
      </section>

      <section className="card">
        <h2>Synchronisation EPREL</h2>
        <p>{d.lastSync ? <>Dernière : {formatDate(d.lastSync.at, true)} {d.lastSync.ok ? "· réussie" : "· avec erreurs"}</> : "Jamais synchronisé."}</p>
        <p className="muted small">{c.files} fichiers locaux · {c.courses} matières{c.unsorted ? ` · ${c.unsorted} à classer` : ""}</p>
        {sync.message && <p className="small">{sync.message}</p>}
        <button className="btn primary" disabled={sync.running} onClick={(e) => { e.stopPropagation(); onSync(); }}>
          {sync.running ? "Synchronisation…" : "Synchroniser maintenant"}
        </button>
      </section>

      {c.unsorted > 0 && (
        <section className="card clickable" onClick={() => go({ kind: "search", query: "À classer" })}>
          <h2>À classer</h2>
          <p><b>{c.unsorted}</b> fichiers dont la catégorie est incertaine.</p>
          <p className="muted small">Clic droit sur un fichier &gt; « Changer de catégorie ». Ton choix est retenu.</p>
        </section>
      )}
    </div>
  );
}
