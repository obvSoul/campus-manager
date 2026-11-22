using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CampusManager.App.Controls;
using IconCtl = CampusManager.App.Controls.Icon;
using CampusManager.App.Services;
using CampusManager.App.ViewModels;
using CampusManager.Core.Library;
using CampusManager.Core.Queries;

namespace CampusManager.App.Views;

public partial class MainWindow : Window
{
    public MainViewModel Vm { get; } = new();
    static AppServices S => App.Services;

    readonly DispatcherTimer _searchTimer = new() { Interval = TimeSpan.FromMilliseconds(220) };
    readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromMilliseconds(3200) };
    readonly HashSet<string> _open = []; // nœuds de l'arborescence dépliés/repliés à la main
    readonly HashSet<string> _closed = [];
    FrameworkElement? _page;
    bool _suppressSearch;

    static readonly (string Page, string Label, string Icon)[] NavItems =
    [
        ("home", "Accueil", "home"), ("all", "Tous les fichiers", "files"), ("assignments", "Devoirs", "check"),
        ("work", "TD / TP", "flask"), ("recent", "Récemment ajoutés", "clock"), ("favorites", "Favoris", "star"),
    ];

    public MainWindow(string page)
    {
        InitializeComponent();
        App.Trace("InitializeComponent");
        Vm.Theme = S.Setting("uiTheme") ?? "dark";
        Vm.Mode = S.Setting("uiMode") ?? "list";
        Vm.Sync = S.State;
        ApplyTheme(Vm.Theme);
        Vm.View = View.Parse(page);
        Vm.PropertyChanged += OnVm;
        Details.Closed += () => Vm.Selected = null;

        S.StateChanged += OnSyncState;
        S.DataChanged += OnDataChanged;
        Closed += (_, _) => { S.StateChanged -= OnSyncState; S.DataChanged -= OnDataChanged; };
        SourceInitialized += (_, _) => Dwm.DarkTitleBar(this, Vm.Theme == "dark");

        _searchTimer.Tick += (_, _) =>
        {
            _searchTimer.Stop();
            var q = Search.Text.Trim();
            if (q.Length > 0) Vm.Go(new View("search", Query: q));
        };
        _toastTimer.Tick += (_, _) => { _toastTimer.Stop(); Toast.Visibility = Visibility.Collapsed; };

        BuildNav();
        App.Trace("avant Reload");
        Reload();
        App.Trace("Reload");
    }

    void OnSyncState(SyncState s) => Dispatcher.BeginInvoke(() => Vm.Sync = s);
    void OnDataChanged() => Dispatcher.BeginInvoke(Vm.Refresh);

    void OnVm(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(Vm.View): ShowView(); break;
            case nameof(Vm.Version): Reload(); break;
            case nameof(Vm.Selected): ShowDetails(); break;
            case nameof(Vm.Mode): S.SetSetting("uiMode", Vm.Mode); UpdateModes(); (_page as FileBrowser)?.SetMode(Vm.Mode); break;
            case nameof(Vm.Theme): S.SetSetting("uiTheme", Vm.Theme); ApplyTheme(Vm.Theme); break;
            case nameof(Vm.Sync): UpdateSync(); break;
        }
    }

    public void ShowToast(string msg)
    {
        ToastText.Text = msg;
        Toast.Visibility = Visibility.Visible;
        _toastTimer.Stop(); _toastTimer.Start();
    }

    void ApplyTheme(string theme)
    {
        var dicts = Application.Current.Resources.MergedDictionaries;
        dicts[0] = new ResourceDictionary { Source = new Uri($"Theme/Onyx.{(theme == "light" ? "Light" : "Dark")}.xaml", UriKind.Relative) };
        ThemeIcon.Kind = theme == "dark" ? "sun" : "moon";
        if (IsLoaded) Dwm.DarkTitleBar(this, theme == "dark");
        App.Widget?.ApplyAppTheme();
    }

    /// <summary>Recharge l'arborescence et la vue courante (après une action ou une synchro).</summary>
    void Reload()
    {
        Vm.Tree = Queries.Tree(S.Db);
        BuildTree();
        ShowView(keepPage: true);
        if (Vm.Selected != null) ShowDetails();
    }

    // ---------- Barre latérale ----------

    static StackPanel IconLabel(string icon, string label, Brush? iconBrush = null)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal };
        var i = new IconCtl { Kind = icon };
        if (iconBrush != null) i.Foreground = iconBrush;
        else i.SetResourceReference(IconCtl.ForegroundProperty, "Muted");
        p.Children.Add(i);
        p.Children.Add(new TextBlock { Text = label, Margin = new Thickness(10, 0, 0, 0) });
        return p;
    }

    void BuildNav()
    {
        foreach (var (page, label, icon) in NavItems)
        {
            var b = new Button { Style = (Style)FindResource("Nav"), Content = IconLabel(icon, label), Tag = page };
            b.Click += (_, _) => Vm.Go(page);
            Nav.Children.Add(b);
        }
    }

    bool IsOpen(string key) => key.StartsWith("s:") ? !_closed.Contains(key) : _open.Contains(key);
    void Toggle(string key)
    {
        if (IsOpen(key)) { _open.Remove(key); _closed.Add(key); } else { _closed.Remove(key); _open.Add(key); }
        BuildTree();
    }

    void BuildTree()
    {
        TreePanel.Children.Clear();
        foreach (var s in Vm.Tree)
        {
            var sk = $"s:{s.Name}";
            var head = new Button { Style = (Style)FindResource("Row"), Padding = new Thickness(6) };
            var hp = new StackPanel { Orientation = Orientation.Horizontal };
            var chev = new IconCtl { Kind = "chevron", Size = 14, RenderTransformOrigin = new Point(.5, .5), RenderTransform = new RotateTransform(IsOpen(sk) ? 90 : 0) };
            chev.SetResourceReference(IconCtl.ForegroundProperty, "Muted");
            hp.Children.Add(chev);
            hp.Children.Add(new TextBlock { Text = s.Name, FontWeight = FontWeights.SemiBold, FontSize = 12.5, Margin = new Thickness(6, 0, 0, 0), Style = (Style)FindResource("TMuted") });
            head.Content = hp;
            head.Click += (_, _) => Toggle(sk);
            TreePanel.Children.Add(head);
            if (!IsOpen(sk)) continue;
            foreach (var c in s.Courses)
            {
                var ck = $"c:{c.Id}";
                var active = Vm.View.Kind == "course" && Vm.View.CourseId == c.Id;
                var row = new Button { Style = (Style)FindResource("Row"), Padding = new Thickness(18, 5, 8, 5), ToolTip = c.Name };
                Ui.SetActive(row, active && Vm.View.Category == null);
                var rp = new DockPanel();
                var fi = new IconCtl { Kind = "folder", Size = 15, Margin = new Thickness(0, 0, 6, 0) };
                fi.SetResourceReference(IconCtl.ForegroundProperty, "Accent");
                rp.Children.Add(fi);
                rp.Children.Add(new TextBlock { Text = c.Folder, TextTrimming = TextTrimming.CharacterEllipsis });
                row.Content = rp;
                row.Click += (_, _) => { _open.Add(ck); Vm.Go(new View("course", c.Id)); };
                row.MouseDoubleClick += (_, e) => { Toggle(ck); e.Handled = true; };
                MakeDropTarget(row, c.Id, null);
                TreePanel.Children.Add(row);
                if (!IsOpen(ck)) continue;
                foreach (var cat in c.Categories)
                {
                    var cr = new Button { Style = (Style)FindResource("Row"), Padding = new Thickness(42, 4, 8, 4) };
                    Ui.SetActive(cr, active && Vm.View.Category == cat.Name);
                    var dp = new DockPanel();
                    var cnt = new TextBlock { Text = cat.Count.ToString(), FontSize = 11, Style = (Style)FindResource("TMuted") };
                    DockPanel.SetDock(cnt, Dock.Right);
                    dp.Children.Add(cnt);
                    dp.Children.Add(new TextBlock { Text = cat.Name, FontSize = 12.5, TextTrimming = TextTrimming.CharacterEllipsis, Style = (Style)FindResource("TMuted") });
                    cr.Content = dp;
                    var name = cat.Name;
                    cr.Click += (_, _) => Vm.Go(new View("course", c.Id, name));
                    MakeDropTarget(cr, c.Id, name);
                    TreePanel.Children.Add(cr);
                }
            }
        }
    }

    void Settings_Click(object sender, RoutedEventArgs e) => Vm.Go("settings");

    // ---------- Barre du haut ----------

    void UpdateCrumbs()
    {
        Crumbs.Children.Clear();
        var v = Vm.View;
        List<string> parts;
        if (v.Kind == "course")
        {
            var c = Vm.FindCourse(v.CourseId);
            parts = [c?.Semester ?? "", c?.Course.Folder ?? ""];
            if (v.Category != null) parts.Add(v.Category);
        }
        else parts = [v.Kind switch
        {
            "home" => "Accueil", "all" => "Tous les fichiers", "recent" => "Récemment ajoutés", "favorites" => "Favoris",
            "assignments" => "Devoirs", "work" => "TD / TP", "settings" => "Paramètres", _ => $"Recherche « {v.Query} »",
        }];
        for (var i = 0; i < parts.Count; i++)
        {
            if (i > 0)
            {
                var ch = new IconCtl { Kind = "chevron", Size = 12, Margin = new Thickness(4, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };
                ch.SetResourceReference(IconCtl.ForegroundProperty, "Muted");
                Crumbs.Children.Add(ch);
            }
            var last = i == parts.Count - 1;
            var t = new TextBlock { Text = parts[i], FontSize = 14, FontWeight = last ? FontWeights.SemiBold : FontWeights.Normal, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            if (!last) t.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            if (v.Kind == "course" && i == 1) { t.Cursor = Cursors.Hand; t.MouseLeftButtonUp += (_, _) => Vm.Go(new View("course", v.CourseId)); }
            Crumbs.Children.Add(t);
        }
        if (v.IsFiles)
        {
            var badge = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 0, 8, 0), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            badge.SetResourceReference(Border.BackgroundProperty, "Hover");
            badge.Child = new TextBlock { Text = Vm.Files.Count.ToString(), FontSize = 12, Style = (Style)FindResource("TMuted") };
            Crumbs.Children.Add(badge);
        }
    }

    void UpdateModes()
    {
        Modes.Visibility = Vm.View.IsFiles ? Visibility.Visible : Visibility.Collapsed;
        foreach (var b in ((StackPanel)Modes.Child).Children.OfType<Button>()) Ui.SetActive(b, (string)b.Tag == Vm.Mode);
    }

    void UpdateSync()
    {
        SyncIcon.Spin = Vm.Sync.Running;
        SyncBtn.ToolTip = Vm.Sync.Running ? Vm.Sync.Message : "Synchroniser avec EPREL";
        (_page as HomePage)?.UpdateSync(Vm.Sync);
        (_page as SettingsPage)?.UpdateSync(Vm.Sync);
    }

    public async void RunSync(bool relogin = false)
    {
        var s = await S.SyncAsync(relogin);
        Vm.Sync = s;
        if (s.Message.Length > 0) ShowToast(s.Message);
    }

    void Sync_Click(object sender, RoutedEventArgs e) => RunSync();
    void Theme_Click(object sender, RoutedEventArgs e) => Vm.Theme = Vm.Theme == "dark" ? "light" : "dark";
    void Mode_Click(object sender, RoutedEventArgs e) => Vm.Mode = (string)((Button)sender).Tag;

    void Search_TextChanged(object sender, TextChangedEventArgs e)
    {
        SearchHint.Visibility = Search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_suppressSearch) return;
        _searchTimer.Stop();
        if (Search.Text.Trim().Length == 0) { if (Vm.View.Kind == "search") Vm.Go("all"); return; }
        _searchTimer.Start();
    }
    void Search_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) Search.Text = ""; }

    // ---------- Pages ----------

    void ShowView(bool keepPage = false)
    {
        var v = Vm.View;
        foreach (var b in Nav.Children.OfType<Button>()) Ui.SetActive(b, (string)b.Tag == v.Kind);
        Ui.SetActive(SettingsNav, v.Kind == "settings");
        if (v.Kind != "search" && Search.Text.Length > 0) { _suppressSearch = true; Search.Text = ""; _suppressSearch = false; }
        if (!keepPage) BuildTree();

        if (v.FileQuery is { } q) Vm.Files = Queries.ListFiles(S.Db, q);
        FrameworkElement page = v.Kind switch
        {
            "home" => keepPage && _page is HomePage h ? h : new HomePage(this),
            "assignments" => keepPage && _page is AssignmentsPage a ? a : new AssignmentsPage(this),
            "settings" => keepPage && _page is SettingsPage st ? st : new SettingsPage(this),
            _ => _page is FileBrowser fb ? fb : new FileBrowser(this),
        };
        if (page is FileBrowser browser)
            browser.Show(Vm.Files, Vm.Mode, showCourse: v.Kind != "course",
                empty: v.Kind == "favorites" ? "Aucun favori : clic droit sur un fichier > Ajouter aux favoris." : v.Kind == "search" ? "Aucun résultat." : "Aucun fichier ici.");
        else if (keepPage && page == _page) (page as IReloadable)?.Reload();
        if (page != _page) { _page = page; PageHost.Content = page; }
        if (!v.IsFiles) Vm.Selected = null;
        UpdateCrumbs();
        UpdateModes();
        UpdateSync();
    }

    void ShowDetails()
    {
        if (Vm.Selected is not { } id || !Vm.View.IsFiles) { Details.Visibility = Visibility.Collapsed; Details.Clear(); return; }
        Details.Visibility = Visibility.Visible;
        Details.Show(id);
    }

    // ---------- Glisser-déposer ----------

    static string[]? DroppedPaths(DragEventArgs e) => e.Data.GetDataPresent(DataFormats.FileDrop) ? e.Data.GetData(DataFormats.FileDrop) as string[] : null;

    public void HandleDrop(string[] paths, long courseId, string? category)
    {
        try
        {
            var r = S.Lib.Drop(paths, courseId, category);
            S.Log($"dépôt : {r.Imported} importés, {r.Moved} déplacés, {r.Duplicates} doublons, {r.Skipped.Count} ignorés");
            ShowToast(DescribeDrop(r));
            S.NotifyChanged();
        }
        catch (Exception ex) { ShowToast($"Dépôt impossible : {ex.Message}"); }
    }

    static string DescribeDrop(DropResult r)
    {
        var parts = new List<string>();
        if (r.Imported > 0) parts.Add($"{r.Imported} fichier(s) ajouté(s)");
        if (r.Moved > 0) parts.Add($"{r.Moved} déplacé(s)");
        if (r.Duplicates > 0) parts.Add($"{r.Duplicates} déjà présent(s)");
        if (r.Skipped.Count > 0) parts.Add($"ignorés : {string.Join(", ", r.Skipped)}");
        return parts.Count > 0 ? string.Join(" · ", parts) : "Rien à faire : déjà au bon endroit";
    }

    void MakeDropTarget(Button b, long courseId, string? category)
    {
        b.AllowDrop = true;
        b.DragEnter += (_, e) => { if (DroppedPaths(e) != null) { Ui.SetDropOver(b, true); e.Effects = DragDropEffects.Copy; e.Handled = true; } };
        b.DragOver += (_, e) => { e.Effects = DroppedPaths(e) != null ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
        b.DragLeave += (_, _) => Ui.SetDropOver(b, false);
        b.Drop += (_, e) =>
        {
            Ui.SetDropOver(b, false);
            if (DroppedPaths(e) is { Length: > 0 } p) HandleDrop(p, courseId, category);
            e.Handled = true;
        };
    }

    void Page_DragEnter(object sender, DragEventArgs e)
    {
        if (Vm.View.Kind != "course" || DroppedPaths(e) == null) { e.Effects = DragDropEffects.None; e.Handled = true; return; }
        var c = Vm.FindCourse(Vm.View.CourseId);
        DropText.Text = $"Déposer dans {c?.Course.Folder}" + (Vm.View.Category != null ? $" / {Vm.View.Category}" : " (dossier « Personnel »)");
        DropOutline.Visibility = Visibility.Visible;
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
    }
    void Page_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = Vm.View.Kind == "course" && DroppedPaths(e) != null ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }
    void Page_DragLeave(object sender, DragEventArgs e)
    {
        // DragLeave arrive aussi en passant sur un enfant : on ne masque que si la souris sort vraiment.
        var p = e.GetPosition(PageArea);
        if (p.X <= 0 || p.Y <= 0 || p.X >= PageArea.ActualWidth || p.Y >= PageArea.ActualHeight) DropOutline.Visibility = Visibility.Collapsed;
    }
    void Page_Drop(object sender, DragEventArgs e)
    {
        DropOutline.Visibility = Visibility.Collapsed;
        if (Vm.View.Kind == "course" && DroppedPaths(e) is { Length: > 0 } p) HandleDrop(p, Vm.View.CourseId, Vm.View.Category);
        e.Handled = true;
    }
}

/// <summary>Page qui sait se recharger quand les données changent.</summary>
public interface IReloadable { void Reload(); }
