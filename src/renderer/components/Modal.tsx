import { useEffect, useRef, useState } from "react";

/** Petite boîte de saisie (Electron ne gère pas window.prompt). */
export function PromptModal({ title, initial = "", label, onOk, onCancel, options }: {
  title: string; initial?: string; label: string; options?: { value: string; label: string }[];
  onOk: (v: string) => void; onCancel: () => void;
}) {
  const [v, setV] = useState(initial);
  const ref = useRef<HTMLInputElement & HTMLSelectElement>(null);
  useEffect(() => {
    ref.current?.focus();
    if (ref.current && "setSelectionRange" in ref.current && !options) {
      const dot = initial.lastIndexOf(".");
      ref.current.setSelectionRange(0, dot > 0 ? dot : initial.length);
    }
  }, []);
  return (
    <div className="modal-back" onMouseDown={onCancel}>
      <form className="modal" onMouseDown={(e) => e.stopPropagation()} onSubmit={(e) => { e.preventDefault(); if (v.trim()) onOk(v.trim()); }}>
        <h3>{title}</h3>
        <label>{label}</label>
        {options
          ? <select ref={ref} value={v} onChange={(e) => setV(e.target.value)}>{options.map((o) => <option key={o.value} value={o.value}>{o.label}</option>)}</select>
          : <input ref={ref} value={v} onChange={(e) => setV(e.target.value)} onKeyDown={(e) => e.key === "Escape" && onCancel()} />}
        <div className="modal-actions">
          <button type="button" className="btn ghost" onClick={onCancel}>Annuler</button>
          <button type="submit" className="btn primary">Valider</button>
        </div>
      </form>
    </div>
  );
}
