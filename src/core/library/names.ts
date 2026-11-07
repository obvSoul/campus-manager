/** Noms de fichiers/dossiers valides sous Windows. */
import { existsSync } from "node:fs";
import { extname, join } from "node:path";

const RESERVED = /^(con|prn|aux|nul|com[1-9]|lpt[1-9])$/i;

export function sanitizeName(name: string, max = 120): string {
  let n = name.normalize("NFC").replace(/[<>:"/\\|?*\u0000-\u001f]/g, "_").trim();
  n = n.replace(/[. ]+$/, ""); // Windows refuse les noms finissant par un point ou une espace
  if (!n) n = "sans nom";
  const ext = extname(n);
  const stem = ext ? n.slice(0, -ext.length) : n;
  if (RESERVED.test(stem)) n = `_${stem}${ext}`;
  if (n.length > max) {
    const e = ext.slice(0, 12);
    n = n.slice(0, max - e.length).replace(/[. ]+$/, "") + e;
  }
  return n;
}

/** Retourne un chemin libre dans `dir` : « nom.pdf », puis « nom (2).pdf », etc. */
export function uniquePath(dir: string, name: string): string {
  const ext = extname(name);
  const stem = ext ? name.slice(0, -ext.length) : name;
  let candidate = join(dir, name);
  for (let i = 2; existsSync(candidate); i++) candidate = join(dir, `${stem} (${i})${ext}`);
  return candidate;
}
