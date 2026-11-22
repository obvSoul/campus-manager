using CampusManager.Core.Db;
using CampusManager.Core.Library;
using CampusManager.Core.Sync;

namespace CampusManager.Tests;

public sealed class SyncTests : IDisposable
{
    readonly TempDir _t = new();
    readonly Db _db = Db.Open(":memory:");
    readonly Library _lib;
    readonly FakeMoodle _api = new();

    public SyncTests()
    {
        _lib = new Library(_db, _t.Combine("Campus Manager"));
        _api.Store["cm1.pdf"] = "COURS1"; _api.Store["td1.pdf"] = "TD1"; _api.Store["td1_corrige.pdf"] = "CORR";
        _api.Sections =
        [
            new(1, "Cours magistraux", [new(11, "CM1", "resource", Contents: [FakeMoodle.File("cm1.pdf", "COURS1")])]),
            new(2, "Travaux dirigés",
            [
                new(12, "TD1", "resource", Contents: [FakeMoodle.File("td1.pdf", "TD1")]),
                new(13, "Correction du TD1", "resource", Contents: [FakeMoodle.File("td1_corrige.pdf", "CORR")]),
            ]),
        ];
    }
    public void Dispose() { _db.Dispose(); _t.Dispose(); }

    Task<SyncReport> Sync(IMoodleApi? api = null) => SyncEngine.SyncAllAsync(api ?? _api, _db, _lib, retryMs: 1);
    string Base => Path.Combine(_lib.Root, "Semestre 1", "Programmation C S1");

    [Fact]
    public async Task DownloadsAndFilesBySemesterCourseCategory()
    {
        var r = await Sync();
        Assert.Equal((1, 3), (r.Courses, r.Added));
        Assert.Empty(r.Errors);
        Assert.Equal("COURS1", File.ReadAllText(Path.Combine(Base, "Cours", "cm1.pdf")));
        Assert.Equal("TD1", File.ReadAllText(Path.Combine(Base, "TD", "td1.pdf")));
        Assert.Equal("CORR", File.ReadAllText(Path.Combine(Base, "Corrections", "td1_corrige.pdf")));
    }

    [Fact]
    public async Task SecondSync_DownloadsNothing()
    {
        await Sync();
        _api.Downloads.Clear();
        var r = await Sync();
        Assert.Equal((0, 3), (r.Added, r.Unchanged));
        Assert.Empty(_api.Downloads);
    }

    [Fact]
    public async Task ResourceGoneFromEprel_FlaggedLocalCopyKept()
    {
        await Sync();
        _api.Sections[1].Modules.RemoveAt(1);
        var r = await Sync();
        Assert.Equal(1, r.RemoteGone);
        Assert.True(File.Exists(Path.Combine(Base, "Corrections", "td1_corrige.pdf")));
        Assert.Equal(1L, _db.Scalar<long>("SELECT COUNT(*) FROM files WHERE remote_deleted=1"));
    }

    [Fact]
    public async Task UserModifiedThenUpdatedOnEprel_ConflictNothingOverwritten()
    {
        await Sync();
        var p = Path.Combine(Base, "TD", "td1.pdf");
        File.WriteAllText(p, "MES NOTES");
        _api.Store["td1.pdf"] = "TD1 v2!";
        _api.Sections[1].Modules[0] = _api.Sections[1].Modules[0] with { Contents = [FakeMoodle.File("td1.pdf", "TD1 v2!", 200)] };
        var r = await Sync();
        Assert.Single(r.Conflicts);
        Assert.Equal("MES NOTES", File.ReadAllText(p));
    }

    [Fact]
    public async Task TransientNetworkError_Retried()
    {
        _api.FailNext = new MoodleException("coupure", "network");
        var r = await Sync();
        Assert.Empty(r.Errors);
        Assert.Equal(3, r.Added);
    }

    [Fact]
    public async Task ExpiredToken_ReportedLocalDataKept()
    {
        await Sync();
        _api.Courses = () => throw new MoodleException("jeton", "invalidtoken");
        var r = await Sync();
        Assert.True(r.AuthExpired);
        Assert.Equal(3L, _db.Scalar<long>("SELECT COUNT(*) FROM files"));
    }

