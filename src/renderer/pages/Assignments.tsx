import { useEffect, useMemo, useState } from "react";
import type { AssignmentItem, AssignmentStatus } from "../../shared/api.js";
import { formatDate, statusLabel, timeLeft, urgency, urgencyLabel } from "../util.js";

/** Devoirs EPREL avec statut PERSONNEL (stocké localement, jamais envoyé à EPREL). */
export function Assignments({ version }: { version: number }) {
  const [list, setList] = useState<AssignmentItem[]>([]);
  const [course, setCourse] = useState("");
  const [open, setOpen] = useState<number | null>(null);
  const load = () => window.campus.assignments().then(setList);
  useEffect(() => { load(); }, [version]);

  const courses = useMemo(() => [...new Set(list.map((a) => a.course))].sort(), [list]);
  const shown = list.filter((a) => !course || a.course === course);
  const todo = shown.filter((a) => !a.old && a.status !== "done");
  const done = shown.filter((a) => !a.old && a.status === "done");
  const old = shown.filter((a) => a.old);
  const [showOld, setShowOld] = useState(false);
  const set = async (id: number, status: AssignmentStatus) => { await window.campus.setAssignment(id, { status }); load(); };

  const row = (a: AssignmentItem) => {
    const u = urgency(a);
    return (
      <div key={a.id} className={`assign ${a.status === "done" ? "done" : ""}`}>
        <label className="check" title="Marquer comme terminé">
          <input type="checkbox" checked={a.status === "done"} onChange={(e) => set(a.id, e.target.checked ? "done" : "todo")} />
        </label>
        <button className="grow assign-main" onClick={() => setOpen(open === a.id ? null : a.id)}>
          <div className="ellipsis strong">{a.name}</div>
          <div className="muted small ellipsis">{a.course}</div>
        </button>
        {a.submitted && <span className="tag ok" title="Information récupérée depuis EPREL">Rendu sur EPREL</span>}
        {urgencyLabel[u] && <span className={`pill u-${u}`}>{urgencyLabel[u]}</span>}
        <div className="right-col">
          <div className={`small u-text-${u}`}>{a.status === "done" ? "Terminé" : timeLeft(a.dueAt)}</div>
          <div className="muted small">{a.dueAt ? formatDate(a.dueAt, true) : "—"}</div>
        </div>
        <select value={a.late && a.status !== "done" ? a.status : a.status} onChange={(e) => set(a.id, e.target.value as AssignmentStatus)}>
          {(["todo", "doing", "done"] as const).map((s) => <option key={s} value={s}>{statusLabel[s]}</option>)}
        </select>
        {open === a.id && (
          <div className="assign-desc">{a.description || <span className="muted">Pas de consigne fournie par EPREL.</span>}</div>
        )}
      </div>
    );
  };

  return (
    <div className="page">
      <div className="page-tools">
        <select value={course} onChange={(e) => setCourse(e.target.value)}>
          <option value="">Toutes les matières</option>
          {courses.map((c) => <option key={c}>{c}</option>)}
        </select>
        <span className="muted small">Trié par date limite · « En retard » est calculé depuis la vraie date EPREL</span>
      </div>
      <h3 className="section-title">À faire <span className="muted">{todo.length}</span></h3>
      {todo.length ? todo.map(row) : <p className="muted">Rien à faire.</p>}
      <h3 className="section-title">Terminés <span className="muted">{done.length}</span></h3>
      {done.map(row)}
      {old.length > 0 && <>
        <h3 className="section-title clickable" onClick={() => setShowOld(!showOld)}>
          Anciens devoirs <span className="muted">{old.length} · échéance d'une année universitaire précédente · {showOld ? "masquer" : "afficher"}</span>
        </h3>
        {showOld && old.map(row)}
      </>}
    </div>
  );
}
