using System.IO.Compression;
using System.Text;
using System.Xml;
using CampusManager.Core.Library;
using UglyToad.PdfPig;

namespace CampusManager.Core.Search;

/// <summary>
/// Recherche dans le CONTENU des fichiers (PDF, Word, PowerPoint, texte et code).
/// Le texte est extrait une fois, puis indexé par SQLite FTS5 (trigrammes, sans casse ni accents) :
/// une recherche trouve un mot n'importe où dans le texte, même au milieu d'un mot.
/// Seuls les fichiers nouveaux ou dont l'empreinte a changé sont relus.
/// </summary>
public static class ContentIndex
{
    static readonly HashSet<string> TextExts =
        ["txt", "md", "c", "h", "cpp", "hpp", "cs", "java", "py", "js", "ts", "html", "htm", "css", "sql", "json", "xml", "csv", "sh", "php"];
    const int MaxChars = 400_000;   // au-delà, le reste d'un très gros document n'est pas indexé
    const int MaxTextBytes = 1_000_000;

    public static bool Supported(string ext) => ext is "pdf" or "docx" or "pptx" || TextExts.Contains(ext);

    /// <summary>Texte d'un fichier, ou null s'il est illisible.</summary>
    public static string? ExtractText(string path)
    {
        var ext = Names.Extension(path).TrimStart('.').ToLowerInvariant();
        try
        {
            var text = ext switch
            {
                "pdf" => Pdf(path),
                "docx" => OfficeXml(path, e => e == "word/document.xml", "p"),
                "pptx" => OfficeXml(path, e => e.StartsWith("ppt/slides/slide", StringComparison.Ordinal) && e.EndsWith(".xml", StringComparison.Ordinal), "p"),
                _ when TextExts.Contains(ext) => PlainText(path),
                _ => null,
            };
            return text is { Length: > MaxChars } ? text[..MaxChars] : text;
        }
        catch { return null; }
    }

    static string Pdf(string path)
    {
        using var doc = PdfDocument.Open(path);
        var sb = new StringBuilder();
        foreach (var page in doc.GetPages())
        {
            sb.AppendJoin(' ', page.GetWords().Select(w => w.Text)).Append('\n');
            if (sb.Length > MaxChars) break;
        }
        return sb.ToString();
    }

