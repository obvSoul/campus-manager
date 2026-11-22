using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using CampusManager.App.Controls;
using IconCtl = CampusManager.App.Controls.Icon;
using CampusManager.App.Services;
using CampusManager.App.Views;
using CampusManager.Core.Queries;

namespace CampusManager.App.Widget;

/// <summary>
/// Fenêtre du widget : sans cadre, transparente, hors barre des tâches et Alt+Tab, rattachée au bureau.
/// Entièrement transparente par défaut (1 % d'opacité gardé pour recevoir les clics) avec un texte ombré.
/// </summary>
public sealed class WidgetWindow : Window
{
    readonly WidgetController _c;
    readonly AppServices _s;
    WidgetSettings _st;
    readonly Border _frame = new() { CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1) };
    readonly DockPanel _layout = new();
    readonly StackPanel _body = new() { Margin = new Thickness(10, 0, 10, 14) };
    readonly ScrollViewer _scroll;
    readonly TextBlock _sub = new() { FontSize = 11 };
    readonly Border _badge;
    readonly TextBlock _badgeText = new() { Foreground = Brushes.White, FontSize = 11, FontWeight = FontWeights.SemiBold };
    readonly IconCtl _syncIcon = new() { Kind = "sync", Size = 15 };
    readonly IconCtl _chev = new() { Kind = "chevron", Size = 15, RenderTransformOrigin = new Point(.5, .5) };
    readonly Thumb _grip = new() { Width = 14, Height = 14, Cursor = Cursors.SizeNWSE, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 2, 2) };
    readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMinutes(1) };
    readonly DispatcherTimer _reload = new() { Interval = TimeSpan.FromMinutes(10) };
    bool _allowClose;
    IntPtr _hwnd;

    public WidgetWindow(WidgetController c, AppServices s, WidgetSettings st)
    {
        _c = c; _s = s; _st = st.Clone();
        Title = "Campus Manager – widget";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        MinWidth = 260; MinHeight = WidgetBounds.CollapsedHeight;
        FontFamily = (FontFamily)Application.Current.FindResource("UiFont");
        FontSize = 12.5;
        UseLayoutRounding = true;

        _badge = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(7, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center, Child = _badgeText, Margin = new Thickness(0, 0, 6, 0) }
            .Res(Border.BackgroundProperty, "Accent");
        _scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _body, Style = (Style)Application.Current.FindResource("ThinScroll") };

        _layout.Children.Add(Header());
        _layout.Children.Add(_scroll);
        var root = new Grid();
        root.Children.Add(_layout);
        root.Children.Add(_grip);
        _frame.Child = root;
        Content = _frame;

        _grip.Template = GripTemplate();
        _grip.DragDelta += (_, e) => _c.Resize(Width + e.HorizontalChange, Height + e.VerticalChange);

        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            Win32.MakeToolWindow(_hwnd);
            var attached = Win32.AttachToDesktop(_hwnd);
            _s.Log($"widget : {(attached ? "rattaché au bureau" : "intégration bureau indisponible, fenêtre basse")}");
            if (_st.ClickThrough) Win32.SetClickThrough(_hwnd, true);
        };
        // Cliquer sur le widget ne doit pas le faire passer devant les autres fenêtres.
        Activated += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Background, () => Win32.SendToBottom(_hwnd));
        LocationChanged += (_, _) => _c.SaveBounds();
        // Alt+F4 ou toute fermeture accidentelle ne doit pas faire disparaître le widget.
        Closing += (_, e) => { if (!_allowClose) e.Cancel = true; };

        _s.DataChanged += OnData;
        _s.StateChanged += OnSync;
        Closed += (_, _) => { _s.DataChanged -= OnData; _s.StateChanged -= OnSync; _tick.Stop(); _reload.Stop(); };
        _tick.Tick += (_, _) => Reload();   // temps restant à jour chaque minute
        _reload.Tick += (_, _) => Reload();
        _tick.Start(); _reload.Start();

        ApplySettings(_st);
    }

    void OnData() => Dispatcher.BeginInvoke(Reload);
    void OnSync(SyncState _) => Dispatcher.BeginInvoke(Reload);

    public void CloseForReal() { _allowClose = true; Close(); }
    public void SetClickThrough(bool on) { if (_hwnd != IntPtr.Zero) Win32.SetClickThrough(_hwnd, on); }

    public void ApplySettings(WidgetSettings st)
    {
        _st = st.Clone();
        // Thème propre au widget.
        Resources.MergedDictionaries.Clear();
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"pack://application:,,,/Theme/Onyx.{(st.Theme == "light" ? "Light" : "Dark")}.xaml") });
        var clear = st.Opacity < 0.15;
        var bg = (Color)FindResource("C.Bg");
        _frame.Background = new SolidColorBrush(Color.FromArgb((byte)Math.Round(Math.Max(0.01, st.Opacity) * 255), bg.R, bg.G, bg.B));
        _frame.BorderBrush = clear ? Brushes.Transparent : new SolidColorBrush(st.Theme == "light" ? Color.FromArgb(20, 0, 0, 0) : Color.FromArgb(18, 255, 255, 255));
        if (clear)
        {
            // Mode entièrement transparent : texte lisible sur tout fond d'écran grâce à une ombre portée.
            var dark = st.Theme != "light";
            Resources["Muted"] = new SolidColorBrush(dark ? Color.FromRgb(0xd7, 0xdb, 0xe4) : Color.FromRgb(0x2b, 0x31, 0x42));
            _layout.Effect = new DropShadowEffect { Color = dark ? Colors.Black : Colors.White, BlurRadius = 5, ShadowDepth = 1, Direction = 270, Opacity = 0.95 };
        }
        else _layout.Effect = null;
        SetResourceReference(ForegroundProperty, "Text");
        _chev.RenderTransform = new RotateTransform(st.Collapsed ? 90 : -90);
        _grip.Visibility = st.Collapsed ? Visibility.Collapsed : Visibility.Visible;
        Reload();
    }

    static ControlTemplate GripTemplate()
    {
        var f = new FrameworkElementFactory(typeof(System.Windows.Shapes.Path));
        f.SetValue(System.Windows.Shapes.Path.DataProperty, Geometry.Parse("M13 5 5 13M13 9 9 13"));
        f.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "Muted");
        f.SetValue(System.Windows.Shapes.Shape.StrokeThicknessProperty, 1.2);
        f.SetValue(OpacityProperty, 0.7);
        var bg = new FrameworkElementFactory(typeof(Grid));
        bg.SetValue(Panel.BackgroundProperty, Brushes.Transparent);
        bg.AppendChild(f);
        return new ControlTemplate(typeof(Thumb)) { VisualTree = bg };
    }

    Button IconButton(UIElement content, string tip, Action click)
    {
        var b = new Button { Style = (Style)Application.Current.FindResource("IconBtn"), Width = 26, Height = 26, ToolTip = tip, Content = content };
        Ui.SetRadius(b, new CornerRadius(7));
        b.Click += (_, _) => click();
        return b;
    }

    FrameworkElement Header()
    {
        var head = new DockPanel { Height = 44, Margin = new Thickness(12, 0, 8, 0), Background = Brushes.Transparent };
        DockPanel.SetDock(head, Dock.Top);
        head.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) { try { DragMove(); } catch { } } };
        var logo = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(7), Background = new SolidColorBrush(Color.FromRgb(0x58, 0x65, 0xf2)), Margin = new Thickness(0, 0, 8, 0),
            Child = new TextBlock { Text = "C", Foreground = Brushes.White, FontWeight = FontWeights.Bold, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
        head.Children.Add(logo);
        var menu = IconButton(new IconCtl { Kind = "dots", Size = 15 }, "Menu", () => { });
        menu.Click += (_, _) => OpenMenu(menu);
        foreach (var b in new[] { menu, IconButton(_chev, "Réduire / déplier", () => _c.Update(s => s.Collapsed = !s.Collapsed)), IconButton(_syncIcon, "Synchroniser avec EPREL", () => (Application.Current as App)?.SyncFromWidget()) })
        {
            DockPanel.SetDock(b, Dock.Right); head.Children.Add(b);
        }
        DockPanel.SetDock(_badge, Dock.Right);
        head.Children.Add(_badge);
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(new TextBlock { Text = "Campus", FontWeight = FontWeights.SemiBold, FontSize = 13 });
        _sub.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        titles.Children.Add(_sub);
        head.Children.Add(titles);
        return head;
    }

    void OpenMenu(Button anchor)
    {
        var m = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Bottom };
        void Add(string label, string gesture, Action a, bool danger = false)
        {
            var mi = new MenuItem { Header = label, InputGestureText = gesture };
            if (danger) mi.SetResourceReference(ForegroundProperty, "Bad");
            mi.Click += (_, _) => a();
            m.Items.Add(mi);
        }
        Add("Ouvrir Campus Manager", "Ctrl+Alt+C", () => App.Current.OpenMain());
        Add("Personnaliser le widget…", "", () => App.Current.OpenMain("settings"));
        Add("Laisser passer les clics", "Ctrl+Alt+X", () => _c.Update(s => s.ClickThrough = true));
        Add("Masquer", "Ctrl+Alt+W", _c.Hide);
        m.Items.Add(new Separator());
        Add("Quitter Campus Manager", "", () => App.Current.QuitAll(), danger: true);
        m.IsOpen = true;
    }

    TextBlock H4(string text, string? page)
    {
        var t = new TextBlock { Text = text.ToUpperInvariant(), FontSize = 10.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(2, 10, 0, 4) };
        t.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        if (page != null)
        {
            t.Cursor = Cursors.Hand;
            t.MouseLeftButtonUp += (_, _) => App.Current.OpenMain(page);
        }
        return t;
    }

    Button FileRow(FileItem f, string meta, string tip)
    {
        var b = new Button { Style = (Style)Application.Current.FindResource("Row"), Padding = new Thickness(6, 5, 6, 5), ToolTip = tip };
        Ui.SetRadius(b, new CornerRadius(8));
        var p = new DockPanel();
        p.Children.Add(new FileIcon { Ext = f.Ext, Size = 16, ShowLabel = false, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
        var m = new TextBlock { Text = meta, FontSize = 11, MaxWidth = 90, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(8, 0, 0, 0) };
        m.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        DockPanel.SetDock(m, Dock.Right);
        p.Children.Add(m);
        p.Children.Add(new TextBlock { Text = f.Name, TextTrimming = TextTrimming.CharacterEllipsis });
        b.Content = p;
        b.Click += (_, _) => Shell.Open(_s.Lib.Abs(f.Path));
        return b;
    }

    public void Reload()
    {
        var d = Queries.WidgetData(_s.Db, Fmt.Now());
        _sub.Text = _s.State.Running ? "Synchronisation…" : d.LastSync is { } ls ? $"Synchro {Fmt.Date(ls, true)}" : "Jamais synchronisé";
        _sub.Visibility = _st.Collapsed ? Visibility.Collapsed : Visibility.Visible;
        _badge.Visibility = _st.Collapsed ? Visibility.Visible : Visibility.Collapsed;
        _badge.ToolTip = "Devoirs à faire";
        _badgeText.Text = d.Progress.Open.ToString();
        _syncIcon.Spin = _s.State.Running;
        _scroll.Visibility = _st.Collapsed ? Visibility.Collapsed : Visibility.Visible;
        _body.Children.Clear();
        if (_st.Collapsed) return;

        if (_st.Sections.Assignments)
        {
            _body.Children.Add(H4("Devoirs à venir", "assignments"));
            if (d.Assignments.Count == 0)
                _body.Children.Add(new TextBlock { Text = "Aucun devoir en attente", Margin = new Thickness(6, 4, 6, 4) }.Res(TextBlock.ForegroundProperty, "Muted"));
            foreach (var a in d.Assignments)
            {
                var u = Fmt.Urgency(a);
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 2) };
                var wrap = new Border { Padding = new Thickness(6), CornerRadius = new CornerRadius(9), Child = row, Background = Brushes.Transparent };
                wrap.MouseEnter += (_, _) => wrap.SetResourceReference(Border.BackgroundProperty, "Hover");
                wrap.MouseLeave += (_, _) => wrap.Background = Brushes.Transparent;
                var check = new CheckBox { ToolTip = "Marquer comme terminé", Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
                var id = a.Id;
                check.Click += (_, _) => { Queries.SetAssignment(_s.Db, id, status: "done"); _s.NotifyChanged(); };
                row.Children.Add(check);
                var right = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(8, 0, 0, 0) };
                right.Children.Add(new TextBlock { Text = Fmt.TimeLeft(a.DueAt), FontSize = 11.5, HorizontalAlignment = HorizontalAlignment.Right }.Res(TextBlock.ForegroundProperty, W.UrgencyBrush(u)));
                if (Fmt.UrgencyLabel(u) is { Length: > 0 } lbl) { var pill = W.Pill(lbl, W.UrgencyBrush(u), 10); pill.HorizontalAlignment = HorizontalAlignment.Right; pill.Margin = new Thickness(0, 2, 0, 0); right.Children.Add(pill); }
                DockPanel.SetDock(right, Dock.Right);
                row.Children.Add(right);
                var link = new Button { Style = (Style)Application.Current.FindResource("Plain"), ToolTip = a.Description ?? a.Name, HorizontalAlignment = HorizontalAlignment.Stretch };
                var lp = new StackPanel();
                lp.Children.Add(new TextBlock { Text = a.Name, TextTrimming = TextTrimming.CharacterEllipsis });
                lp.Children.Add(new TextBlock { Text = a.Course, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis }.Res(TextBlock.ForegroundProperty, "Muted"));
                link.Content = lp;
                link.Click += (_, _) => App.Current.OpenMain("assignments");
                row.Children.Add(link);
                _body.Children.Add(wrap);
            }
        }
        if (_st.Sections.Work && d.Work.Count > 0)
        {
            _body.Children.Add(H4("TD / TP récents", "work"));
            foreach (var f in d.Work) _body.Children.Add(FileRow(f, f.Category, "Ouvrir"));
        }
        if (_st.Sections.Recent && d.Recent.Count > 0)
        {
            _body.Children.Add(H4("Dernières publications", "recent"));
            foreach (var f in d.Recent) _body.Children.Add(FileRow(f, f.Course.Split(" - ")[0], $"{f.Course} · ouvrir"));
        }
        var total = d.Progress.Done + d.Progress.Open;
        if (_st.Sections.Progress && total > 0)
        {
            _body.Children.Add(H4("Progression", null));
            var t = new TextBlock { Margin = new Thickness(4, 2, 4, 6) }.Res(TextBlock.ForegroundProperty, "Muted");
            t.Inlines.Add(new System.Windows.Documents.Run(d.Progress.Done.ToString()) { FontWeight = FontWeights.Bold });
            t.Inlines.Add(new System.Windows.Documents.Run(" terminés · "));
            t.Inlines.Add(new System.Windows.Documents.Run(d.Progress.Open.ToString()) { FontWeight = FontWeights.Bold });
            t.Inlines.Add(new System.Windows.Documents.Run(" restants"));
            if (d.Progress.Late > 0)
            {
                t.Inlines.Add(new System.Windows.Documents.Run(" · "));
                var late = new System.Windows.Documents.Run(d.Progress.Late.ToString()) { FontWeight = FontWeights.Bold };
                late.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Bad");
                t.Inlines.Add(late);
                t.Inlines.Add(new System.Windows.Documents.Run(" en retard"));
            }
            _body.Children.Add(t);
            var bar = W.Bar((double)d.Progress.Done / total, 6, "Accent", "Hover"); bar.Margin = new Thickness(4, 0, 4, 0);
            _body.Children.Add(bar);
        }
    }
}
