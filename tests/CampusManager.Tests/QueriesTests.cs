using System.Text;
using System.Text.Json;
using CampusManager.Core.Db;
using CampusManager.Core.Library;
using CampusManager.Core.Queries;
using CampusManager.Core.Sync;

namespace CampusManager.Tests;

public sealed class QueriesTests : IAsyncLifetime
{
    readonly TempDir _t = new();
    readonly Db _db = Db.Open(":memory:");
    Library _lib = null!;
    long _c1, _c2;

    Task Add(long courseId, string category, string name, string data) =>
        _lib.ImportFileAsync(new(courseId, null, category, name, name, 1, data.Length, () => Task.FromResult(Encoding.UTF8.GetBytes(data))));

    public async Task InitializeAsync()
    {
        _lib = new Library(_db, _t.Path);
        _c1 = _db.Insert("INSERT INTO courses(remote_id,name,semester,folder) VALUES(1,'BUT INFO : R1.01 - Dev','Semestre 1','R1.01 - Dev')");
        _c2 = _db.Insert("INSERT INTO courses(remote_id,name,semester,folder) VALUES(2,'Infos','Autres matières','Infos')");
        await Add(_c1, "TD", "td1_boucles.pdf", "A");
        await Add(_c1, "TP", "tp1.pdf", "B");
        await Add(_c2, "À classer", "guide.pdf", "C");
    }
    public Task DisposeAsync() { _db.Dispose(); _t.Dispose(); return Task.CompletedTask; }

    static long T(string s) => new DateTimeOffset(DateTime.Parse(s, System.Globalization.CultureInfo.InvariantCulture)).ToUnixTimeSeconds();
    List<string> Names(FileQuery q) => Queries.ListFiles(_db, q).Select(f => f.Name).ToList();

    [Fact]
    public void Tree_SemestersCoursesCategoriesWithCounts_OthersLast()
    {
        var t = Queries.Tree(_db);
        Assert.Equal(["Semestre 1", "Autres matières"], t.Select(s => s.Name));
        var cats = t[0].Courses[0].Categories;
        Assert.Contains(new CategoryCount("TD", 1), cats);
        Assert.Contains(new CategoryCount("TP", 1), cats);
    }

    [Fact]
    public void FiltersAndSearch()
    {
        Assert.Equal(["td1_boucles.pdf", "tp1.pdf"], Names(new() { CourseId = _c1 }));
        Assert.Equal(["td1_boucles.pdf"], Names(new() { Search = "boucles" }));
        Assert.Equal(["tp1.pdf"], Names(new() { Search = "r1.01 tp" }));
        Assert.Equal(2, Queries.ListFiles(_db, new() { Categories = ["TD", "TP"] }).Count);
    }

    [Fact]
    public async Task FavoritesAndTrash_DeletedFileHiddenAndNotRedownloaded()
    {
        var f = Queries.ListFiles(_db, new() { Search = "tp1" })[0];
        _lib.SetFavorite(f.Id, true);
        Assert.Equal([f.Id], Queries.ListFiles(_db, new() { Favorites = true }).Select(x => x.Id));
        _lib.MarkDeleted(f.Id);
        Assert.Empty(Queries.ListFiles(_db, new() { Search = "tp1" }));
        var dl = false;
        var r = await _lib.ImportFileAsync(new(_c1, null, "TP", "tp1.pdf", "tp1.pdf", 2, 2, () => { dl = true; return Task.FromResult(new byte[2]); }));
        Assert.Equal(ImportStatus.Unchanged, r.Status);
        Assert.False(dl);
    }

    [Fact]
    public void Move_IsRememberedManualFix()
    {
        _db.Execute("INSERT INTO resources(id,course_id,remote_id,name,category) VALUES(9,@c,99,'guide','À classer')", new { c = _c2 });
        var f = Queries.ListFiles(_db, new() { Search = "guide" })[0];
        _db.Execute("UPDATE files SET resource_id=9 WHERE id=@id", new { id = f.Id });
        _lib.MoveFile(f.Id, category: "Cours");
        Assert.Equal(("Cours", "manual"), _db.One<(string, string)>("SELECT category, category_source FROM resources WHERE id=9"));
    }

