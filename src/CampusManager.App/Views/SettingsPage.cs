using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CampusManager.App.Controls;
using CampusManager.App.Services;
using CampusManager.Core.Queries;

namespace CampusManager.App.Views;

public sealed class SettingsPage : UserControl, IReloadable
{
    readonly MainWindow _w;
    readonly StackPanel _root = new() { Margin = new Thickness(0, 0, 6, 8), MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left };
    TextBlock? _syncInfo;
    Button? _syncBtn, _reloginBtn;

    static AppServices S => App.Services;

    public SettingsPage(MainWindow w)
    {
        _w = w;
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _root };
        Reload();
    }

    static Border Card(params UIElement[] children)
    {
        var p = new StackPanel();
        foreach (var c in children) p.Children.Add(c);
        return new Border { Style = (Style)Application.Current.FindResource("CardBox"), Child = p, Margin = new Thickness(0, 0, 0, 14) };
    }
    static TextBlock H2(string t) => new() { Text = t, Style = (Style)Application.Current.FindResource("H2") };
    static TextBlock Note(string t, string style = "Small") { var x = W.Text(t, style, ellipsis: false); x.TextWrapping = TextWrapping.Wrap; x.Margin = new Thickness(0, 4, 0, 4); return x; }
    static Border Code(string t) => new Border { CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), Padding = new Thickness(7, 3, 7, 3), VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock { Text = t, FontSize = 12, FontFamily = new FontFamily("Consolas"), TextWrapping = TextWrapping.Wrap } }.Res(Border.BackgroundProperty, "Bg").Res(Border.BorderBrushProperty, "Line");
    Button Btn(string text, string style, Action click)
    {
        var b = new Button { Content = text, Style = (Style)FindResource(style), Margin = new Thickness(0, 0, 8, 0) };
        b.Click += (_, _) => click();
        return b;
    }
    static StackPanel Row(params UIElement[] c) { var p = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 8) }; foreach (var x in c) p.Children.Add(x); return p; }
    static CheckBox Check(string label, bool value, Action<bool> set)
    {
        var c = new CheckBox { Content = label, IsChecked = value };
        c.Click += (_, _) => set(c.IsChecked == true);
        return c;
    }

    public void Reload()
    {
        _root.Children.Clear();
        _root.Children.Add(Card(H2("Bibliothèque"),
            Note("Tes fichiers sont de vrais fichiers Windows dans ce dossier."),
            Row(Code(S.Lib.Root), Btn("Ouvrir le dossier", "BtnGhost", () => Shell.Open(S.Lib.Root)).Also(b => b.Margin = new Thickness(10, 0, 0, 0))),
            Note($"Base de données et journaux : {S.DataDir}"),
            Note($"Recherche dans le texte des fichiers (PDF, Word, PowerPoint, texte, code) : {S.Db.Scalar<long>("SELECT COUNT(*) FROM content_index WHERE chars > 0")} fichier(s) indexé(s), mis à jour après chaque synchronisation."),
            Note("Changer de dossier racine : prévu à une étape suivante (déplacement complet de la bibliothèque).")));

        _syncInfo = Note("", "Small");
        _syncBtn = Btn("Synchroniser maintenant", "BtnPrimary", () => _w.RunSync());
        _reloginBtn = Btn("Se reconnecter", "BtnGhost", () => _w.RunSync(relogin: true));
        _root.Children.Add(Card(H2("Connexion EPREL"),
            new TextBlock { Text = "Le jeton de connexion est chiffré par Windows. Ton mot de passe n'est jamais enregistré.", FontSize = 12.5, TextWrapping = TextWrapping.Wrap },
            _syncInfo, Row(_syncBtn, _reloginBtn)));
        UpdateSync(_w.Vm.Sync);

        _root.Children.Add(AutoSyncCard());
        _root.Children.Add(WidgetCard());

        var dark = Btn("Onyx (sombre)", "BtnChoice", () => { _w.Vm.Theme = "dark"; Reload(); });
        var light = Btn("Clair", "BtnChoice", () => { _w.Vm.Theme = "light"; Reload(); });
        Ui.SetActive(dark, _w.Vm.Theme == "dark"); Ui.SetActive(light, _w.Vm.Theme == "light");
        var cur = double.TryParse(S.Setting("uiOpacity"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var ov) ? ov : MainWindow.DefaultOpacity;
        string OpLabel(double pct) => $"Opacité du fond de la fenêtre : {Math.Round(pct)} % (plus bas = on voit davantage ce qu'il y a derrière)";
        var opText = Note(OpLabel(cur * 100), "TMuted");
        var op = new Slider { Minimum = 20, Maximum = 100, Value = Math.Round(cur * 100), IsMoveToPointEnabled = true, Margin = new Thickness(0, 4, 0, 0) };
        op.ValueChanged += (_, e) =>
        {
            S.SetSetting("uiOpacity", (e.NewValue / 100).ToString(System.Globalization.CultureInfo.InvariantCulture));
            opText.Text = OpLabel(e.NewValue);
            _w.ApplyTint();
        };
        _root.Children.Add(Card(H2("Apparence"), Row(dark, light), opText, op));

        _root.Children.Add(Card(H2("Démarrage de Windows"),
            Check("Lancer Campus Manager à l'ouverture de session (widget seul, sans fenêtre)", Autostart.Get(), on =>
            {
                var r = Autostart.Set(on);
                S.SetSetting("autostart", r ? "1" : "0");
            })));

        var quit = Btn("Quitter complètement (ferme aussi le widget)", "BtnGhost", () => App.Current.QuitAll());
        quit.SetResourceReference(ForegroundProperty, "Bad");
        _root.Children.Add(Card(H2("Application"),
            Note("Fermer cette fenêtre (croix, Alt+F4) ne quitte pas Campus Manager : le widget reste sur le bureau et la synchronisation continue. Ctrl+Alt+C, le raccourci du Bureau ou le menu ⋯ du widget la rouvrent."),
            quit));
    }

    Border AutoSyncCard()
    {
        var a = App.AutoSync;
        var interval = new ComboBox { Width = 240, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var (m, label) in AutoSync.Intervals) interval.Items.Add(new ComboBoxItem { Content = label, Tag = m });
        interval.SelectedItem = interval.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (int)i.Tag == a.Minutes)
            ?? interval.Items.OfType<ComboBoxItem>().First(i => (int)i.Tag == AutoSync.DefaultMinutes);
        interval.SelectionChanged += (_, _) => { if (interval.SelectedItem is ComboBoxItem i) a.Minutes = (int)i.Tag; };

        var n = a.Notify;
        void SetN(Action<NotifySettings> p) { var x = a.Notify; p(x); a.Notify = x; Reload(); }
        var subs = new StackPanel { Margin = new Thickness(24, 0, 0, 0), IsEnabled = n.Enabled, Opacity = n.Enabled ? 1 : 0.5 };
        subs.Children.Add(Check("Nouveaux documents publiés sur EPREL (après une synchro automatique)", n.NewFiles, v => SetN(s => s.NewFiles = v)));
        subs.Children.Add(Check("Devoir à rendre dans moins de 24 h (un rappel par devoir)", n.Deadlines, v => SetN(s => s.Deadlines = v)));

        return Card(H2("Synchronisation automatique"),
            Note("Campus Manager synchronise tout seul en arrière-plan, même fenêtre fermée (widget seul). Une synchro automatique n'ouvre jamais la page de connexion : si la session expire, une notification te prévient."),
            Row(new TextBlock { Text = "Fréquence", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) }, interval),
            Note("Notifications Windows", "TMuted"),
            Check("Activer les notifications", n.Enabled, v => SetN(s => s.Enabled = v)),
            subs,
            Row(Btn("Tester une notification", "BtnGhost", () => Notifier.Show("Campus Manager", "Les notifications fonctionnent : un clic ouvre les devoirs.", "assignments"))));
    }

    Border WidgetCard()
    {
        var w = App.Widget.Get();
        void Set(Action<WidgetSettings> p) { App.Widget.Update(p); Reload(); }
        var left = new StackPanel();
        left.Children.Add(Note("Informations affichées", "TMuted"));
        left.Children.Add(Check("Devoirs à venir", w.Sections.Assignments, v => Set(s => s.Sections.Assignments = v)));
        left.Children.Add(Check("TD / TP récents", w.Sections.Work, v => Set(s => s.Sections.Work = v)));
        left.Children.Add(Check("Dernières publications", w.Sections.Recent, v => Set(s => s.Sections.Recent = v)));
        left.Children.Add(Check("Progression", w.Sections.Progress, v => Set(s => s.Sections.Progress = v)));
        var right = new StackPanel();
        right.Children.Add(Note("Comportement", "TMuted"));
        right.Children.Add(Check("Masquer quand une appli est en plein écran", w.AutoHideFullscreen, v => Set(s => s.AutoHideFullscreen = v)));
        right.Children.Add(Check("Clic à travers (Ctrl+Alt+X pour basculer)", w.ClickThrough, v => Set(s => s.ClickThrough = v)));
        var grid = new Grid { Margin = new Thickness(0, 8, 0, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetColumn(right, 1);
        grid.Children.Add(left); grid.Children.Add(right);

        var opLabel = Note(OpacityText(w.Opacity), "TMuted");
        var slider = new Slider { Minimum = 0, Maximum = 100, Value = Math.Round(w.Opacity * 100), IsMoveToPointEnabled = true };
        slider.ValueChanged += (_, e) => { opLabel.Text = OpacityText(e.NewValue / 100); App.Widget.Update(s => s.Opacity = e.NewValue / 100); };

        var dark = Btn("Sombre", "BtnChoice", () => Set(s => s.Theme = "dark"));
        var light = Btn("Clair", "BtnChoice", () => Set(s => s.Theme = "light"));
        Ui.SetActive(dark, w.Theme == "dark"); Ui.SetActive(light, w.Theme == "light");

        return Card(H2("Widget de bureau"),
            Note("Il reste sur le bureau, sous tes fenêtres, même quand cette fenêtre est fermée. Il n'utilise que les données locales."),
            Check("Afficher le widget", w.Enabled, v => Set(s => s.Enabled = v)),
            grid, opLabel, slider,
            Row(dark, light, Btn("Afficher maintenant", "BtnGhost", () => App.Widget.Show())),
            Note("Déplacer : glisser l'en-tête. Redimensionner : poignée en bas à droite. Ctrl+Alt+W affiche/masque, Ctrl+Alt+C rouvre cette fenêtre."));
    }

    static string OpacityText(double o) => "Opacité du fond : " + (o < 0.15 ? "entièrement transparent" : $"{Math.Round(o * 100)} %");

    public void UpdateSync(SyncState s)
    {
        if (_syncInfo == null) return;
        _syncInfo.Text = $"Dernière synchronisation : {Fmt.Date(s.LastAt, true)}" + (s.Message.Length > 0 ? $" · {s.Message}" : "");
        _syncBtn!.IsEnabled = _reloginBtn!.IsEnabled = !s.Running;
        _syncBtn.Content = s.Running ? "Synchronisation…" : "Synchroniser maintenant";
    }
}

public static class ObjExt
{
    public static T Also<T>(this T o, Action<T> a) { a(o); return o; }
}
