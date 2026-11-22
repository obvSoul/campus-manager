using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CampusManager.App.Controls;
using CampusManager.App.Services;
using CampusManager.Core.Queries;

namespace CampusManager.App.Views;

/// <summary>Panneau de détails : aperçu local + métadonnées. L'aperçu est libéré dès qu'on change de fichier.</summary>
public sealed class DetailsPane : Border
{
    public event Action? Closed;
    readonly StackPanel _root = new();
    int _token;

    static readonly string[] Images = ["png", "jpg", "jpeg", "gif", "webp", "bmp"];
    static readonly string[] Texts = ["txt", "md", "c", "h", "py", "js", "ts", "html", "css", "sql", "json", "csv"];

    public DetailsPane()
    {
        Width = 340;
        BorderThickness = new Thickness(1, 0, 0, 0);
        SetResourceReference(BorderBrushProperty, "Line");
        Child = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _root, Padding = new Thickness(14) };
    }

    public void Clear() { _token++; _root.Children.Clear(); }

    public void Show(long id)
    {
        Clear();
        var token = _token;
        FileDetails d;
        try { d = Queries.Details(App.Services.Db, App.Services.Lib, id); }
        catch { _root.Children.Add(W.Text("Détails indisponibles.", "TMuted")); return; }
        var f = d.Item;

        var head = new DockPanel();
        head.Children.Add(new FileIcon { Ext = f.Ext, Size = 30, Margin = new Thickness(0, 0, 10, 0) });
        var close = new Button { Style = (Style)FindResource("IconBtn"), ToolTip = "Fermer", Content = new Icon { Kind = "close", Size = 16 } };
        close.Click += (_, _) => Closed?.Invoke();
        DockPanel.SetDock(close, Dock.Right);
        head.Children.Add(close);
        var title = W.Text(f.Name, weight: FontWeights.SemiBold); title.ToolTip = f.Name; title.VerticalAlignment = VerticalAlignment.Center;
        head.Children.Add(title);
        _root.Children.Add(head);

        var preview = new Border { Height = 300, Margin = new Thickness(0, 12, 0, 12), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), ClipToBounds = true }
            .Res(BorderBrushProperty, "Line").Res(BackgroundProperty, "Bg");
        _root.Children.Add(preview);
        if (f.Missing || !File.Exists(d.AbsPath)) preview.Child = NoPreview("alert", "Fichier introuvable sur le disque", null);
        else if (Images.Contains(f.Ext)) preview.Child = ImagePreview(d.AbsPath);
        else if (f.Ext == "pdf") { preview.Child = W.Text("Chargement de l'aperçu…", "TMuted"); _ = LoadPdf(d.AbsPath, preview, token); }
        else if (Texts.Contains(f.Ext)) preview.Child = TextPreview(d.AbsPath);
        else preview.Child = NoPreview(null, "Pas d'aperçu pour ce type de fichier", f.Ext);
        if (preview.Child is TextBlock tb) { tb.HorizontalAlignment = HorizontalAlignment.Center; tb.VerticalAlignment = VerticalAlignment.Center; }

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        var open = new Button { Style = (Style)FindResource("BtnPrimary") };
        var op = new StackPanel { Orientation = Orientation.Horizontal };
        op.Children.Add(new Icon { Kind = "open", Size = 15, Foreground = Brushes.White, Margin = new Thickness(0, 0, 6, 0) });
        op.Children.Add(new TextBlock { Text = "Ouvrir" });
        open.Content = op;
        open.Click += (_, _) => Shell.Open(d.AbsPath);
        var reveal = new Button { Style = (Style)FindResource("BtnGhost"), Content = "Emplacement", Margin = new Thickness(8, 0, 0, 0) };
        reveal.Click += (_, _) => Shell.Reveal(d.AbsPath);
        actions.Children.Add(open); actions.Children.Add(reveal);
        _root.Children.Add(actions);

        var meta = new Grid();
        meta.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
        meta.ColumnDefinitions.Add(new ColumnDefinition());
        void Row(string k, string v, string? brush = null, bool mono = false)
        {
            meta.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var r = meta.RowDefinitions.Count - 1;
            var kt = W.Text(k, "TMuted", 12.5); kt.Margin = new Thickness(0, 3, 10, 3); Grid.SetRow(kt, r);
            var vt = new TextBlock { Text = v, FontSize = mono ? 11.5 : 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 3) };
            if (mono) { vt.FontFamily = new FontFamily("Consolas"); vt.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); }
            if (brush != null) vt.SetResourceReference(TextBlock.ForegroundProperty, brush);
            Grid.SetRow(vt, r); Grid.SetColumn(vt, 1);
            meta.Children.Add(kt); meta.Children.Add(vt);
        }
        Row("Matière", f.Course.Length > 0 ? f.Course : "—");
        Row("Semestre", f.Semester.Length > 0 ? f.Semester : "—");
        Row("Type", f.Category);
        Row("Extension", "." + (f.Ext.Length > 0 ? f.Ext : "?"));
        Row("Taille", Fmt.Size(f.Size));
        Row("Ajouté le", Fmt.Date(f.AddedAt, true));
        Row("Modifié le", Fmt.Date(d.ModifiedAt, true));
        Row("Origine", (d.FromEprel ? "EPREL" : "Personnel") + (f.RemoteDeleted ? " (retiré d'EPREL)" : ""));
        if (d.LocallyModified) Row("État", "Modifié par toi : jamais écrasé par la synchronisation", "Warn");
        if (d.ResourceName != null) Row("Ressource", d.ResourceName);
        if (d.Section != null) Row("Section", d.Section);
        if (d.Description != null) Row("Description", d.Description);
        Row("Chemin", f.Path, mono: true);
        _root.Children.Add(meta);
    }

    FrameworkElement NoPreview(string? icon, string text, string? ext)
    {
        var p = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        if (icon != null) p.Children.Add(new Icon { Kind = icon, Size = 28, HorizontalAlignment = HorizontalAlignment.Center }.Res(Icon.ForegroundProperty, "Muted"));
        if (ext != null) p.Children.Add(new FileIcon { Ext = ext, Size = 54, HorizontalAlignment = HorizontalAlignment.Center });
        var t = W.Text(text, "TMuted", 12.5); t.Margin = new Thickness(0, 10, 0, 0); t.HorizontalAlignment = HorizontalAlignment.Center;
        p.Children.Add(t);
        return p;
    }

    static FrameworkElement ImagePreview(string path)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad; // fichier relâché tout de suite
            bmp.DecodePixelWidth = 640;               // décodé à la taille affichée, pas en pleine résolution
            bmp.UriSource = new Uri(path);
            bmp.EndInit();
            bmp.Freeze();
            return new Image { Source = bmp, Stretch = Stretch.Uniform };
        }
        catch { return W.Text("Image illisible", "TMuted"); }
    }

    static FrameworkElement TextPreview(string path)
    {
        string text;
        try
        {
            using var s = File.OpenRead(path);
            var buf = new byte[Math.Min(64 * 1024, s.Length)];
            s.ReadExactly(buf);
            text = Encoding.UTF8.GetString(buf);
        }
        catch (Exception e) { text = e.Message; }
        return new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new TextBlock { Text = text, FontFamily = new FontFamily("Consolas"), FontSize = 11.5, Margin = new Thickness(10, 8, 10, 8) },
        };
    }

    async Task LoadPdf(string path, Border host, int token)
    {
        try
        {
            var (pages, images) = await PdfPreview.RenderAsync(path, 3, 620);
            if (token != _token) return; // l'utilisateur a changé de fichier entre-temps
            var stack = new StackPanel();
            foreach (var img in images) stack.Children.Add(new Image { Source = img, Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 0, 6) });
            if (pages > images.Count)
                stack.Children.Add(new TextBlock { Text = $"… {pages - images.Count} page(s) de plus : « Ouvrir » pour tout lire", FontSize = 12, Margin = new Thickness(8), HorizontalAlignment = HorizontalAlignment.Center }.Res(TextBlock.ForegroundProperty, "Muted"));
            host.Child = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = stack, Style = (Style)FindResource("ThinScroll") };
        }
        catch (Exception e)
        {
            if (token == _token) host.Child = NoPreview("alert", $"Aperçu impossible : {e.Message}", null);
        }
    }
}