    [Fact]
    public async Task Assignments_RealDueDate_NeverInvented_PersonalStatusKept()
    {
        _api.Assigns =
        [
            new(1, 5, 7, "Rendu du TD#3", "<p>Rendre le <b>TD</b></p>", 1790000000, 0, 1),
            new(2, 6, 7, "Sans date", null, 0, 0, 1),
        ];
        await Sync();
        _db.Execute("UPDATE assignments SET status='done' WHERE remote_id=1");
        _api.Assigns[0] = _api.Assigns[0] with { Duedate = 1790086400 }; // l'enseignant décale la date
        await Sync();
        var a = _db.One<(long DueAt, string Status, string Description)>("SELECT due_at, status, description FROM assignments WHERE remote_id=1");
        Assert.Equal((1790086400L, "done", "Rendre le TD"), a);
        Assert.Null(_db.Scalar<long?>("SELECT due_at FROM assignments WHERE remote_id=2"));
    }

    [Fact]
    public async Task Assignments_TimelineDate_SubmittedIsDone_ManualChoiceKept()
    {
        _api.Assigns =
        [
            new(48508, 900, 7, "Rendu du TD#3", null, 1762901940, 0, 1),
            new(2, 901, 7, "TP rendu", null, 1790000000, 0, 1),
        ];
        var subs = new Dictionary<long, string> { [48508] = "new", [2] = "submitted" };
        _api.Submission = id => Task.FromResult<string?>(subs.GetValueOrDefault(id));
        _api.Events = _ => Task.FromResult(new List<ActionEvent> { new("assign", 417783, 1794092340, "https://e/mod/assign/view.php?id=900") });
        await Sync();
        Assert.Equal((1794092340L, 1762901940L, "todo"),
            _db.One<(long, long, string)>("SELECT due_at, base_due_at, status FROM assignments WHERE remote_id=48508"));
        Assert.Equal(("done", 1L), _db.One<(string, long)>("SELECT status, submitted FROM assignments WHERE remote_id=2"));
        // choix manuel « en cours » malgré le rendu : conservé (équivaut à Queries.SetAssignment)
        _db.Execute("UPDATE assignments SET status='doing', status_source='manual' WHERE remote_id=2");
        await Sync();
        Assert.Equal("doing", _db.Scalar<string>("SELECT status FROM assignments WHERE remote_id=2"));
    }

    [Fact]
    public async Task ManualCategoryFix_KeptOnNextSyncs()
    {
        await Sync();
        _db.Execute("UPDATE resources SET category='Projets', category_source='manual' WHERE remote_id=11");
        await Sync();
        Assert.Equal("Projets", _db.Scalar<string>("SELECT category FROM resources WHERE remote_id=11"));
    }

    [Fact]
    public void ButCodification_SemesterAndFolderWithoutPrefix()
    {
        Assert.Equal("Semestre 1", SyncEngine.GuessSemester("BUT INFO : R1.01 - Initiation au développement", ""));
        Assert.Equal("Semestre 1", SyncEngine.GuessSemester("BUT INFO : SAE1.07-2.07 - Portfolio", ""));
        Assert.Equal("Autres matières", SyncEngine.GuessSemester("BUT INFO : Informations générales", ""));
        Assert.Equal("R1.03 - Introduction à l'architecture", SyncEngine.CourseFolderName("BUT INFO : R1.03 -  Introduction à l'architecture"));
    }

    [Fact]
    public async Task BetterLayout_FilesMovedNotRedownloaded()
    {
        await Sync();
        _db.Execute("UPDATE courses SET semester='Autres matières', folder='BUT INFO _ vieux'");
        Directory.CreateDirectory(Path.Combine(_lib.Root, "Autres matières"));
        Directory.Move(Base, Path.Combine(_lib.Root, "Autres matières", "BUT INFO _ vieux"));
        _db.Execute("UPDATE files SET local_path=replace(local_path, @a, @b)",
            new { a = Path.Combine("Semestre 1", "Programmation C S1"), b = Path.Combine("Autres matières", "BUT INFO _ vieux") });
        _api.Downloads.Clear();
        var r = await Sync();
        Assert.Empty(r.Errors);
        Assert.Empty(_api.Downloads);
        Assert.Equal("TD1", File.ReadAllText(Path.Combine(Base, "TD", "td1.pdf")));
        Assert.False(Directory.Exists(Path.Combine(_lib.Root, "Autres matières")));
    }

    [Fact]
    public void Semester_OnlyWhenWrittenInName()
    {
        Assert.Equal("Semestre 2", SyncEngine.GuessSemester("Maths S2", "R2.01"));
        Assert.Equal("Autres matières", SyncEngine.GuessSemester("Algorithmique", "ALGO"));
    }
}
