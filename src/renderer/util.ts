import type { AssignmentItem } from "../shared/api.js";

export const formatSize = (b: number) =>
  b < 1024 ? `${b} o` : b < 1048576 ? `${(b / 1024).toFixed(0)} Ko` : `${(b / 1048576).toFixed(1).replace(".", ",")} Mo`;

export const formatDate = (t: number | null, withTime = false) =>
  t == null ? "—" : new Date(t * 1000).toLocaleDateString("fr-FR", withTime
    ? { day: "numeric", month: "short", hour: "2-digit", minute: "2-digit" } : { day: "numeric", month: "short", year: "numeric" });

/** Temps restant calculé depuis la vraie date limite. */
export function timeLeft(due: number | null, now = Date.now() / 1000): string {
  if (due == null) return "Pas de date limite";
  const s = due - now;
  const abs = Math.abs(s);
  const txt = abs < 3600 ? `${Math.max(1, Math.round(abs / 60))} min` : abs < 86400 ? `${Math.round(abs / 3600)} h` : `${Math.round(abs / 86400)} j`;
  return s < 0 ? `En retard de ${txt}` : `Dans ${txt}`;
}

export type Urgency = "late" | "24h" | "48h" | "7d" | "later" | "none";
export function urgency(a: Pick<AssignmentItem, "dueAt" | "status" | "late">, now = Date.now() / 1000): Urgency {
  if (a.status === "done" || a.dueAt == null) return "none";
  if (a.late) return "late";
  const h = (a.dueAt - now) / 3600;
  return h <= 24 ? "24h" : h <= 48 ? "48h" : h <= 168 ? "7d" : "later";
}
export const urgencyLabel: Record<Urgency, string> = { late: "En retard", "24h": "< 24 h", "48h": "< 48 h", "7d": "< 7 jours", later: "", none: "" };
export const statusLabel = { todo: "À faire", doing: "En cours", done: "Terminé" } as const;

const KINDS: [string, string[], string][] = [
  ["pdf", ["pdf"], "#ef5b5b"],
  ["doc", ["doc", "docx", "odt", "rtf"], "#4c8dff"],
  ["ppt", ["ppt", "pptx", "odp"], "#f08a3c"],
  ["xls", ["xls", "xlsx", "ods", "csv"], "#3fb37f"],
  ["zip", ["zip", "rar", "7z", "gz", "tar"], "#d9a93f"],
  ["img", ["png", "jpg", "jpeg", "gif", "svg", "webp", "bmp"], "#2bb3c0"],
  ["code", ["c", "h", "cpp", "py", "js", "ts", "html", "css", "sql", "java", "json", "xml"], "#a274f0"],
  ["txt", ["txt", "md"], "#9aa3b2"],
];
export function fileKind(ext: string): { kind: string; color: string } {
  for (const [kind, exts, color] of KINDS) if (exts.includes(ext)) return { kind, color };
  return { kind: "file", color: "#7d8597" };
}
export const previewable = (ext: string) => ["pdf", "png", "jpg", "jpeg", "gif", "svg", "webp", "bmp", "txt", "md", "c", "h", "py", "js", "ts", "html", "css", "sql", "json", "csv"].includes(ext);

export const CATEGORIES = ["Cours", "TD", "TP", "Exercices", "Devoirs", "Projets", "Corrections", "Documents complémentaires", "À classer"];
