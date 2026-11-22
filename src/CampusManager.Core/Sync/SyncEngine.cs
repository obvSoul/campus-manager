using System.Text.Json;
using System.Text.RegularExpressions;
using CampusManager.Core.Classify;
using CampusManager.Core.Library;

namespace CampusManager.Core.Sync;

public record SyncReport
{
    public int Courses { get; set; }
    public int Added { get; set; }
    public int Updated { get; set; }
    public int Unchanged { get; set; }
    public int Duplicates { get; set; }
    public List<string> Conflicts { get; init; } = [];
    public List<string> Errors { get; init; } = [];
    public int RemoteGone { get; set; }
    public int Assignments { get; set; }
    public bool AuthExpired { get; set; }
}

/// <summary>
/// Moteur de synchronisation : EPREL → disque + base locale. Rien ne remonte vers EPREL.
/// N'écrase jamais un fichier modifié par l'utilisateur, ne supprime jamais une copie locale quand une
/// ressource disparaît d'EPREL (elle est seulement signalée), une erreur sur un fichier n'arrête pas les
/// autres, les erreurs réseau sont réessayées.
/// </summary>
public static partial class SyncEngine
{
    public static async Task<T> WithRetry<T>(Func<Task<T>> fn, int tries = 3, int baseMs = 1000)
    {
        for (var i = 1; ; i++)
        {
            try { return await fn(); }
            catch (MoodleException e) when (e.IsTransient && i < tries) { await Task.Delay(baseMs * (1 << (i - 1))); }
        }
    }

    [GeneratedRegex(@"\b(?:R|SA[EÉ])\s?([1-6])\.\d{1,2}", RegexOptions.IgnoreCase)] private static partial Regex ButCode();
    [GeneratedRegex(@"\b(?:semestre\s*|s)([1-6])\b", RegexOptions.IgnoreCase)] private static partial Regex SemesterWord();
    [GeneratedRegex(@"^\s*BUT\s+INFO\s*:\s*", RegexOptions.IgnoreCase)] private static partial Regex ButPrefix();
    [GeneratedRegex(@"\s+")] private static partial Regex Spaces();
    [GeneratedRegex("<[^>]+>")] private static partial Regex Tags();

    /// <summary>Déduit le semestre seulement s'il est écrit dans le nom ; sinon « Autres matières ».</summary>
    public static string GuessSemester(string fullname, string shortname)
    {
        var t = $"{fullname} {shortname}";
        // Codification du BUT : R1.01, SAE1.02, SAÉ 2.03 → le premier chiffre est le semestre.
        var m = ButCode().Match(t);
        if (!m.Success) m = SemesterWord().Match(t);
        return m.Success ? $"Semestre {m.Groups[1].Value}" : "Autres matières";
    }

    /// <summary>Nom de dossier lisible : sans le préfixe commun « BUT INFO : ».</summary>
    public static string CourseFolderName(string fullname) => Names.Sanitize(Spaces().Replace(ButPrefix().Replace(fullname, ""), " "), 80);

    static string StripQuery(string u) => u.Split('?')[0];
    static string Text(string? html) => Spaces().Replace(Tags().Replace(html ?? "", " "), " ").Trim();
    /// <summary>mod/assign/view.php?id=123 → 123</summary>
    static long? CmidOf(string? url)
    {
        if (url == null || !Uri.TryCreate(url, UriKind.Absolute, out var u)) return null;
        var q = System.Web.HttpUtility.ParseQueryString(u.Query)["id"];
        return long.TryParse(q, out var n) && n != 0 ? n : null;
    }

