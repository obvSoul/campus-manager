using System.IO.Compression;
using System.Text;
using CampusManager.Core.Db;
using CampusManager.Core.Library;
using CampusManager.Core.Queries;
using CampusManager.Core.Search;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace CampusManager.Tests;

/// <summary>Recherche dans le contenu des fichiers : vrais fichiers PDF, DOCX et texte sur le disque.</summary>
public sealed class ContentIndexTests : IDisposable
{
    readonly TempDir _t = new();
    readonly Db _db = Db.Open(":memory:");
    readonly Library _lib;
    readonly long _course;

    public ContentIndexTests()
    {
        _lib = new Library(_db, _t.Combine("lib"));
        _course = _db.Insert("INSERT INTO courses(remote_id,name,semester,folder) VALUES(1,'R1.01 - Dev','Semestre 1','R1.01 - Dev')");
    }
    public void Dispose() { _db.Dispose(); _t.Dispose(); }

    long Add(string name, byte[] bytes, string category = "TD") =>
        _lib.ImportFileAsync(new(_course, null, category, name, name, 1, bytes.Length, () => Task.FromResult(bytes))).Result.FileId;

    static byte[] Pdf(string text)
    {
        var b = new PdfDocumentBuilder();
        var font = b.AddStandard14Font(Standard14Font.Helvetica);
        b.AddPage(UglyToad.PdfPig.Content.PageSize.A4).AddText(text, 12, new UglyToad.PdfPig.Core.PdfPoint(50, 700), font);
        return b.Build();
    }

    static byte[] Docx(string text)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            using var w = new StreamWriter(zip.CreateEntry("word/document.xml").Open());
            w.Write($"""<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body><w:p><w:r><w:t>{text}</w:t></w:r></w:p></w:body></w:document>""");
        }
        return ms.ToArray();
    }

    List<string> Find(string q) => Queries.ListFiles(_db, new() { Search = q }).Select(f => f.Name).ToList();

    [Fact]
    public void FindsWordsInsidePdfDocxAndText()
    {
        Add("td3.pdf", Pdf("Exercice 2 : boucles imbriquees et tableaux"));
        Add("consigne.docx", Docx("Rendre le compte rendu sur la jointure SQL"));
        Add("notes.txt", Encoding.UTF8.GetBytes("pointeurs et allocation dynamique"));
        Assert.Equal(3, ContentIndex.IndexPending(_db, _lib));
        Assert.Equal(["td3.pdf"], Find("imbriquees"));
        Assert.Equal(["consigne.docx"], Find("jointure"));
        Assert.Equal(["notes.txt"], Find("allocation"));
    }

    [Fact]
    public void IgnoresCaseAndAccents()
    {
        Add("enonce.txt", Encoding.UTF8.GetBytes("Énoncé : Fonctions Récursives"));
        ContentIndex.IndexPending(_db, _lib);
        Assert.Equal(["enonce.txt"], Find("recursives"));
        Assert.Equal(["enonce.txt"], Find("FONCTIONS récursives"));
    }

    [Fact]
    public void EveryWordMustMatch_NameOrContent()
    {
        Add("td3.pdf", Pdf("boucles et tableaux"));
        Add("td4.pdf", Pdf("boucles et chaines"));
        ContentIndex.IndexPending(_db, _lib);
        Assert.Equal(["td3.pdf"], Find("boucles tableaux"));
        Assert.Equal(["td4.pdf"], Find("td4 boucles")); // un mot dans le nom, l'autre dans le texte
    }

    [Fact]
    public void ShortWordsStillSearchNames()
    {
        Add("tp1.txt", Encoding.UTF8.GetBytes("contenu"));
        ContentIndex.IndexPending(_db, _lib);
        Assert.Equal(["tp1.txt"], Find("tp"));
    }

    [Fact]
    public void OnlyChangedFilesAreReindexed_DeletedOnesLeaveTheIndex()
    {
        var a = Add("a.txt", Encoding.UTF8.GetBytes("premier texte"));
        Add("b.txt", Encoding.UTF8.GetBytes("second texte"));
        Assert.Equal(2, ContentIndex.IndexPending(_db, _lib));
        Assert.Equal(0, ContentIndex.IndexPending(_db, _lib));

        var path = _lib.Abs(_lib.GetFile(a).LocalPath);
        File.WriteAllText(path, "texte corrige avec dichotomie");
        _db.Execute("UPDATE files SET sha256=@h WHERE id=@a", new { h = Library.Sha256Hex(File.ReadAllBytes(path)), a });
        Assert.Equal(1, ContentIndex.IndexPending(_db, _lib));
        Assert.Equal(["a.txt"], Find("dichotomie"));

        _lib.MarkDeleted(a);
        ContentIndex.IndexPending(_db, _lib);
        Assert.Equal(0L, _db.Scalar<long>("SELECT COUNT(*) FROM content_fts WHERE rowid=@a", new { a }));
    }

    [Fact]
    public void SnippetShowsTheMatchInContext()
    {
        var id = Add("td.txt", Encoding.UTF8.GetBytes("Dans cet exercice on parcourt la liste chainee avec un pointeur courant."));
        ContentIndex.IndexPending(_db, _lib);
        var s = ContentIndex.Snippets(_db, [id], "chainee");
        Assert.Contains("chainee", s[id]);
        Assert.Contains("liste", s[id]);
    }

    [Fact]
    public void UnreadableFileIsSkippedNotFatal()
    {
        Add("casse.pdf", Encoding.UTF8.GetBytes("ceci n'est pas un pdf"));
        Add("ok.txt", Encoding.UTF8.GetBytes("texte lisible"));
        Assert.Equal(1, ContentIndex.IndexPending(_db, _lib));
        Assert.Equal(["ok.txt"], Find("lisible"));
        Assert.Equal(0, ContentIndex.IndexPending(_db, _lib)); // le fichier illisible n'est pas réessayé à chaque fois
    }
}
