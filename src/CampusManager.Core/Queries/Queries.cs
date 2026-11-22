using System.Text.Json;
using CampusManager.Core.Library;

namespace CampusManager.Core.Queries;

/// <summary>Lectures pour l'interface et le widget : uniquement la base locale, jamais EPREL.</summary>
public static class Queries
{
    const string FileSelect = """
        SELECT f.id, f.original_name, f.local_path, f.category, f.course_id, f.size, f.added_at, f.favorite, f.missing, f.remote_deleted,
               COALESCE(c.folder, '') AS course, COALESCE(c.semester, '') AS semester
        FROM files f LEFT JOIN courses c ON c.id = f.course_id
        LEFT JOIN resources r ON r.id = f.resource_id
        """;

    sealed class FileRowQ
    {
        public long Id { get; init; } public string OriginalName { get; init; } = ""; public string LocalPath { get; init; } = "";
        public string Category { get; init; } = ""; public long? CourseId { get; init; } public long Size { get; init; } public long AddedAt { get; init; }
        public bool Favorite { get; init; } public bool Missing { get; init; } public bool RemoteDeleted { get; init; }
        public string Course { get; init; } = ""; public string Semester { get; init; } = "";
    }

    static FileItem ToItem(FileRowQ r)
    {
        var name = Path.GetFileName(r.LocalPath);
        if (string.IsNullOrEmpty(name)) name = r.OriginalName;
        return new(r.Id, name, Names.Extension(name).TrimStart('.').ToLowerInvariant(), r.Category, r.CourseId, r.Course, r.Semester,
            r.Size, r.AddedAt, r.Favorite, r.Missing, r.RemoteDeleted, r.LocalPath);
    }

