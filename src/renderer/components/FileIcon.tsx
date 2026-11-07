import { fileKind } from "../util.js";

/** Pastille d'extension colorée par type de fichier. */
export function FileIcon({ ext, size = 34 }: { ext: string; size?: number }) {
  const { color } = fileKind(ext);
  return (
    <div className="ficon" style={{ width: size, height: size * 1.22, ["--c" as string]: color }}>
      <span style={{ fontSize: Math.max(8, size * 0.26) }}>{(ext || "?").slice(0, 4).toUpperCase()}</span>
    </div>
  );
}
