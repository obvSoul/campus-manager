using System.Text.Json;
using System.Text.Json.Serialization;

namespace CampusManager.Core.Queries;

public record CategoryCount(string Name, long Count);
public record CourseNode(long Id, string Name, string Folder, string Semester, List<CategoryCount> Categories);
public record SemesterNode(string Name, List<CourseNode> Courses);

public record FileItem(long Id, string Name, string Ext, string Category, long? CourseId, string Course, string Semester,
    long Size, long AddedAt, bool Favorite, bool Missing, bool RemoteDeleted, string Path);

public record FileDetails(FileItem Item, long? ModifiedAt, bool LocallyModified, string? ResourceName, string? Section,
    string? Description, bool FromEprel, string AbsPath);

public record FileQuery
{
    public long? CourseId { get; init; }
    public string? Category { get; init; }
    public List<string>? Categories { get; init; }
    public string? Search { get; init; }
    public bool Recent { get; init; }
    public bool Favorites { get; init; }
    public int Limit { get; init; } = 2000;
}

public record AssignmentItem(long Id, string Name, string Course, long? CourseId, string? Description, long? DueAt, long? PublishedAt,
    string Status, bool Late, long Priority, string? Notes, bool Submitted, bool Old);

public record DashboardCounts(long Files, long Courses, long Td, long Tp, int AssignmentsOpen, int AssignmentsDone, int AssignmentsLate, long Unsorted);
public record CourseProgress(string Course, int Done, int Total);
public record LastSync(long At, bool Ok, int Added, int Errors);
public record Dashboard(List<AssignmentItem> Upcoming, List<FileItem> RecentFiles, DashboardCounts Counts, List<CourseProgress> PerCourse, LastSync? LastSync);

public record WidgetProgress(int Done, int Open, int Late);
public record WidgetData(List<AssignmentItem> Assignments, List<FileItem> Work, List<FileItem> Recent, WidgetProgress Progress, long? LastSync);

public record SyncState(bool Running, string Message, long? LastAt, bool NeedsLogin);

public class WidgetSections
{
    public bool Assignments { get; set; } = true;
    public bool Work { get; set; } = true;
    public bool Recent { get; set; } = true;
    public bool Progress { get; set; } = true;
}

/// <summary>Réglages du widget, enregistrés en JSON (clé <c>settings.widget</c>, mêmes noms qu'avant).</summary>
public class WidgetSettings
{
    static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };

    public bool Enabled { get; set; } = true;
    public WidgetSections Sections { get; set; } = new();
    /// <summary>0 (entièrement transparent) à 1 (fond du widget).</summary>
    public double Opacity { get; set; }
    public string Theme { get; set; } = "dark";
    /// <summary>Les clics traversent le widget.</summary>
    public bool ClickThrough { get; set; }
    /// <summary>Masqué quand une appli est en plein écran.</summary>
    public bool AutoHideFullscreen { get; set; } = true;
    /// <summary>Effet de flou Windows (acrylique).</summary>
    public bool Blur { get; set; }
    public bool Collapsed { get; set; }

    public static WidgetSettings Default => new();

    public static WidgetSettings FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try { var s = JsonSerializer.Deserialize<WidgetSettings>(json, Json) ?? new(); s.Sections ??= new(); s.Opacity = Math.Clamp(s.Opacity, 0, 1); return s; }
        catch (JsonException) { return new(); }
    }
    public string ToJson() => JsonSerializer.Serialize(this, Json);
    public WidgetSettings Clone() => FromJson(ToJson());
}
