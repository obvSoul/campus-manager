using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CampusManager.App.Services;
using CampusManager.App.ViewModels;
using CampusManager.Core.Queries;

namespace CampusManager.App.Views;

/// <summary>Ligne affichée : le fichier + les textes déjà mis en forme.</summary>
public record FileRowVm(FileItem F, bool ShowCourse, string Mode)
{
    public long Id => F.Id;
    public string Name => F.Name;
    public string Ext => F.Ext;
    public string Category => F.Category;
    public string Course => ShowCourse ? F.Course : "";
    public bool Favorite => F.Favorite;
    public bool Missing => F.Missing;
    public bool RemoteDeleted => F.RemoteDeleted;
    public double Opacity => F.Missing ? 0.55 : 1;
    public string DateText => Fmt.Date(F.AddedAt);
    public string SizeText => Fmt.Size(F.Size);
    public GridLength CourseWidth => ShowCourse ? new GridLength(0.6, GridUnitType.Star) : new GridLength(0);
    public double CourseMin => ShowCourse ? 120 : 0;
    public double TileWidth => Mode == "large" ? 176 : 136;
    public double TileIcon => Mode == "large" ? 64 : 44;
    public string TileSub => F.Category + (ShowCourse ? $" · {F.Course}" : "");
}

public partial class FileBrowser : UserControl
{
    readonly MainWindow _w;
    List<FileItem> _files = [];
    bool _showCourse;
    string _mode = "list";
    string _sortKey = "name";
    bool _asc = true;
    Point? _dragStart;
    bool _loading;

    static AppServices S => App.Services;

    public FileBrowser(MainWindow w)
    {
        _w = w;
        InitializeComponent();
        BuildHeaders();
    }

    public void Show(List<FileItem> files, string mode, bool showCourse, string empty)
    {
        _files = files; _mode = mode; _showCourse = showCourse;
        EmptyText.Text = empty;
        Render();
    }

    public void SetMode(string mode) { _mode = mode; Render(); }

    void Render()
    {
        _loading = true;
        var list = _mode == "list";
        Head.Visibility = list && _files.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        SortBar.Visibility = !list && _files.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        CourseCol.Width = _showCourse ? new GridLength(0.6, GridUnitType.Star) : new GridLength(0);
        CourseCol.MinWidth = _showCourse ? 120 : 0;
        BuildHeaders();
        SortBox.SelectedItem = SortBox.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == _sortKey);
        SortDir.Content = _asc ? "↑" : "↓";

