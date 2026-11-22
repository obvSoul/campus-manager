using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CampusManager.App.Services;
using CampusManager.Core.Queries;

namespace CampusManager.App.Views;

/// <summary>Devoirs EPREL avec statut PERSONNEL (stocké localement, jamais envoyé à EPREL).</summary>
public sealed class AssignmentsPage : UserControl, IReloadable
{
    readonly StackPanel _root = new() { Margin = new Thickness(0, 0, 6, 8), MaxWidth = 1000, HorizontalAlignment = HorizontalAlignment.Left };
    readonly ComboBox _course = new() { Width = 260 };
    List<AssignmentItem> _list = [];
    long? _open;
    bool _showOld;
    bool _loading;

    public AssignmentsPage(MainWindow _)
    {
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _root };
        _course.SelectionChanged += (_, _) => { if (!_loading) Render(); };
        Reload();
    }

    public void Reload()
    {
        _list = Queries.ListAssignments(App.Services.Db, Fmt.Now());
        _loading = true;
        var sel = (_course.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
        _course.Items.Clear();
        _course.Items.Add(new ComboBoxItem { Content = "Toutes les matières", Tag = "" });
        foreach (var c in _list.Select(a => a.Course).Distinct().OrderBy(c => c, StringComparer.CurrentCulture))
            _course.Items.Add(new ComboBoxItem { Content = c, Tag = c });
        _course.SelectedItem = _course.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == sel) ?? _course.Items[0];
        _loading = false;
        Render();
    }

    static void Set(long id, string status)
    {
        Queries.SetAssignment(App.Services.Db, id, status: status);
        App.Services.NotifyChanged();
    }

    void Render()
    {
        _root.Children.Clear();
        var course = (_course.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
        var shown = _list.Where(a => course == "" || a.Course == course).ToList();
        var todo = shown.Where(a => !a.Old && a.Status != "done").ToList();
        var done = shown.Where(a => !a.Old && a.Status == "done").ToList();
        var old = shown.Where(a => a.Old).ToList();

        var tools = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        if (_course.Parent is Panel p) p.Children.Remove(_course);
        tools.Children.Add(_course);
        var hint = W.Text("Trié par date limite · « En retard » est calculé depuis la vraie date EPREL", "Small");
        hint.Margin = new Thickness(12, 0, 0, 0); hint.VerticalAlignment = VerticalAlignment.Center;
        tools.Children.Add(hint);
        _root.Children.Add(tools);

        _root.Children.Add(Title("À faire", todo.Count.ToString()));
        if (todo.Count == 0) _root.Children.Add(W.Text("Rien à faire.", "TMuted"));
        foreach (var a in todo) _root.Children.Add(Row(a));
        _root.Children.Add(Title("Terminés", done.Count.ToString()));
        foreach (var a in done) _root.Children.Add(Row(a));
        if (old.Count > 0)
        {
            var t = Title("Anciens devoirs", $"{old.Count} · échéance d'une année universitaire précédente · {(_showOld ? "masquer" : "afficher")}");
            t.Cursor = Cursors.Hand;
            t.MouseLeftButtonUp += (_, _) => { _showOld = !_showOld; Render(); };
            _root.Children.Add(t);
            if (_showOld) foreach (var a in old) _root.Children.Add(Row(a));
        }
    }

    static TextBlock Title(string text, string count)
    {
        var t = new TextBlock { Style = (Style)Application.Current.FindResource("SectionTitle") };
        t.Inlines.Add(new System.Windows.Documents.Run(text.ToUpperInvariant() + "  ") { FontWeight = FontWeights.SemiBold });
        t.Inlines.Add(new System.Windows.Documents.Run(count) { FontWeight = FontWeights.Normal });
        return t;
    }

    FrameworkElement Row(AssignmentItem a)
    {
        var u = Fmt.Urgency(a);
        var isDone = a.Status == "done";
        var card = new Border { Padding = new Thickness(12, 10, 12, 10), CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 6), Opacity = isDone ? 0.6 : 1 }
            .Res(Border.BackgroundProperty, "Card").Res(Border.BorderBrushProperty, "Line");
        var outer = new StackPanel();
        var row = new DockPanel();
        card.Child = outer;
        outer.Children.Add(row);

        var check = new CheckBox { IsChecked = isDone, ToolTip = "Marquer comme terminé", Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        check.Click += (_, _) => Set(a.Id, check.IsChecked == true ? "done" : "todo");
        row.Children.Add(check);

        var status = new ComboBox { Width = 120, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        foreach (var s in new[] { "todo", "doing", "done" }) status.Items.Add(new ComboBoxItem { Content = Fmt.StatusLabel(s), Tag = s });
        status.SelectedItem = status.Items.OfType<ComboBoxItem>().First(i => (string)i.Tag == a.Status);
        status.SelectionChanged += (_, _) => { if (status.SelectedItem is ComboBoxItem i && (string)i.Tag != a.Status) Set(a.Id, (string)i.Tag); };
        DockPanel.SetDock(status, Dock.Right);
        row.Children.Add(status);

        var right = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(new TextBlock { Text = isDone ? "Terminé" : Fmt.TimeLeft(a.DueAt), FontSize = 12, HorizontalAlignment = HorizontalAlignment.Right }
            .Res(TextBlock.ForegroundProperty, u is "late" or "24h" ? "Bad" : u == "48h" ? "Warn" : "Muted"));
        right.Children.Add(new TextBlock { Text = a.DueAt != null ? Fmt.Date(a.DueAt, true) : "—", FontSize = 12, HorizontalAlignment = HorizontalAlignment.Right }.Res(TextBlock.ForegroundProperty, "Muted"));
        DockPanel.SetDock(right, Dock.Right);
        row.Children.Add(right);
        if (Fmt.UrgencyLabel(u) is { Length: > 0 } lbl)
        {
            var pill = W.Pill(lbl, W.UrgencyBrush(u)); pill.Margin = new Thickness(12, 0, 0, 0);
            DockPanel.SetDock(pill, Dock.Right); row.Children.Add(pill);
        }
        if (a.Submitted)
        {
            var tag = W.Tag("Rendu sur EPREL", "OkSoft", "Ok"); tag.ToolTip = "Information récupérée depuis EPREL"; tag.Margin = new Thickness(12, 0, 0, 0);
            DockPanel.SetDock(tag, Dock.Right); row.Children.Add(tag);
        }

        var main = new Button { Style = (Style)FindResource("Plain"), HorizontalAlignment = HorizontalAlignment.Stretch };
        var mp = new StackPanel();
        var name = W.Text(a.Name, weight: FontWeights.SemiBold);
        if (isDone) name.TextDecorations = TextDecorations.Strikethrough;
        mp.Children.Add(name);
        mp.Children.Add(W.Text(a.Course, "Small"));
        main.Content = mp;
        main.Click += (_, _) => { _open = _open == a.Id ? null : a.Id; Render(); };
        row.Children.Add(main);

        if (_open == a.Id)
        {
            var desc = a.Description is { Length: > 0 } dd ? new TextBlock { Text = dd } : W.Text("Pas de consigne fournie par EPREL.", "TMuted");
            desc.TextWrapping = TextWrapping.Wrap; desc.FontSize = 12.5; desc.Margin = new Thickness(30, 8, 0, 2);
            outer.Children.Add(desc);
        }
        return card;
    }
}
