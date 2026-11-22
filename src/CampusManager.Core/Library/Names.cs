using System.Text;
using System.Text.RegularExpressions;

namespace CampusManager.Core.Library;

/// <summary>Noms de fichiers et dossiers valides sous Windows.</summary>
public static partial class Names
{
    [GeneratedRegex(@"^(con|prn|aux|nul|com[1-9]|lpt[1-9])$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Reserved();
    [GeneratedRegex(@"[<>:""/\\|?*\u0000-\u001f]")]
    private static partial Regex Forbidden();
    [GeneratedRegex(@"[. ]+$")]
    private static partial Regex TrailingDotsSpaces();

    public static string Sanitize(string name, int max = 120)
    {
        var n = TrailingDotsSpaces().Replace(Forbidden().Replace(name.Normalize(NormalizationForm.FormC), "_").Trim(), "");
        if (n.Length == 0) n = "sans nom";
        var ext = Extension(n);
        var stem = ext.Length > 0 ? n[..^ext.Length] : n;
        if (Reserved().IsMatch(stem)) n = $"_{stem}{ext}";
        if (n.Length > max)
        {
            var e = ext.Length > 12 ? ext[..12] : ext;
            n = TrailingDotsSpaces().Replace(n[..(max - e.Length)], "") + e;
        }
        return n;
    }

    /// <summary>Chemin libre dans <paramref name="dir"/> : « nom.pdf », puis « nom (2).pdf », etc.</summary>
    public static string UniquePath(string dir, string name)
    {
        var ext = Extension(name);
        var stem = ext.Length > 0 ? name[..^ext.Length] : name;
        var candidate = Path.Combine(dir, name);
        for (var i = 2; File.Exists(candidate) || Directory.Exists(candidate); i++)
            candidate = Path.Combine(dir, $"{stem} ({i}){ext}");
        return candidate;
    }

    /// <summary>Extension comme Node (`extname`) : « .pdf », vide pour « .bashrc » ou « fin. ».</summary>
    public static string Extension(string name)
    {
        var dot = name.LastIndexOf('.');
        return dot <= 0 || dot == name.Length - 1 ? "" : name[dot..];
    }
}
