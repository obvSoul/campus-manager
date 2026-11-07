import { describe, expect, it } from "vitest";
import { classify, UNSORTED } from "../src/core/classify/classifier.js";

describe("classifier (règles génériques ; à affiner avec les vrais noms EPREL)", () => {
  it("activité de type devoir -> Devoirs", () => {
    expect(classify({ modname: "assign", title: "Rendu du TD#3" }).category).toBe("Devoirs");
  });
  it("utilise la section d'origine, pas seulement le nom du fichier", () => {
    expect(classify({ modname: "resource", filename: "feuille3.pdf", section: "Travaux dirigés" }).category).toBe("TD");
  });
  it("une correction de TD est une correction", () => {
    expect(classify({ title: "Correction du TD 2", filename: "td2_corrige.pdf" }).category).toBe("Corrections");
  });
  it("TP et cours", () => {
    expect(classify({ title: "TP 4 - Pointeurs" }).category).toBe("TP");
    expect(classify({ title: "Cours magistral chapitre 2", filename: "CM2.pdf" }).category).toBe("Cours");
  });
  it("aucun indice -> À classer, jamais un choix arbitraire", () => {
    const r = classify({ title: "Document", filename: "scan0001.pdf" });
    expect(r.category).toBe(UNSORTED);
    expect(r.confidence).toBe(0);
  });
  it("indices contradictoires -> À classer", () => {
    expect(classify({ title: "TD", filename: "TP.pdf" }).category).toBe(UNSORTED);
  });
  it("le titre prime sur la section quand les indices divergent", () => {
    expect(classify({ title: "TD 3", section: "TP" }).category).toBe("TD");
  });
});
