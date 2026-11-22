using System.Text.RegularExpressions;

namespace CampusManager.Core.Classify;

public record ClassifyInput(string? Modname = null, string? Title = null, string? Filename = null, string? Section = null, string? Description = null);
public record Classification(string Category, double Confidence, List<string> Reasons);

/// <summary>
/// Classement automatique : plusieurs signaux pondérés (type d'activité Moodle, titre, nom du fichier,
/// section, description). Un score de confiance envoie les cas douteux dans « À classer ».
/// </summary>
public static class Classifier
{
    public static readonly string[] Categories = ["Cours", "TD", "TP", "Exercices", "Devoirs", "Projets", "Corrections", "Documents complémentaires"];
    public const string Unsorted = "À classer";

    const RegexOptions O = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
    static readonly (string Cat, Regex Re)[] Rules =
    [
        ("Corrections", new(@"\b(correction|corrig[ée]s?|corr)\b", O)),
        ("TD", new(@"\b(td\s*#?\d*|travaux\s+dirig[ée]s?)\b", O)),
        ("TP", new(@"\b(tp\s*#?\d*|travaux\s+pratiques?)\b", O)),
        ("Exercices", new(@"\b(exercices?|exos?|entra[iî]nement)\b", O)),
        ("Projets", new(@"\b(projet|sae|mini-?projet)\b", O)),
        ("Devoirs", new(@"\b(devoir|rendu|[ée]valuation|d[ée]p[oô]t|dm)\b", O)),
        ("Cours", new(@"\b(cours|cm|support|diapo(?:rama)?s?|slides?|chapitre|polycopi[ée]|pr[ée]sentation|le[cç]on)\b", O)),
    ];

    public static Classification Classify(ClassifyInput input)
    {
        var score = new Dictionary<string, double>();
        var order = new List<string>(); // ordre d'apparition, comme une Map JavaScript
        var reasons = new List<string>();
        void Add(string c, double w, string why)
        {
            if (!score.ContainsKey(c)) { score[c] = 0; order.Add(c); }
            score[c] += w; reasons.Add(why);
        }

        // Une activité « devoir » Moodle est un signal fiable.
        if (input.Modname == "assign") Add("Devoirs", 4, "activité de type devoir");

        foreach (var (field, text0, w) in new[] { ("title", input.Title, 3.0), ("filename", input.Filename, 3), ("section", input.Section, 2), ("description", input.Description, 1) })
        {
            // « td1_corrige.pdf » : le tiret bas relie les mots pour les expressions régulières, on le remplace.
            var text = text0?.Replace('_', ' ').Replace('.', ' ');
            if (string.IsNullOrEmpty(text)) continue;
            foreach (var (cat, re) in Rules) if (re.IsMatch(text)) Add(cat, w, $"{field} : {cat}");
        }

        // « Correction du TD3 » est une correction, pas un TD.
        if (score.ContainsKey("Corrections")) score["Corrections"] += 4;

        // Tri stable décroissant (comme Array.sort en JavaScript).
        var ranked = order.Select(c => (Cat: c, Score: score[c])).OrderByDescending(x => x.Score).ToList();
        if (ranked.Count == 0) return new(Unsorted, 0, ["aucun indice"]);
        var best = ranked[0];
        var total = ranked.Sum(x => x.Score);
        var confidence = Math.Min(1, best.Score / total * Math.Min(1, best.Score / 3));
        if (ranked.Count > 1 && best.Score - ranked[1].Score < 1)
            return new(Unsorted, confidence / 2, [.. reasons, "indices contradictoires"]);
        return new(confidence >= 0.4 ? best.Cat : Unsorted, confidence, reasons);
    }
}
