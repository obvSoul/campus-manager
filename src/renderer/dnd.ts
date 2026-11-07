/** Glisser-déposer : une zone qui accepte des fichiers (depuis Windows ou depuis l'appli elle-même). */
import { useRef, useState, type DragEvent } from "react";

export type DropTarget = { courseId: number; category?: string };
export type DropResult = Awaited<ReturnType<Window["campus"]["drop"]>>;

const hasFiles = (e: DragEvent) => Array.from(e.dataTransfer.types).includes("Files");

export function describeDrop(r: DropResult): string {
  const parts = [];
  if (r.imported) parts.push(`${r.imported} fichier(s) ajouté(s)`);
  if (r.moved) parts.push(`${r.moved} déplacé(s)`);
  if (r.duplicates) parts.push(`${r.duplicates} déjà présent(s)`);
  if (r.skipped.length) parts.push(`ignorés : ${r.skipped.join(", ")}`);
  return parts.join(" · ") || "Rien à faire : déjà au bon endroit";
}

export function useDropTarget(target: DropTarget | null, onDone: (r: DropResult | Error) => void) {
  const [over, setOver] = useState(false);
  const depth = useRef(0); // dragenter/dragleave se déclenchent aussi sur les éléments enfants
  if (!target) return { over: false, props: {} };
  return {
    over,
    props: {
      onDragEnter: (e: DragEvent) => { if (!hasFiles(e)) return; e.preventDefault(); depth.current++; setOver(true); },
      onDragOver: (e: DragEvent) => { if (!hasFiles(e)) return; e.preventDefault(); e.dataTransfer.dropEffect = "copy"; },
      onDragLeave: () => { depth.current = Math.max(0, depth.current - 1); if (!depth.current) setOver(false); },
      onDrop: async (e: DragEvent) => {
        if (!hasFiles(e)) return;
        e.preventDefault(); e.stopPropagation();
        depth.current = 0; setOver(false);
        const paths = Array.from(e.dataTransfer.files).map((f) => window.campus.pathForFile(f)).filter(Boolean);
        if (!paths.length) return;
        try { onDone(await window.campus.drop(paths, target)); }
        catch (err) { onDone(err as Error); }
      },
    },
  };
}