    [Fact]
    public void AcademicYear_StartsAug15_EarlierDueDatesAreOld()
    {
        Assert.Equal(T("2026-08-15T00:00:00"), Queries.AcademicYearStart(T("2026-11-07T17:00:00")));
        Assert.Equal(T("2026-08-15T00:00:00"), Queries.AcademicYearStart(T("2027-03-01T10:00:00")));
        Assert.Equal(T("2025-08-15T00:00:00"), Queries.AcademicYearStart(T("2026-08-10T10:00:00")));
        _db.Execute("INSERT INTO assignments(remote_id,course_id,name,due_at) VALUES(50,@c,'TP 2025',@a), (51,@c,'TD 2026',@b)",
            new { c = _c1, a = T("2025-10-05T23:59:00"), b = T("2026-11-07T23:59:00") });
        var a = Queries.ListAssignments(_db, T("2026-11-08T10:00:00"));
        Assert.Equal([("TP 2025", true, false), ("TD 2026", false, true)], a.Select(x => (x.Name, x.Old, x.Late)));
    }

    [Fact]
    public void Assignments_LateFromRealDate_LocalStatus()
    {
        const long now = 1_000_000;
        _db.Execute("INSERT INTO assignments(remote_id,course_id,name,due_at) VALUES(1,@c,'Passé',@p), (2,@c,'Futur',@f), (3,@c,'Sans date',NULL)",
            new { c = _c1, p = now - 10, f = now + 3600 });
        var a = Queries.ListAssignments(_db, now);
        Assert.Equal([("Passé", true), ("Futur", false), ("Sans date", false)], a.Select(x => (x.Name, x.Late)));
        Queries.SetAssignment(_db, a[0].Id, status: "done");
        Assert.False(Queries.ListAssignments(_db, now)[0].Late);
        Assert.ThrowsAny<Exception>(() => Queries.SetAssignment(_db, 1, status: "x"));
        var d = Queries.Dashboard(_db, now);
        Assert.Equal((3, 1, 1, 1, 2, 1), (d.Counts.Files, d.Counts.Td, d.Counts.Tp, d.Counts.AssignmentsDone, d.Counts.AssignmentsOpen, d.Counts.Unsorted));
        Assert.Equal(["Futur", "Sans date"], d.Upcoming.Select(x => x.Name));
    }

    [Fact]
    public async Task AssignmentBeforeCourseStart_IsOld_NeverLate()
    {
        var api = new FakeMoodle
        {
            Courses = () => Task.FromResult(new List<MoodleCourse> { new(7, "Programmation C S1", "R1.01", 1788000000) }),
            Assigns = [new(5, 5, 7, "Vieux TP", null, 1759000000, 0, 1)],
        };
        await SyncEngine.SyncAllAsync(api, _db, _lib, 1);
        var a = Queries.ListAssignments(_db, 1800000000).Single(x => x.Name == "Vieux TP");
        Assert.True(a.Old);
        Assert.False(a.Late);
    }

    [Fact]
    public void WidgetSettingsJsonFromElectronIsRead()
    {
        var s = WidgetSettings.FromJson("""{"enabled":true,"opacity":0.3,"sections":{"recent":false}}""");
        Assert.Equal(0.3, s.Opacity);
        Assert.False(s.Sections.Recent);
        Assert.True(s.Sections.Assignments);
        Assert.True(s.AutoHideFullscreen);
        Assert.Equal("dark", s.Theme);
        var json = JsonDocument.Parse(s.ToJson()).RootElement;
        Assert.True(json.TryGetProperty("clickThrough", out _));
    }

    [Fact]
    public void Details_ReportsLocalModificationAndPath()
    {
        var f = Queries.ListFiles(_db, new() { Search = "tp1" })[0];
        var d = Queries.Details(_db, _lib, f.Id);
        Assert.False(d.LocallyModified);
        Assert.True(d.FromEprel);
        Assert.Equal(_lib.Abs(f.Path), d.AbsPath);
        File.WriteAllText(d.AbsPath, "modifié");
        Assert.True(Queries.Details(_db, _lib, f.Id).LocallyModified);
    }
}
