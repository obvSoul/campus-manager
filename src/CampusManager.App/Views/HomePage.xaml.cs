using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CampusManager.App.Controls;
using CampusManager.App.Services;
using CampusManager.App.ViewModels;
using CampusManager.Core.Queries;

namespace CampusManager.App.Views;

public partial class HomePage : UserControl, IReloadable
{
    readonly MainWindow _w;

    public HomePage(MainWindow w)
    {
        _w = w;
        InitializeComponent();
        Reload();
    }

    public void Reload()
    {
        var d = Queries.Dashboard(App.Services.Db, Fmt.Now());
        var c = d.Counts;

        Upcoming.Children.Clear();
        if (d.Upcoming.Count == 0) Upcoming.Children.Add(W.Text("Aucun devoir en attente.", "TMuted"));
        var first = true;
        foreach (var a in d.Upcoming)
        {
            var u = Fmt.Urgency(a);
            var row = new DockPanel { Margin = new Thickness(4, 0, 4, 0) };
            var wrap = new Border { BorderThickness = new Thickness(0, first ? 0 : 1, 0, 0), Padding = new Thickness(0, 8, 0, 8), Child = row }.Res(Border.BorderBrushProperty, "Line");
            first = false;
            var dot = W.Dot(u); dot.Margin = new Thickness(0, 0, 12, 0);
            DockPanel.SetDock(dot, Dock.Left); row.Children.Add(dot);
            if (Fmt.UrgencyLabel(u) is { Length: > 0 } lbl)
            {
                var pill = W.Pill(lbl, W.UrgencyBrush(u)); pill.Margin = new Thickness(12, 0, 0, 0);
                DockPanel.SetDock(pill, Dock.Right); row.Children.Add(pill);
            }
            var right = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };
            right.Children.Add(new TextBlock { Text = Fmt.TimeLeft(a.DueAt), FontSize = 12, HorizontalAlignment = HorizontalAlignment.Right }
                .Res(TextBlock.ForegroundProperty, u is "late" or "24h" ? "Bad" : u == "48h" ? "Warn" : "Muted"));
            right.Children.Add(new TextBlock { Text = $"{(a.DueAt != null ? Fmt.Date(a.DueAt, true) : "")} · {Fmt.StatusLabel(a.Status)}", FontSize = 12 }.Res(TextBlock.ForegroundProperty, "Muted"));
            DockPanel.SetDock(right, Dock.Right); row.Children.Add(right);
            var mid = new StackPanel();
            mid.Children.Add(W.Text(a.Name, weight: FontWeights.SemiBold));
            mid.Children.Add(W.Text(a.Course, "Small"));
            row.Children.Add(mid);
            Upcoming.Children.Add(wrap);
        }

        Open.Text = c.AssignmentsOpen.ToString();
        Late.Text = c.AssignmentsLate.ToString();
        Late.SetResourceReference(TextBlock.ForegroundProperty, c.AssignmentsLate > 0 ? "Bad" : "Text");
        Td.Text = c.Td.ToString();
        Tp.Text = c.Tp.ToString();

        Recent.Children.Clear();
        foreach (var f in d.RecentFiles)
        {
            var b = new Button { Style = (Style)FindResource("Row"), Padding = new Thickness(8, 7, 8, 7), Margin = new Thickness(0, 0, 6, 6) };
            Ui.SetRadius(b, new CornerRadius(8));
            var p = new DockPanel();
            var icon = new FileIcon { Ext = f.Ext, Size = 26, Margin = new Thickness(0, 0, 10, 0) };
            p.Children.Add(icon);
            var t = new StackPanel();
            t.Children.Add(W.Text(f.Name));
            t.Children.Add(W.Text($"{f.Category} · {f.Course}", "Small"));
            p.Children.Add(t);
            b.Content = p;
            var file = f;
            b.Click += (_, _) => { if (file.CourseId is { } cid) _w.Vm.Go(new View("course", cid, file.Category)); };
            b.MouseDoubleClick += (_, _) => Shell.Open(App.Services.Lib.Abs(file.Path));
            Recent.Children.Add(b);
        }

        var total = c.AssignmentsDone + c.AssignmentsOpen;
        Done.Text = c.AssignmentsDone.ToString();
        Total.Text = $" / {total} terminés";
        BigBar.Children.Clear();
        BigBar.Children.Add(W.Bar(total > 0 ? (double)c.AssignmentsDone / total : 0));
        PerCourse.Children.Clear();
        foreach (var pc in d.PerCourse)
        {
            var g = new Grid { Margin = new Thickness(0, 0, 0, 7) };
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
            var name = W.Text(pc.Course, size: 12); name.ToolTip = pc.Course; name.Margin = new Thickness(0, 0, 8, 0);
            g.Children.Add(name);
            var bar = W.Bar((double)pc.Done / pc.Total, 5); bar.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(bar, 1);
            g.Children.Add(bar);
            var n = W.Text($"{pc.Done}/{pc.Total}", "Small"); n.Margin = new Thickness(8, 0, 0, 0); Grid.SetColumn(n, 2);
            g.Children.Add(n);
            PerCourse.Children.Add(g);
        }

        LastSync.Text = d.LastSync is { } ls ? $"Dernière : {Fmt.Date(ls.At, true)} {(ls.Ok ? "· réussie" : "· avec erreurs")}" : "Jamais synchronisé.";
        Counts.Text = $"{c.Files} fichiers locaux · {c.Courses} matières" + (c.Unsorted > 0 ? $" · {c.Unsorted} à classer" : "");
        Unsorted.Visibility = c.Unsorted > 0 ? Visibility.Visible : Visibility.Collapsed;
        UnsortedN.Text = c.Unsorted.ToString();
        UpdateSync(_w.Vm.Sync);
    }

    public void UpdateSync(SyncState s)
    {
        SyncMsg.Text = s.Message;
        SyncMsg.Visibility = s.Message.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        SyncNow.IsEnabled = !s.Running;
        SyncNow.Content = s.Running ? "Synchronisation…" : "Synchroniser maintenant";
    }

    void Assignments_Click(object sender, RoutedEventArgs e) { e.Handled = true; _w.Vm.Go("assignments"); }
    void UpcomingCard_Click(object sender, MouseButtonEventArgs e) => _w.Vm.Go("assignments");
    void Work_Click(object sender, RoutedEventArgs e) { e.Handled = true; _w.Vm.Go("work"); }
    void Recent_Click(object sender, MouseButtonEventArgs e) => _w.Vm.Go("recent");
    void Unsorted_Click(object sender, MouseButtonEventArgs e) => _w.Vm.Go(new View("search", Query: "À classer"));
    void SyncNow_Click(object sender, RoutedEventArgs e) { e.Handled = true; _w.RunSync(); }
}
