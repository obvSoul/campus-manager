import { useState } from "react";
import type { CourseNode, SemesterNode, View } from "../../shared/api.js";
import { useDropTarget, type DropResult } from "../dnd.js";
import { Icon } from "./Icon.js";

const NAV: { view: View; label: string; icon: string }[] = [
  { view: { kind: "home" }, label: "Accueil", icon: "home" },
  { view: { kind: "all" }, label: "Tous les fichiers", icon: "files" },
  { view: { kind: "assignments" }, label: "Devoirs", icon: "check" },
  { view: { kind: "work" }, label: "TD / TP", icon: "flask" },
  { view: { kind: "recent" }, label: "Récemment ajoutés", icon: "clock" },
  { view: { kind: "favorites" }, label: "Favoris", icon: "star" },
];

const same = (a: View, b: View) => JSON.stringify(a) === JSON.stringify(b);

type Dropped = (r: DropResult | Error) => void;

/** Ligne de matière : cliquable et cible de dépôt (fichiers glissés dessus). */
function CourseRow({ c, active, onClick, onDouble, onDropped }: { c: CourseNode; active: boolean; onClick: () => void; onDouble: () => void; onDropped: Dropped }) {
  const d = useDropTarget({ courseId: c.id }, onDropped);
  return (
    <button {...d.props} className={`tree-course ${active ? "active" : ""} ${d.over ? "drop" : ""}`} title={c.name} onClick={onClick} onDoubleClick={onDouble}>
      <Icon name="folder" size={15} /><span className="ellipsis">{c.folder}</span>
    </button>
  );
}

function CategoryRow({ c, cat, active, onClick, onDropped }: { c: CourseNode; cat: { name: string; count: number }; active: boolean; onClick: () => void; onDropped: Dropped }) {
  const d = useDropTarget({ courseId: c.id, category: cat.name }, onDropped);
  return (
    <button {...d.props} className={`tree-cat ${active ? "active" : ""} ${d.over ? "drop" : ""}`} onClick={onClick}>
      <span className="ellipsis">{cat.name}</span><span className="count">{cat.count}</span>
    </button>
  );
}

export function Sidebar({ view, tree, go, onDropped }: { view: View; tree: SemesterNode[]; go: (v: View) => void; onDropped: Dropped }) {
  const [open, setOpen] = useState<Record<string, boolean>>({});
  const toggle = (k: string) => setOpen((o) => ({ ...o, [k]: !(o[k] ?? k.startsWith("s:")) }));
  const isOpen = (k: string) => open[k] ?? k.startsWith("s:"); // semestres dépliés par défaut

  return (
    <aside className="sidebar">
      <div className="brand"><span className="logo">C</span>Campus Manager</div>
      <nav>
        {NAV.map((n) => (
          <button key={n.label} className={`nav ${same(view, n.view) ? "active" : ""}`} onClick={() => go(n.view)}>
            <Icon name={n.icon} /> {n.label}
          </button>
        ))}
      </nav>
      <div className="nav-title">Mes matières</div>
      <div className="tree">
        {tree.map((s) => (
          <div key={s.name}>
            <button className="tree-sem" onClick={() => toggle(`s:${s.name}`)}>
              <span className={`chev ${isOpen(`s:${s.name}`) ? "down" : ""}`}><Icon name="chevron" size={14} /></span>{s.name}
            </button>
            {isOpen(`s:${s.name}`) && s.courses.map((c) => {
              const active = view.kind === "course" && view.courseId === c.id;
              return (
                <div key={c.id}>
                  <CourseRow c={c} active={active && !(view.kind === "course" && view.category)} onDropped={onDropped}
                    onClick={() => { go({ kind: "course", courseId: c.id }); setOpen((o) => ({ ...o, [`c:${c.id}`]: true })); }}
                    onDouble={() => toggle(`c:${c.id}`)} />
                  {isOpen(`c:${c.id}`) && c.categories.map((cat) => (
                    <CategoryRow key={cat.name} c={c} cat={cat} onDropped={onDropped}
                      active={active && view.kind === "course" && view.category === cat.name}
                      onClick={() => go({ kind: "course", courseId: c.id, category: cat.name })} />
                  ))}
                </div>
              );
            })}
          </div>
        ))}
      </div>
      <button className={`nav bottom ${view.kind === "settings" ? "active" : ""}`} onClick={() => go({ kind: "settings" })}>
        <Icon name="gear" /> Paramètres
      </button>
    </aside>
  );
}