        List.ItemTemplate = (DataTemplate)FindResource(_mode switch { "list" => "ListTpl", "compact" => "CompactTpl", _ => "GridTpl" });
        List.ItemsPanel = (ItemsPanelTemplate)FindResource(_mode is "grid" or "large" ? "WrapPanelTpl" : "StackPanelTpl");
        var rows = Sorted().Select(f => new FileRowVm(f, _showCourse, _mode)).ToList();
        List.ItemsSource = rows;
        List.SelectedItem = rows.FirstOrDefault(r => r.Id == _w.Vm.Selected);
        Empty.Visibility = _files.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        List.Visibility = _files.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        _loading = false;
    }

    IEnumerable<FileItem> Sorted()
    {
        Func<FileItem, IComparable> key = _sortKey switch
        {
            "addedAt" => f => f.AddedAt,
            "ext" => f => f.Ext,
            "course" => f => f.Course.ToLowerInvariant(),
            "size" => f => f.Size,
            "category" => f => f.Category,
            _ => f => f.Name.ToLowerInvariant(),
        };
        return _asc ? _files.OrderBy(key) : _files.OrderByDescending(key);
    }

    void BuildHeaders()
    {
        HeadGrid.Children.Clear();
        void H(int col, string key, string label, HorizontalAlignment align = HorizontalAlignment.Left)
        {
            var b = new Button { Style = (Style)FindResource("Plain"), HorizontalAlignment = align, Margin = new Thickness(col == 1 ? 12 : 0, 0, 0, 0) };
            b.Content = W.Text(label + (_sortKey == key ? (_asc ? " ↑" : " ↓") : ""), "Small");
            b.Click += (_, _) => { _asc = _sortKey != key || !_asc; _sortKey = key; Render(); };
            Grid.SetColumn(b, col);
            HeadGrid.Children.Add(b);
        }
        H(1, "name", "Nom"); H(2, "category", "Type");
        if (_showCourse) H(3, "course", "Matière");
        H(4, "addedAt", "Ajouté"); H(5, "size", "Taille", HorizontalAlignment.Right);
    }

    void SortBox_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || SortBox.SelectedItem is not ComboBoxItem i) return;
        _sortKey = (string)i.Tag; _asc = true; Render();
    }
    void SortDir_Click(object sender, RoutedEventArgs e) { _asc = !_asc; Render(); }

    FileRowVm? Current => List.SelectedItem as FileRowVm;

    void List_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading) _w.Vm.Selected = Current?.Id;
    }

    void OpenFile(FileRowVm r)
    {
        var err = Shell.Open(S.Lib.Abs(r.F.Path));
        if (err != null) _w.ShowToast($"Impossible d'ouvrir : {err}");
    }

    void List_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if ((e.OriginalSource as DependencyObject)?.FindAncestor<ListBoxItem>() != null && Current is { } r) OpenFile(r);
    }

    void List_KeyDown(object sender, KeyEventArgs e)
    {
        if (Current is not { } r) return;
        if (e.Key == Key.Enter) OpenFile(r);
        else if (e.Key == Key.F2) Rename(r);
        else if (e.Key == Key.Delete) Trash(r);
    }

    void Act(Action fn, string ok)
    {
        try { fn(); _w.ShowToast(ok); S.NotifyChanged(); }
        catch (Exception ex) { _w.ShowToast($"Erreur : {ex.Message}"); }
    }

    void Rename(FileRowVm r)
    {
        if (PromptDialog.Ask(Window.GetWindow(this), "Renommer", "Nouveau nom (le fichier est renommé sur le disque)", r.Name) is { } v)
            Act(() => S.Lib.RenameFile(r.Id, v), "Fichier renommé");
    }

    void Trash(FileRowVm r)
    {
        if (!ConfirmDialog.Ask(Window.GetWindow(this), $"Mettre « {r.F.Name} » à la corbeille Windows ?",
                "Tu pourras le restaurer depuis la Corbeille. La synchronisation ne le retéléchargera pas.", "Mettre à la corbeille")) return;
        Act(() =>
        {
            var abs = S.Lib.Abs(r.F.Path);
            if (File.Exists(abs)) Shell.Trash(abs);
            S.Lib.MarkDeleted(r.Id);
            _w.Vm.Selected = null;
        }, "Fichier mis à la corbeille");
    }

    void List_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var item = (e.OriginalSource as DependencyObject)?.FindAncestor<ListBoxItem>();
        if (item?.DataContext is not FileRowVm r) { e.Handled = true; return; }
        List.SelectedItem = r;
        var m = List.ContextMenu!;
        m.Items.Clear();
        void Add(string label, Action a, bool danger = false)
        {
            var mi = new MenuItem { Header = label };
            if (danger) mi.SetResourceReference(ForegroundProperty, "Bad");
            mi.Click += (_, _) => a();
            m.Items.Add(mi);
        }
        var f = r.F;
        Add("Ouvrir", () => OpenFile(r));
        Add("Ouvrir l'emplacement", () => Shell.Reveal(S.Lib.Abs(f.Path)));
        m.Items.Add(new Separator());
        Add("Renommer", () => Rename(r));
        Add("Changer de catégorie", () =>
        {
            if (PromptDialog.Choose(Window.GetWindow(this), "Changer de catégorie",
                    "Le fichier sera déplacé dans ce dossier ; ce choix est retenu pour les prochaines synchronisations.",
                    Fmt.Categories.Select(c => (c, c)).ToList(), f.Category) is { } v)
                Act(() => S.Lib.MoveFile(f.Id, category: v), $"Déplacé dans {v}");
        });
        Add("Changer de matière", () =>
        {
            var courses = _w.Vm.Tree.SelectMany(s => s.Courses.Select(c => (c.Id.ToString(), $"{s.Name} · {c.Folder}"))).ToList();
            if (PromptDialog.Choose(Window.GetWindow(this), "Changer de matière", "Matière de destination", courses, f.CourseId?.ToString()) is { } v)
                Act(() => S.Lib.MoveFile(f.Id, courseId: long.Parse(v)), "Fichier déplacé");
        });
        Add("Copier le chemin", () => Act(() => Clipboard.SetText(S.Lib.Abs(f.Path)), "Chemin copié"));
        Add(f.Favorite ? "Retirer des favoris" : "Ajouter aux favoris",
            () => Act(() => S.Lib.SetFavorite(f.Id, !f.Favorite), f.Favorite ? "Retiré des favoris" : "Ajouté aux favoris"));
        m.Items.Add(new Separator());
        Add("Propriétés", () => _w.Vm.Selected = f.Id);
        Add("Mettre à la corbeille…", () => Trash(r), danger: true);
    }

    // Glisser un fichier HORS de l'appli (Explorateur, Bureau, pièce jointe…) ou sur une matière.
    void List_PreviewDown(object sender, MouseButtonEventArgs e) =>
        _dragStart = (e.OriginalSource as DependencyObject)?.FindAncestor<ListBoxItem>() != null ? e.GetPosition(List) : null;

    void List_PreviewMove(object sender, MouseEventArgs e)
    {
        if (_dragStart is not { } start || e.LeftButton != MouseButtonState.Pressed) return;
        var p = e.GetPosition(List);
        if (Math.Abs(p.X - start.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(p.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        _dragStart = null;
        if (Current is not { Missing: false } r) return;
        var abs = S.Lib.Abs(r.F.Path);
        if (!File.Exists(abs)) return;
        var data = new DataObject(DataFormats.FileDrop, new[] { abs });
        // Copie seulement : l'Explorateur ne doit jamais retirer le fichier de la bibliothèque.
        DragDrop.DoDragDrop(List, data, DragDropEffects.Copy);
    }
}

public static class TreeExt
{
    public static T? FindAncestor<T>(this DependencyObject? d) where T : DependencyObject
    {
        while (d != null && d is not T) d = d is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D ? System.Windows.Media.VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        return d as T;
    }
}