    public static async Task<SyncReport> SyncAllAsync(IMoodleApi api, Db.Db db, Library.Library lib, int retryMs = 1000, long? now = null)
    {
        var rep = new SyncReport();
        long Now() => now ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var logId = db.Insert("INSERT INTO sync_log(started_at) VALUES(@t)", new { t = Now() });
        try
        {
            var remoteCourses = await WithRetry(api.CoursesAsync, 3, retryMs);
            rep.Courses = remoteCourses.Count;
            var idOf = new Dictionary<long, long>();

            foreach (var c in remoteCourses)
            {
                var row = db.One<(long Id, string Semester, string Folder, long LayoutManual)?>(
                    "SELECT id, semester, folder, layout_manual FROM courses WHERE remote_id=@id", new { id = c.Id });
                if (row is { } r)
                {
                    db.Execute("UPDATE courses SET name=@n, shortname=@s WHERE id=@id", new { n = c.Fullname, s = c.Shortname, id = r.Id });
                    // Rangement amélioré : on déplace (sans retélécharger), sauf réglage manuel.
                    var sem = GuessSemester(c.Fullname, c.Shortname);
                    var folder = CourseFolderName(c.Fullname);
                    if (r.LayoutManual == 0 && (sem != r.Semester || folder != r.Folder)) lib.RelocateCourse(r.Id, sem, folder);
                    idOf[c.Id] = r.Id;
                }
                else
                {
                    idOf[c.Id] = db.Insert("INSERT INTO courses(remote_id,name,shortname,semester,folder) VALUES(@rid,@n,@s,@sem,@f)",
                        new { rid = c.Id, n = c.Fullname, s = c.Shortname, sem = GuessSemester(c.Fullname, c.Shortname), f = CourseFolderName(c.Fullname) });
                }
            }

            foreach (var c in remoteCourses)
            {
                var courseId = idOf[c.Id];
                var seenRes = new HashSet<long>();
                List<CourseSection> sections;
                try { sections = await WithRetry(() => api.ContentsAsync(c.Id), 3, retryMs); }
                catch (Exception e) when (e is not MoodleException { IsAuthError: true }) { rep.Errors.Add($"{c.Shortname} : {e.Message}"); continue; }

                foreach (var s in sections)
                foreach (var m in s.Modules)
                {
                    var files = (m.Contents ?? []).Where(f => f.Type == "file").ToList();
                    if (m.Modname is not ("resource" or "folder" or "assign") && files.Count == 0) continue;
                    seenRes.Add(m.Id);
                    var existing = db.One<(long Id, string Category, string CategorySource)?>(
                        "SELECT id, category, category_source FROM resources WHERE course_id=@c AND remote_id=@m", new { c = courseId, m = m.Id });
                    var verdict = Classifier.Classify(new(m.Modname, m.Name, files.FirstOrDefault()?.Filename, s.Name, Text(m.Description)));
                    var category = existing is { CategorySource: "manual" } ex0 ? ex0.Category : verdict.Category;
                    long resId;
                    if (existing is { } ex)
                    {
                        db.Execute("UPDATE resources SET name=@n, modname=@mod, section=@sec, description=@d, category=@cat, confidence=@conf, remote_deleted=0 WHERE id=@id",
                            new { n = m.Name, mod = m.Modname, sec = s.Name, d = Text(m.Description), cat = category, conf = verdict.Confidence, id = ex.Id });
                        resId = ex.Id;
                    }
                    else
                    {
                        resId = db.Insert("INSERT INTO resources(course_id,remote_id,name,modname,section,description,category,confidence,published_at) VALUES(@c,@rid,@n,@mod,@sec,@d,@cat,@conf,@pub)",
                            new { c = courseId, rid = m.Id, n = m.Name, mod = m.Modname, sec = s.Name, d = Text(m.Description), cat = category, conf = verdict.Confidence, pub = m.Added });
                    }
                    foreach (var f in files)
                    {
                        try
                        {
                            var r = await WithRetry(() => lib.ImportFileAsync(new(courseId, resId, category, f.Filename, StripQuery(f.Fileurl),
                                f.Timemodified, f.Filesize, () => api.DownloadAsync(f.Fileurl))), 3, retryMs);
                            switch (r.Status)
                            {
                                case ImportStatus.Added: rep.Added++; break;
                                case ImportStatus.Updated: rep.Updated++; break;
                                case ImportStatus.Unchanged: rep.Unchanged++; break;
                                case ImportStatus.Duplicate: rep.Duplicates++; break;
                                default: rep.Conflicts.Add(r.Path); break;
                            }
                        }
                        catch (MoodleException e) when (e.IsAuthError) { throw; }
                        catch (Exception e) { rep.Errors.Add($"{f.Filename} : {e.Message}"); }
                    }
                }

                // Ressources disparues d'EPREL : signalées, JAMAIS supprimées localement.
                var gone = db.All<(long Id, long? RemoteId)>("SELECT id, remote_id FROM resources WHERE course_id=@c AND remote_deleted=0", new { c = courseId });
                foreach (var (id, rid) in gone)
                    if (rid != null && !seenRes.Contains(rid.Value))
                    {
                        db.Execute("UPDATE resources SET remote_deleted=1 WHERE id=@id", new { id });
                        db.Execute("UPDATE files SET remote_deleted=1 WHERE resource_id=@id", new { id });
                        rep.RemoteGone++;
                    }
            }

            foreach (var c in remoteCourses)
                db.Execute("UPDATE courses SET startdate=@s WHERE id=@id", new { s = c.Startdate is > 0 ? c.Startdate : null, id = idOf[c.Id] });

            List<Assignment> assigns;
            try { assigns = await WithRetry(() => api.AssignmentsAsync(remoteCourses.Select(c => c.Id).ToList()), 3, retryMs); }
            catch (Exception e) when (e is not MoodleException { IsAuthError: true }) { rep.Errors.Add($"devoirs : {e.Message}"); assigns = []; }

            // Date limite RÉELLEMENT applicable : celle de la chronologie EPREL (prolongations, dérogations).
            List<ActionEvent> events;
            try { events = await WithRetry(() => api.ActionEventsAsync(Now() - 2 * 365 * 86400), 3, retryMs); }
            catch (Exception e) when (e is not MoodleException { IsAuthError: true }) { rep.Errors.Add($"chronologie : {e.Message}"); events = []; }
            long? EffectiveDue(Assignment a) => events.FirstOrDefault(e => e.Modulename == "assign" &&
                (CmidOf(e.Url) == a.Cmid || e.Instance == a.Cmid || e.Instance == a.Id))?.Timesort;

            foreach (var a in assigns)
            {
                if (!idOf.TryGetValue(a.Course, out var courseId)) continue;
                long? baseDue = a.Duedate > 0 ? a.Duedate : null; // 0 = pas de date limite : on n'en invente pas
                var due = EffectiveDue(a) ?? baseDue;
                long? submitted = null;
                try
                {
                    var st = await WithRetry(() => api.SubmissionStatusAsync(a.Id), 3, retryMs);
                    if (st != null) submitted = st == "submitted" ? 1 : 0;
                }
                catch (Exception e) when (e is not MoodleException { IsAuthError: true }) { }
                var p = new { n = a.Name, d = Text(a.Intro), due, baseDue, cmid = a.Cmid, pub = a.Allowsubmissionsfromdate > 0 ? a.Allowsubmissionsfromdate : (long?)null,
                    mod = a.Timemodified, c = courseId, submitted, rid = a.Id };
                var exId = db.One<long?>("SELECT id FROM assignments WHERE remote_id=@rid", new { rid = a.Id });
                long id;
                if (exId is { } e0)
                {
                    db.Execute("""
                        UPDATE assignments SET name=@n, description=@d, due_at=@due, base_due_at=@baseDue, cmid=@cmid, published_at=@pub, remote_modified=@mod,
                          course_id=@c, remote_deleted=0, submitted=COALESCE(@submitted, submitted) WHERE remote_id=@rid
                        """, p);
                    id = e0;
                }
                else
                {
                    id = db.Insert("""
                        INSERT INTO assignments(remote_id,course_id,name,description,due_at,base_due_at,cmid,published_at,remote_modified,submitted)
                        VALUES(@rid,@c,@n,@d,@due,@baseDue,@cmid,@pub,@mod,COALESCE(@submitted,0))
                        """, p);
                }
                // Rendu sur EPREL → « Terminé », sauf statut choisi à la main (jamais écrasé).
                if (submitted == 1) db.Execute("UPDATE assignments SET status='done' WHERE id=@id AND status_source='auto'", new { id });
                rep.Assignments++;
            }
        }
        catch (Exception e)
        {
            if (e is MoodleException { IsAuthError: true }) rep.AuthExpired = true;
            rep.Errors.Add(e.Message);
        }
        var summary = JsonSerializer.Serialize(rep, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        db.Execute("UPDATE sync_log SET finished_at=@t, ok=@ok, summary=@s WHERE id=@id",
            new { t = Now(), ok = rep.Errors.Count == 0 && !rep.AuthExpired ? 1 : 0, s = summary, id = logId });
        return rep;
    }
}
