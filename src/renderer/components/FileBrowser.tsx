import { useEffect, useMemo, useState } from "react";
import type { FileItem, SemesterNode } from "../../shared/api.js";
import { CATEGORIES, formatDate, formatSize } from "../util.js";
import { FileIcon } from "./FileIcon.js";
import { Icon } from "./Icon.js";
import { PromptModal } from "./Modal.js";

export type Mode = "list" | "grid" | "large" | "compact";
type SortKey = "name" | "addedAt" | "ext" | "course" | "size" | "category";

interface Props {
  files: FileItem[]; tree: SemesterNode[]; mode: Mode; selected: number | null; showCourse: boolean;
  onSelect: (id: number | null) => void; onChanged: () => void; toast: (m: string) => void; empty: string;
}

export function FileBrowser({ files, tree, mode, selected, showCourse, onSelect, onChanged, toast, empty }: Props) {
  const [sort, setSort] = useState<{ key: SortKey; asc: boolean }>({ key: "name", asc: true });
  const [menu, setMenu] = useState<{ x: number; y: number; f: FileItem } | null>(null);
  const [modal, setModal] = useState<{ kind: "rename" | "move" | "course"; f: FileItem } | null>(null);

  useEffect(() => { const close = () => setMenu(null); window.addEventListener("click", close); window.addEventListener("blur", close);
    return () => { window.removeEventListener("click", close); window.removeEventListener("blur", close); }; }, []);

  const sorted = useMemo(() => {
    const k = sort.key;
    const v = (f: FileItem) => (k === "name" ? f.name.toLowerCase() : k === "course" ? f.course.toLowerCase() : f[k]);
    return [...files].sort((a, b) => (v(a) < v(b) ? -1 : v(a) > v(b) ? 1 : 0) * (sort.asc ? 1 : -1));
  }, [files, sort]);

  const open = async (f: FileItem) => { const err = await window.campus.open(f.id); if (err) toast(`Impossible d'ouvrir : ${err}`); };
  const act = async (fn: () => Promise<unknown>, ok: string) => {
    try { await fn(); toast(ok); onChanged(); } catch (e) { toast(`Erreur : ${(e as Error).message.replace(/^Error invoking remote method '[^']+': (Error: )?/, "")}`); }
  };

  const header = (key: SortKey, label: string, cls = "") => (
    <button className={`th ${cls}`} onClick={() => setSort((s) => ({ key, asc: s.key === key ? !s.asc : true }))}>
      {label}{sort.key === key ? (sort.asc ? " ↑" : " ↓") : ""}
    </button>
  );

  const courses = tree.flatMap((s) => s.courses.map((c) => ({ value: String(c.id), label: `${s.name} · ${c.folder}` })));

  if (!files.length) return <div className="empty"><Icon name="folder" size={36} /><p>{empty}</p></div>;

  return (
    <>
      {mode === "list" && (
        <div className={`thead ${showCourse ? "with-course" : ""}`}>
          <span />{header("name", "Nom", "c-name")}{header("category", "Type")}{showCourse && header("course", "Matière")}
          {header("addedAt", "Ajouté")}{header("size", "Taille", "right")}
        </div>
      )}
      {mode !== "list" && (
        <div className="sortbar">
          Trier par
          <select value={sort.key} onChange={(e) => setSort({ key: e.target.value as SortKey, asc: true })}>
            <option value="name">Nom</option><option value="addedAt">Date d'ajout</option><option value="ext">Type de fichier</option>
            <option value="course">Matière</option><option value="size">Taille</option>
          </select>
          <button className="btn ghost sm" onClick={() => setSort((s) => ({ ...s, asc: !s.asc }))}>{sort.asc ? "↑" : "↓"}</button>
        </div>
      )}
      <div className={`files mode-${mode} ${showCourse ? "with-course" : ""}`}>
        {sorted.map((f) => (
          <div key={f.id} className={`file ${selected === f.id ? "sel" : ""} ${f.missing ? "missing" : ""}`} tabIndex={0}
            draggable={!f.missing}
            onDragStart={(e) => { e.preventDefault(); window.campus.startDrag(f.id); }}
            onClick={() => onSelect(f.id)} onDoubleClick={() => open(f)}
            onKeyDown={(e) => { if (e.key === "Enter") open(f); if (e.key === "F2") setModal({ kind: "rename", f }); }}
            onContextMenu={(e) => { e.preventDefault(); onSelect(f.id); setMenu({ x: e.clientX, y: e.clientY, f }); }}>
            <FileIcon ext={f.ext} size={mode === "large" ? 64 : mode === "grid" ? 44 : mode === "compact" ? 16 : 20} />
            <div className="fname">
              <span className="ellipsis" title={f.name}>{f.name}</span>
              {f.favorite && <span className="fav" title="Favori"><Icon name="star" size={13} /></span>}
              {f.remoteDeleted && <span className="tag warn" title="Retiré d'EPREL : ta copie locale est conservée">retiré d'EPREL</span>}
              {f.missing && <span className="tag bad">introuvable</span>}
            </div>
            {mode === "list" && <>
              <span className="tag cat">{f.category}</span>
              {showCourse && <span className="muted ellipsis" title={f.course}>{f.course}</span>}
              <span className="muted">{formatDate(f.addedAt)}</span>
              <span className="muted right">{formatSize(f.size)}</span>
            </>}
            {(mode === "grid" || mode === "large") && <span className="muted small">{f.category}{showCourse ? ` · ${f.course}` : ""}</span>}
          </div>
        ))}
      </div>

      {menu && (
        <div className="ctx" style={{ left: Math.min(menu.x, innerWidth - 230), top: Math.min(menu.y, innerHeight - 330) }} onClick={(e) => e.stopPropagation()}>
          {([
            ["Ouvrir", () => open(menu.f)],
            ["Ouvrir l'emplacement", () => window.campus.reveal(menu.f.id)],
            null,
            ["Renommer", () => setModal({ kind: "rename", f: menu.f })],
            ["Changer de catégorie", () => setModal({ kind: "move", f: menu.f })],
            ["Changer de matière", () => setModal({ kind: "course", f: menu.f })],
            ["Copier le chemin", () => act(() => window.campus.copyPath(menu.f.id), "Chemin copié")],
            [menu.f.favorite ? "Retirer des favoris" : "Ajouter aux favoris", () => act(() => window.campus.favorite(menu.f.id, !menu.f.favorite), menu.f.favorite ? "Retiré des favoris" : "Ajouté aux favoris")],
            null,
            ["Propriétés", () => onSelect(menu.f.id)],
            ["Mettre à la corbeille…", () => act(async () => { if (!(await window.campus.trash(menu.f.id))) throw new Error("annulé"); onSelect(null); }, "Fichier mis à la corbeille")],
          ] as ([string, () => unknown] | null)[]).map((it, i) => it
            ? <button key={i} className={it[0].startsWith("Mettre") ? "danger" : ""} onClick={() => { setMenu(null); it[1](); }}>{it[0]}</button>
            : <hr key={i} />)}
        </div>
      )}

      {modal?.kind === "rename" && (
        <PromptModal title="Renommer" label="Nouveau nom (le fichier est renommé sur le disque)" initial={modal.f.name}
          onCancel={() => setModal(null)} onOk={(v) => { const f = modal.f; setModal(null); act(() => window.campus.rename(f.id, v), "Fichier renommé"); }} />
      )}
      {modal?.kind === "move" && (
        <PromptModal title="Changer de catégorie" label="Le fichier sera déplacé dans ce dossier ; ce choix est retenu pour les prochaines synchronisations."
          initial={modal.f.category} options={CATEGORIES.map((c) => ({ value: c, label: c }))}
          onCancel={() => setModal(null)} onOk={(v) => { const f = modal.f; setModal(null); act(() => window.campus.move(f.id, { category: v }), `Déplacé dans ${v}`); }} />
      )}
      {modal?.kind === "course" && (
        <PromptModal title="Changer de matière" label="Matière de destination" initial={String(modal.f.courseId ?? courses[0]?.value)} options={courses}
          onCancel={() => setModal(null)} onOk={(v) => { const f = modal.f; setModal(null); act(() => window.campus.move(f.id, { courseId: Number(v) }), "Fichier déplacé"); }} />
      )}
    </>
  );
}
