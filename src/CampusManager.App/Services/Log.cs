using CampusManager.Core.Queries;

namespace CampusManager.App.Services;

/// <summary>Formatage commun (dates, tailles, échéances), repris de l'interface Electron (util.ts).</summary>
public static class Fmt
{
    static readonly System.Globalization.CultureInfo Fr = new("fr-FR");

    public static string Size(long b) =>
        b < 1024 ? $"{b} o" : b < 1048576 ? $"{Math.Round(b / 1024.0):0} Ko" : $"{b / 1048576.0:0.0} Mo".Replace('.', ',');

    static DateTime Local(long unix) => DateTimeOffset.FromUnixTimeSeconds(unix).LocalDateTime;
    public static string Date(long? t, bool withTime = false) =>
        t == null ? "—" : Local(t.Value).ToString(withTime ? "d MMM, HH:mm" : "d MMM yyyy", Fr);

    public static long Now() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    /// <summary>Temps restant calculé depuis la vraie date limite.</summary>
    public static string TimeLeft(long? due)
    {
        if (due == null) return "Pas de date limite";
        var s = due.Value - Now();
        var abs = Math.Abs(s);
        var txt = abs < 3600 ? $"{Math.Max(1, Math.Round(abs / 60.0))} min" : abs < 86400 ? $"{Math.Round(abs / 3600.0)} h" : $"{Math.Round(abs / 86400.0)} j";
        return s < 0 ? $"En retard de {txt}" : $"Dans {txt}";
    }

    /// <summary>late | 24h | 48h | 7d | later | none</summary>
    public static string Urgency(AssignmentItem a)
    {
        if (a.Status == "done" || a.DueAt == null) return "none";
        if (a.Late) return "late";
        var h = (a.DueAt.Value - Now()) / 3600.0;
        return h <= 24 ? "24h" : h <= 48 ? "48h" : h <= 168 ? "7d" : "later";
    }
    public static string UrgencyLabel(string u) => u switch { "late" => "En retard", "24h" => "< 24 h", "48h" => "< 48 h", "7d" => "< 7 jours", _ => "" };
    public static string StatusLabel(string s) => s switch { "doing" => "En cours", "done" => "Terminé", _ => "À faire" };

    static readonly (string Kind, string[] Exts, string Color)[] Kinds =
    [
        ("pdf", ["pdf"], "#ef5b5b"),
        ("doc", ["doc", "docx", "odt", "rtf"], "#4c8dff"),
        ("ppt", ["ppt", "pptx", "odp"], "#f08a3c"),
        ("xls", ["xls", "xlsx", "ods", "csv"], "#3fb37f"),
        ("zip", ["zip", "rar", "7z", "gz", "tar"], "#d9a93f"),
        ("img", ["png", "jpg", "jpeg", "gif", "svg", "webp", "bmp"], "#2bb3c0"),
        ("code", ["c", "h", "cpp", "py", "js", "ts", "html", "css", "sql", "java", "json", "xml"], "#a274f0"),
        ("txt", ["txt", "md"], "#9aa3b2"),
    ];
    public static string KindColor(string ext) => Kinds.FirstOrDefault(k => k.Exts.Contains(ext)).Color ?? "#7d8597";

    /// <summary>Couleur propre à chaque catégorie, pour distinguer d'un coup d'œil cours, TD, TP…</summary>
    static readonly Dictionary<string, string> CategoryColors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Cours"] = "#4c8dff",                     // bleu
        ["TD"] = "#a274f0",                        // violet
        ["TP"] = "#3fb37f",                        // vert
        ["Exercices"] = "#2bb3c0",                 // cyan
        ["Devoirs"] = "#f08a3c",                   // orange
        ["Projets"] = "#e45f9c",                   // rose
        ["Corrections"] = "#d9a93f",               // jaune
        ["Documents complémentaires"] = "#8a93a6", // gris bleuté
        ["À classer"] = "#f2545b",                 // rouge : à traiter
    };
    const string NeutralColor = "#7d8597";
    public static string CategoryColor(string category) => CategoryColors.GetValueOrDefault(category, NeutralColor);

    static readonly Dictionary<string, (System.Windows.Media.Brush Fg, System.Windows.Media.Brush Bg)> CategoryBrushCache = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Texte coloré + fond très léger de la même couleur (étiquettes).</summary>
    public static (System.Windows.Media.Brush Fg, System.Windows.Media.Brush Bg) CategoryBrushes(string category)
    {
        if (CategoryBrushCache.TryGetValue(category, out var b)) return b;
        var c = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(CategoryColor(category));
        var fg = new System.Windows.Media.SolidColorBrush(c); fg.Freeze();
        var bg = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(40, c.R, c.G, c.B)); bg.Freeze();
        return CategoryBrushCache[category] = (fg, bg);
    }

    public static readonly string[] Categories = ["Cours", "TD", "TP", "Exercices", "Devoirs", "Projets", "Corrections", "Documents complémentaires", "À classer"];
}