    public static List<FileItem> ListFiles(Db.Db db, FileQuery q)
    {
        var where = new List<string> { "f.user_deleted = 0" };
        var args = new Dapper.DynamicParameters();
        if (q.CourseId != null) { where.Add("f.course_id = @courseId"); args.Add("courseId", q.CourseId); }
        if (!string.IsNullOrEmpty(q.Category)) { where.Add("f.category = @category"); args.Add("category", q.Category); }
        if (q.Categories is { Count: > 0 }) { where.Add("f.category IN @cats"); args.Add("cats", q.Categories); }
        if (q.Favorites) where.Add("f.favorite = 1");
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            // Recherche dans le nom, le chemin, la matière, la catégorie, le titre et la description EPREL.
            var terms = q.Search.Trim().ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < terms.Length; i++)
            {
                // Chaque mot doit se trouver dans le nom, la matière, la catégorie, la ressource EPREL… ou dans le texte du fichier.
                var inContent = Search.ContentIndex.Indexable(terms[i]) ? $" OR f.id IN (SELECT rowid FROM content_fts WHERE content_fts MATCH @m{i})" : "";
                where.Add($"(lower(f.local_path) LIKE @t{i} OR lower(c.name) LIKE @t{i} OR lower(f.category) LIKE @t{i} OR lower(COALESCE(r.name,'')) LIKE @t{i} OR lower(COALESCE(r.description,'')) LIKE @t{i}{inContent})");
                args.Add($"t{i}", $"%{terms[i]}%");
                if (inContent.Length > 0) args.Add($"m{i}", Search.ContentIndex.Quote(terms[i]));
            }
        }
        var order = q.Recent ? "f.added_at DESC, f.id DESC" : "c.semester, c.folder, f.category, f.local_path";
        args.Add("limit", q.Limit);
        return db.All<FileRowQ>($"{FileSelect} WHERE {string.Join(" AND ", where)} ORDER BY {order} LIMIT @limit", args).Select(ToItem).ToList();
    }

    public static List<SemesterNode> Tree(Db.Db db)
    {
        var courses = db.All<(long Id, string Name, string Folder, string Semester)>("SELECT id, name, folder, semester FROM courses ORDER BY semester, folder");
        var cats = db.All<(long CourseId, string Category, long N)>("SELECT course_id, category, COUNT(*) n FROM files WHERE user_deleted = 0 GROUP BY course_id, category");
        var sems = new List<SemesterNode>();
        foreach (var c in courses)
        {
            var node = new CourseNode(c.Id, c.Name, c.Folder, c.Semester, cats.Where(x => x.CourseId == c.Id).Select(x => new CategoryCount(x.Category, x.N)).ToList());
            var s = sems.FirstOrDefault(x => x.Name == c.Semester);
            if (s == null) sems.Add(s = new SemesterNode(c.Semester, []));
            s.Courses.Add(node);
        }
        // « Autres matières » en dernier.
        return sems.OrderBy(s => s.Name == "Autres matières" ? 1 : 0).ThenBy(s => s.Name, StringComparer.CurrentCulture).ToList();
    }

    /// <summary>Début de l'année universitaire en cours : 15 août (heure locale).</summary>
    public static long AcademicYearStart(long now)
    {
        var d = DateTimeOffset.FromUnixTimeSeconds(now).LocalDateTime;
        var year = d.Month > 8 || (d.Month == 8 && d.Day >= 15) ? d.Year : d.Year - 1;
        return new DateTimeOffset(new DateTime(year, 8, 15, 0, 0, 0, DateTimeKind.Local)).ToUnixTimeSeconds();
    }

    sealed class AssignRow
    {
        public long Id { get; init; } public string Name { get; init; } = ""; public string? Description { get; init; }
        public long? DueAt { get; init; } public long? PublishedAt { get; init; } public string Status { get; init; } = "todo";
        public long Priority { get; init; } public string? Notes { get; init; } public long? CourseId { get; init; }
        public long Submitted { get; init; } public long? Startdate { get; init; } public string Course { get; init; } = "";
    }

    public static List<AssignmentItem> ListAssignments(Db.Db db, long now)
    {
        var rows = db.All<AssignRow>("""
            SELECT a.id, a.name, a.description, a.due_at, a.published_at, a.status, a.priority, a.notes, a.course_id,
                   a.submitted, c.startdate, COALESCE(c.folder, 'Personnel') AS course
            FROM assignments a LEFT JOIN courses c ON c.id = a.course_id
            WHERE a.remote_deleted = 0 ORDER BY a.due_at IS NULL, a.due_at
            """);
        var yearStart = AcademicYearStart(now);
        return rows.Select(r =>
        {
            // Échéance d'une année précédente (espaces EPREL réutilisés) ou antérieure au début du cours :
            // affichée à part, jamais comptée en retard.
            var old = r.DueAt != null && (r.DueAt < yearStart || (r.Startdate is > 0 && r.DueAt < r.Startdate));
            return new AssignmentItem(r.Id, r.Name, r.Course, r.CourseId, r.Description, r.DueAt, r.PublishedAt, r.Status,
                // « En retard » est calculé à partir de la vraie date limite, jamais stocké ni inventé.
                Late: !old && r.Status != "done" && r.DueAt != null && r.DueAt < now,
                r.Priority, r.Notes, r.Submitted != 0, old);
        }).ToList();
    }

    public static void SetAssignment(Db.Db db, long id, string? status = null, int? priority = null, string? notes = null, bool notesSet = false)
    {
        if (status != null && status is not ("todo" or "doing" or "done")) throw new ArgumentException("Statut invalide");
        // Un statut choisi à la main n'est plus jamais modifié par la synchronisation.
        if (status != null) db.Execute("UPDATE assignments SET status=@status, status_source='manual' WHERE id=@id", new { status, id });
        if (priority != null) db.Execute("UPDATE assignments SET priority=@priority WHERE id=@id", new { priority, id });
        if (notesSet) db.Execute("UPDATE assignments SET notes=@notes WHERE id=@id", new { notes, id });
    }

    static long? LastSyncAt(Db.Db db) => db.One<long?>("SELECT finished_at FROM sync_log WHERE finished_at IS NOT NULL ORDER BY id DESC LIMIT 1");

    /// <summary>Données du widget : uniquement la base locale (fonctionne hors ligne).</summary>
    public static WidgetData WidgetData(Db.Db db, long now)
    {
        var all = ListAssignments(db, now).Where(a => !a.Old).ToList();
        var open = all.Where(a => a.Status != "done").ToList();
        var work = ListFiles(db, new() { Categories = ["TD", "TP", "Exercices"], Recent = true, Limit = 5 });
        var shown = work.Select(f => f.Id).ToHashSet();
        return new(
            open.Where(a => a.Late).Concat(open.Where(a => !a.Late)).Take(6).ToList(),
            work,
            // Pas de doublon avec la section TD/TP.
            ListFiles(db, new() { Recent = true, Limit = 20 }).Where(f => !shown.Contains(f.Id)).Take(5).ToList(),
            new(all.Count - open.Count, open.Count, open.Count(a => a.Late)),
            LastSyncAt(db));
    }

    public static Dashboard Dashboard(Db.Db db, long now)
    {
        var all = ListAssignments(db, now).Where(a => !a.Old).ToList();
        var open = all.Where(a => a.Status != "done").ToList();
        long N(string sql) => db.Scalar<long>(sql);
        var perCourse = all.GroupBy(a => a.Course).Select(g => new CourseProgress(g.Key, g.Count(a => a.Status == "done"), g.Count())).ToList();
        var last = db.One<(long FinishedAt, long Ok, string? Summary)?>("SELECT finished_at, ok, summary FROM sync_log WHERE finished_at IS NOT NULL ORDER BY id DESC LIMIT 1");
        LastSync? lastSync = null;
        if (last is { } l)
        {
            int added = 0, errors = 0;
            try
            {
                var s = JsonDocument.Parse(l.Summary ?? "{}").RootElement;
                if (s.TryGetProperty("added", out var a) && a.ValueKind == JsonValueKind.Number) added = a.GetInt32();
                if (s.TryGetProperty("errors", out var e) && e.ValueKind == JsonValueKind.Array) errors = e.GetArrayLength();
            }
            catch (JsonException) { }
            lastSync = new(l.FinishedAt, l.Ok != 0, added, errors);
        }
        return new(
            // Les retards d'abord (à traiter), puis les prochaines échéances.
            open.Where(a => a.Late).Take(3).Concat(open.Where(a => !a.Late).Take(6)).ToList(),
            ListFiles(db, new() { Recent = true, Limit = 8 }),
            new(N("SELECT COUNT(*) FROM files WHERE user_deleted=0"), N("SELECT COUNT(*) FROM courses"),
                N("SELECT COUNT(*) FROM files WHERE user_deleted=0 AND category='TD'"), N("SELECT COUNT(*) FROM files WHERE user_deleted=0 AND category='TP'"),
                open.Count, all.Count - open.Count, open.Count(a => a.Late),
                N("SELECT COUNT(*) FROM files WHERE user_deleted=0 AND category='À classer'")),
            perCourse, lastSync);
    }

    public static FileDetails Details(Db.Db db, Library.Library lib, long id)
    {
        var f = lib.GetFile(id);
        var abs = lib.Abs(f.LocalPath);
        var item = ListFiles(db, new() { Limit = 1_000_000 }).FirstOrDefault(x => x.Id == id)
            ?? throw new InvalidOperationException("Fichier introuvable");
        var r = db.One<(string? Name, string? Section, string? Description)?>(
            "SELECT r.name, r.section, r.description FROM files f LEFT JOIN resources r ON r.id=f.resource_id WHERE f.id=@id", new { id });
        var exists = File.Exists(abs);
        return new(item, exists ? new DateTimeOffset(File.GetLastWriteTimeUtc(abs)).ToUnixTimeSeconds() : null,
            exists && lib.IsLocallyModified(f), r?.Name, r?.Section, string.IsNullOrEmpty(r?.Description) ? null : r?.Description,
            f.RemoteKey != null, abs);
    }
}