    /// <summary>Word / PowerPoint : texte des éléments &lt;t&gt;, un saut de ligne par paragraphe.</summary>
    static string OfficeXml(string path, Func<string, bool> entry, string paragraph)
    {
        using var zip = ZipFile.OpenRead(path);
        var sb = new StringBuilder();
        foreach (var e in zip.Entries.Where(x => entry(x.FullName)).OrderBy(x => x.FullName.Length).ThenBy(x => x.FullName, StringComparer.Ordinal))
        {
            using var r = XmlReader.Create(e.Open(), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
            while (r.Read())
            {
                if (r.NodeType == XmlNodeType.Element && r.LocalName == "t") sb.Append(r.ReadElementContentAsString());
                if (r.NodeType == XmlNodeType.EndElement && r.LocalName == paragraph) sb.Append('\n');
            }
            if (sb.Length > MaxChars) break;
        }
        return sb.ToString();
    }

    static string PlainText(string path)
    {
        using var s = File.OpenRead(path);
        var buf = new byte[Math.Min(MaxTextBytes, s.Length)];
        s.ReadExactly(buf);
        return Encoding.UTF8.GetString(buf);
    }

    sealed class Pending { public long Id { get; init; } public string LocalPath { get; init; } = ""; public string Sha256 { get; init; } = ""; }

    /// <summary>Indexe les fichiers nouveaux ou modifiés et retire ceux qui ont disparu. Retourne le nombre de fichiers indexés.</summary>
    public static int IndexPending(Db.Db db, Library.Library lib, CancellationToken ct = default)
    {
        // Fichiers mis à la corbeille ou introuvables : retirés de l'index.
        db.Execute("""
            DELETE FROM content_fts WHERE rowid IN (SELECT file_id FROM content_index WHERE file_id NOT IN (SELECT id FROM files WHERE user_deleted=0 AND missing=0));
            DELETE FROM content_index WHERE file_id NOT IN (SELECT id FROM files WHERE user_deleted=0 AND missing=0);
            """);
        var pending = db.All<Pending>("""
            SELECT f.id, f.local_path, f.sha256 FROM files f LEFT JOIN content_index c ON c.file_id = f.id
            WHERE f.user_deleted = 0 AND f.missing = 0 AND (c.sha256 IS NULL OR c.sha256 <> f.sha256)
            """).Where(p => Supported(Names.Extension(p.LocalPath).TrimStart('.').ToLowerInvariant())).ToList();
        var done = 0;
        foreach (var p in pending)
        {
            if (ct.IsCancellationRequested) break;
            var text = ExtractText(lib.Abs(p.LocalPath)); // lecture hors du verrou de la base
            db.Tx(() =>
            {
                db.Execute("DELETE FROM content_fts WHERE rowid=@Id", new { p.Id });
                if (!string.IsNullOrWhiteSpace(text)) db.Execute("INSERT INTO content_fts(rowid, text) VALUES(@Id, @text)", new { p.Id, text });
                // Enregistré même si illisible : le fichier n'est relu que s'il change.
                db.Execute("INSERT OR REPLACE INTO content_index(file_id, sha256, chars, indexed_at) VALUES(@Id, @Sha256, @n, @t)",
                    new { p.Id, p.Sha256, n = text?.Length ?? 0, t = DateTimeOffset.UtcNow.ToUnixTimeSeconds() });
            });
            if (!string.IsNullOrWhiteSpace(text)) done++;
        }
        return done;
    }

    /// <summary>Mot utilisable par l'index (3 caractères au moins : en dessous, seuls les noms sont cherchés).</summary>
    public static bool Indexable(string term) => term.EnumerateRunes().Count() >= 3;

    /// <summary>Expression FTS5 d'un mot, entre guillemets (aucun opérateur interprété).</summary>
    public static string Quote(string term) => "\"" + term.Replace("\"", "\"\"") + "\"";

    static readonly System.Globalization.CompareInfo Cmp = System.Globalization.CultureInfo.GetCultureInfo("fr-FR").CompareInfo;
    const System.Globalization.CompareOptions Loose = System.Globalization.CompareOptions.IgnoreCase | System.Globalization.CompareOptions.IgnoreNonSpace;

    /// <summary>Extrait de texte commençant juste avant le premier mot trouvé (sans casse ni accents), pour chaque fichier (100 au plus).</summary>
    public static Dictionary<long, string> Snippets(Db.Db db, IEnumerable<long> ids, string search, int before = 25, int after = 140)
    {
        var terms = search.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Where(Indexable).ToList();
        var list = ids.Take(100).ToList();
        var result = new Dictionary<long, string>();
        if (terms.Count == 0 || list.Count == 0) return result;
        foreach (var (id, text) in db.All<(long Id, string Text)>("SELECT rowid, text FROM content_fts WHERE rowid IN @list", new { list }))
        {
            var hit = terms.Select(t => (t, i: Cmp.IndexOf(text, t, Loose))).Where(x => x.i >= 0).OrderBy(x => x.i).FirstOrDefault();
            if (hit.t == null) continue;
            // Peu de texte avant le mot : il reste visible même si la ligne est coupée à droite.
            var from = Math.Max(0, hit.i - before);
            var to = Math.Min(text.Length, hit.i + hit.t.Length + after);
            var snip = System.Text.RegularExpressions.Regex.Replace(text[from..to], @"\s+", " ").Trim();
            result[id] = (from > 0 ? "…" : "") + snip + (to < text.Length ? "…" : "");
        }
        return result;
    }
}
