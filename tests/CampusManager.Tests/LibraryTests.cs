using System.Runtime.InteropServices;
using System.Text;
using CampusManager.Core.Db;
using CampusManager.Core.Library;

namespace CampusManager.Tests;

/// <summary>Tests sur de VRAIS fichiers dans un dossier temporaire du disque.</summary>
public sealed class LibraryTests : IDisposable
{
    readonly TempDir _t = new();
    readonly Db _db = Db.Open(":memory:");
    readonly Library _lib;
    readonly long _course;

    public LibraryTests()
    {
        _lib = new Library(_db, _t.Combine("Campus Manager"));
        _course = _db.Insert("INSERT INTO courses(remote_id,name,semester,folder) VALUES(1,'Programmation C','Semestre 1','Programmation C')");
    }
    public void Dispose() { _db.Dispose(); _t.Dispose(); }

    static byte[] B(string s) => Encoding.UTF8.GetBytes(s);
    ImportRequest Req(string data = "v1", string remoteKey = "k1", string name = "td1.pdf", long remoteModified = 100, long? remoteSize = null, Func<Task<byte[]>>? fetch = null) =>
        new(_course, null, "TD", name, remoteKey, remoteModified, remoteSize ?? data.Length, fetch ?? (() => Task.FromResult(B(data))));
    string Read(string rel) => File.ReadAllText(_lib.Abs(rel));

    [Fact]
    public async Task WritesFileInSemesterCourseCategory()
    {
        var r = await _lib.ImportFileAsync(Req());
        Assert.Equal(ImportStatus.Added, r.Status);
        Assert.Equal(Path.Combine("Semestre 1", "Programmation C", "TD", "td1.pdf"), r.Path);
        Assert.Equal("v1", Read(r.Path));
    }

    [Fact]
    public async Task DoesNotRedownloadUnchangedFile()
    {
        await _lib.ImportFileAsync(Req());
        var downloaded = false;
        var r = await _lib.ImportFileAsync(Req(fetch: () => { downloaded = true; return Task.FromResult(B("v1")); }));
        Assert.Equal(ImportStatus.Unchanged, r.Status);
        Assert.False(downloaded);
    }

    [Fact]
    public async Task UpdatesFileChangedOnEprel_WhenUserDidNotTouchIt()
    {
        var a = await _lib.ImportFileAsync(Req());
        var r = await _lib.ImportFileAsync(Req("v2!", remoteModified: 200));
        Assert.Equal(ImportStatus.Updated, r.Status);
        Assert.Equal("v2!", Read(a.Path));
    }

    [Fact]
    public async Task NeverOverwritesUserModifiedFile_Conflict()
    {
        var a = await _lib.ImportFileAsync(Req());
        File.WriteAllText(_lib.Abs(a.Path), "mes annotations");
        var r = await _lib.ImportFileAsync(Req("v2!", remoteModified: 200));
        Assert.Equal(ImportStatus.Conflict, r.Status);
        Assert.Equal("mes annotations", Read(a.Path));
        Assert.Equal("v2!", Read(r.Path));
        Assert.Contains("version EPREL", r.Path);
    }

    [Fact]
    public async Task AvoidsContentDuplicatesInSameCourseCategory()
    {
        var a = await _lib.ImportFileAsync(Req());
        var b = await _lib.ImportFileAsync(Req(remoteKey: "k2", name: "copie.pdf"));
        Assert.Equal(ImportStatus.Duplicate, b.Status);
        Assert.Equal(a.FileId, b.FileId);
        Assert.False(File.Exists(_lib.Abs(Path.Combine("Semestre 1", "Programmation C", "TD", "copie.pdf"))));
        var dl = false;
        await _lib.ImportFileAsync(Req(remoteKey: "k2", name: "copie.pdf", fetch: () => { dl = true; return Task.FromResult(B("v1")); }));
        Assert.False(dl);
    }

    [Fact]
    public async Task RejectsIncompleteDownload_WritesNothing()
    {
        var e = await Assert.ThrowsAnyAsync<Exception>(() => _lib.ImportFileAsync(Req(remoteSize: 50)));
        Assert.Contains("incomplet", e.Message);
        Assert.Equal(0L, _db.Scalar<long>("SELECT COUNT(*) FROM files"));
    }

    [Fact]
    public async Task SameNameDifferentContent_Coexist()
    {
        var a = await _lib.ImportFileAsync(Req("AAA"));
        var b = await _lib.ImportFileAsync(Req("BBB", remoteKey: "k2"));
        Assert.Contains("td1 (2).pdf", b.Path);
        Assert.NotEqual(a.Path, b.Path);
    }

