/**
 * Classement automatique : plusieurs signaux pondérés, pas seulement le nom du fichier.
 * Signaux : type d'activité Moodle, titre de la ressource, nom du fichier, section/dossier d'origine,
 * description. Un score de confiance permet d'envoyer les cas douteux dans « À classer ».
 * Les règles sont génériques : elles seront affinées avec les vrais noms vus sur EPREL.
 */
export const CATEGORIES = ["Cours", "TD", "TP", "Exercices", "Devoirs", "Projets", "Corrections", "Documents complémentaires"] as const;
export type Category = (typeof CATEGORIES)[number];
export const UNSORTED = "À classer";

export interface ClassifyInput {
  modname?: string;      // type d'activité Moodle : assign, resource, folder, url, page...
  title?: string;
  filename?: string;
  section?: string;      // nom de la section/du dossier d'origine
  description?: string;
}
export interface Classification { category: Category | typeof UNSORTED; confidence: number; reasons: string[] }

const RULES: [Category, RegExp][] = [
  ["Corrections", /\b(correction|corrig[ée]s?|corr)\b/i],
  ["TD", /\b(td\s*#?\d*|travaux\s+dirig[ée]s?)\b/i],
  ["TP", /\b(tp\s*#?\d*|travaux\s+pratiques?)\b/i],
  ["Exercices", /\b(exercices?|exos?|entra[iî]nement)\b/i],
  ["Projets", /\b(projet|sae|mini-?projet)\b/i],
  ["Devoirs", /\b(devoir|rendu|[ée]valuation|d[ée]p[oô]t|dm)\b/i],
  ["Cours", /\b(cours|cm|support|diapo(?:rama)?s?|slides?|chapitre|polycopi[ée]|pr[ée]sentation|le[cç]on)\b/i],
];
const WEIGHTS = { title: 3, filename: 3, section: 2, description: 1 } as const;

export function classify(input: ClassifyInput): Classification {
  const score = new Map<Category, number>();
  const reasons: string[] = [];
  const add = (c: Category, w: number, why: string) => { score.set(c, (score.get(c) ?? 0) + w); reasons.push(why); };

  // Une activité « devoir » Moodle est un signal fiable.
  if (input.modname === "assign") add("Devoirs", 4, "activité de type devoir");

  for (const field of ["title", "filename", "section", "description"] as const) {
    // « td1_corrige.pdf » : le tiret bas relie les mots pour les expressions régulières, on le remplace.
    const text = input[field]?.replace(/[_.]/g, " ");
    if (!text) continue;
    for (const [cat, re] of RULES) if (re.test(text)) add(cat, WEIGHTS[field], `${field} : ${cat}`);
  }

  // « Correction du TD3 » est une correction, pas un TD.
  if (score.has("Corrections")) score.set("Corrections", (score.get("Corrections") ?? 0) + 4);

  const ranked = [...score.entries()].sort((a, b) => b[1] - a[1]);
  if (!ranked.length) return { category: UNSORTED, confidence: 0, reasons: ["aucun indice"] };
  const [best, second] = [ranked[0]!, ranked[1]];
  const total = ranked.reduce((s, [, v]) => s + v, 0);
  const confidence = Math.min(1, (best[1] / total) * Math.min(1, best[1] / 3)) ;
  if (second && best[1] - second[1] < 1) return { category: UNSORTED, confidence: confidence / 2, reasons: [...reasons, "indices contradictoires"] };
  return { category: confidence >= 0.4 ? best[0] : UNSORTED, confidence, reasons };
}
