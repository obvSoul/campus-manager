using CommunityToolkit.Mvvm.ComponentModel;
using CampusManager.Core.Queries;

namespace CampusManager.App.ViewModels;

/// <summary>Vue affichée, sous la même forme que les fragments d'URL de la version Electron (« course/12/TD »).</summary>
public record View(string Kind, long CourseId = 0, string? Category = null, string Query = "")
{
    public static View Parse(string? h)
    {
        var p = Uri.UnescapeDataString((h ?? "").TrimStart('#')).Split('/');
        var k = p[0];
        if (k == "course" && p.Length > 1 && long.TryParse(p[1], out var id)) return new("course", id, p.Length > 2 && p[2].Length > 0 ? p[2] : null);
        if (k == "search") return new("search", Query: p.Length > 1 ? string.Join('/', p[1..]) : "");
        return k is "all" or "recent" or "favorites" or "assignments" or "work" or "settings" ? new(k) : new("home");
    }

    public bool IsFiles => Kind is "all" or "recent" or "favorites" or "work" or "course" or "search";

    public FileQuery? FileQuery => Kind switch
    {
        "all" => new(),
        "recent" => new() { Recent = true, Limit = 60 },
        "favorites" => new() { Favorites = true },
        "work" => new() { Categories = ["TD", "TP", "Exercices", "Corrections"] },
        "course" => new() { CourseId = CourseId, Category = Category },
        "search" => Query == "À classer" ? new() { Category = "À classer" } : new() { Search = Query },
        _ => null,
    };
}

public partial class MainViewModel : ObservableObject
{
    [ObservableProperty] private View _view = new("home");
    [ObservableProperty] private List<SemesterNode> _tree = [];
    [ObservableProperty] private List<FileItem> _files = [];
    [ObservableProperty] private long? _selected;
    [ObservableProperty] private string _mode = "list";
    [ObservableProperty] private string _theme = "dark";
    [ObservableProperty] private SyncState _sync = new(false, "", null, false);
    [ObservableProperty] private string? _toast;

    /// <summary>Incrémenté à chaque changement de données : les pages se rechargent.</summary>
    [ObservableProperty] private int _version;

    public void Go(string page) => Go(View.Parse(page));
    public void Go(View v) { Selected = null; View = v; }
    public void Refresh() => Version++;

    public (string Semester, CourseNode Course)? FindCourse(long id)
    {
        foreach (var s in Tree) foreach (var c in s.Courses) if (c.Id == id) return (s.Name, c);
        return null;
    }
}