    [Fact]
    public async Task MoveAndRename_ActOnDiskAndDb()
    {
        var a = await _lib.ImportFileAsync(Req());
        var m = _lib.MoveFile(a.FileId, category: "Corrections", subfolder: "Série 1");
        Assert.False(File.Exists(_lib.Abs(a.Path)));
        Assert.Equal("v1", Read(m.LocalPath));
        Assert.Equal("Corrections", m.Category);
        var n = _lib.RenameFile(a.FileId, "correction:td1.pdf");
        Assert.EndsWith("correction_td1.pdf", n.LocalPath);
        Assert.True(File.Exists(_lib.Abs(n.LocalPath)));
    }

    [Fact]
    public async Task Reconcile_FindsFileMovedInExplorer()
    {
        var a = await _lib.ImportFileAsync(Req());
        var elsewhere = Path.Combine(_lib.Root, "Archives", "ancien");
        Directory.CreateDirectory(elsewhere);
        File.Move(_lib.Abs(a.Path), Path.Combine(elsewhere, "renommé.pdf"));
        Assert.Equal((1, 0), _lib.Reconcile());
        Assert.Equal(Path.Combine("Archives", "ancien", "renommé.pdf"), _lib.GetFile(a.FileId).LocalPath);
    }

    [Fact]
    public void Drop_ExternalFileIsCopied_OriginalStays()
    {
        var ext = _t.Combine("mon_compte_rendu.docx");
        File.WriteAllText(ext, "CR");
        var r = _lib.Drop([ext], _course, null);
        Assert.Equal((1, 0), (r.Imported, r.Moved));
        Assert.Equal("CR", Read(Path.Combine("Semestre 1", "Programmation C", "Personnel", "mon_compte_rendu.docx")));
        Assert.True(File.Exists(ext));
        Assert.Equal(1, _lib.Drop([ext], _course, null).Duplicates);
    }

    [Fact]
    public async Task Drop_LibraryFileIsMoved_NotCopied()
    {
        var a = await _lib.ImportFileAsync(Req());
        var other = _db.Insert("INSERT INTO courses(remote_id,name,semester,folder) VALUES(2,'Maths','Semestre 1','Maths')");
        Assert.Equal(1, _lib.Drop([_lib.Abs(a.Path)], _course, "TP").Moved);
        Assert.Equal("TP", _lib.GetFile(a.FileId).Category);
        var r = _lib.Drop([_lib.Abs(_lib.GetFile(a.FileId).LocalPath)], other, null);
        Assert.Equal(1, r.Moved);
        Assert.Equal(Path.Combine("Semestre 1", "Maths", "TP", "td1.pdf"), _lib.GetFile(a.FileId).LocalPath);
        Assert.Equal(1L, _db.Scalar<long>("SELECT COUNT(*) FROM files"));
    }

    [Fact]
    public void Drop_FoldersAndMissingFiles_SkippedAndReported()
    {
        Directory.CreateDirectory(_t.Combine("un dossier"));
        var r = _lib.Drop([_t.Combine("un dossier"), _t.Combine("absent.pdf")], _course, null);
        Assert.Equal(["un dossier (dossier)", "absent.pdf (introuvable)"], r.Skipped);
    }

    [Fact]
    public async Task Reconcile_ReportsReallyMissingFile_WithoutDeleting()
    {
        var a = await _lib.ImportFileAsync(Req());
        File.Delete(_lib.Abs(a.Path));
        Assert.Equal((0, 1), _lib.Reconcile());
        Assert.True(_lib.GetFile(a.FileId).Missing);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern uint GetShortPathNameW(string longPath, StringBuilder shortPath, uint size);

    [Fact]
    public async Task FindByAbsPathAcceptsShortAndAccentedPaths()
    {
        var c = _db.Insert("INSERT INTO courses(remote_id,name,semester,folder) VALUES(3,'Matière é','Semestre 1','Matière é longue')");
        var r = await _lib.ImportFileAsync(new(c, null, "TD", "x.pdf", "k9", 1, 1, () => Task.FromResult(B("x"))));
        var abs = _lib.Abs(r.Path);
        Assert.Equal(r.FileId, _lib.FindByAbsPath(abs)?.Id);
        Assert.Equal(r.FileId, _lib.FindByAbsPath(abs.ToUpperInvariant())?.Id);
        var sb = new StringBuilder(1024);
        GetShortPathNameW(abs, sb, 1024);
        var shortPath = sb.ToString();
        if (shortPath.Length > 0 && shortPath != abs) // noms 8.3 actifs sur ce volume
            Assert.Equal(r.FileId, _lib.FindByAbsPath(shortPath)?.Id);
    }
}
