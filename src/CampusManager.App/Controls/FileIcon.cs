using System.Globalization;
using System.Windows;
using System.Windows.Media;
using CampusManager.App.Services;

namespace CampusManager.App.Controls;

/// <summary>Pastille d'extension colorée par type de fichier (coin haut droit arrondi).</summary>
public class FileIcon : FrameworkElement
{
    public static readonly DependencyProperty ExtProperty = DependencyProperty.Register(nameof(Ext), typeof(string), typeof(FileIcon),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(nameof(Size), typeof(double), typeof(FileIcon),
        new FrameworkPropertyMetadata(34.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ShowLabelProperty = DependencyProperty.Register(nameof(ShowLabel), typeof(bool), typeof(FileIcon),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public string Ext { get => (string)GetValue(ExtProperty); set => SetValue(ExtProperty, value); }
    public double Size { get => (double)GetValue(SizeProperty); set => SetValue(SizeProperty, value); }
    public bool ShowLabel { get => (bool)GetValue(ShowLabelProperty); set => SetValue(ShowLabelProperty, value); }

    static readonly Dictionary<string, (Brush Fill, Pen Border, Brush Text)> Cache = [];
    static readonly Typeface Face = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

    protected override Size MeasureOverride(Size _) => new(Size, Size * 1.22);

    protected override void OnRender(DrawingContext dc)
    {
        var hex = Fmt.KindColor(Ext ?? "");
        if (!Cache.TryGetValue(hex, out var b))
        {
            var c = (Color)ColorConverter.ConvertFromString(hex);
            b = (Freeze(new SolidColorBrush(Color.FromArgb(46, c.R, c.G, c.B))),
                 Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromArgb(140, c.R, c.G, c.B))), 1)),
                 Freeze(new SolidColorBrush(c)));
            Cache[hex] = b;
        }
        double w = Size, h = Size * 1.22, r = Math.Max(2, Size * 0.12), big = Math.Max(3, Size * 0.26);
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(new Point(0.5 + r, 0.5), true, true);
            ctx.LineTo(new Point(w - 0.5 - big, 0.5), true, false);
            ctx.ArcTo(new Point(w - 0.5, 0.5 + big), new Size(big, big), 0, false, SweepDirection.Clockwise, true, false);
            ctx.LineTo(new Point(w - 0.5, h - 0.5 - r), true, false);
            ctx.ArcTo(new Point(w - 0.5 - r, h - 0.5), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
            ctx.LineTo(new Point(0.5 + r, h - 0.5), true, false);
            ctx.ArcTo(new Point(0.5, h - 0.5 - r), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
            ctx.LineTo(new Point(0.5, 0.5 + r), true, false);
            ctx.ArcTo(new Point(0.5 + r, 0.5), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
        }
        g.Freeze();
        dc.DrawGeometry(b.Fill, b.Border, g);
        if (!ShowLabel || Size < 18) return;
        var label = (string.IsNullOrEmpty(Ext) ? "?" : Ext.Length > 4 ? Ext[..4] : Ext).ToUpperInvariant();
        var ft = new FormattedText(label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, Math.Max(8, Size * 0.26) * 0.75 * 96 / 72, b.Text,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(ft, new Point((w - ft.Width) / 2, h * 0.88 - ft.Height));
    }

    static T Freeze<T>(T f) where T : Freezable { f.Freeze(); return f; }
}
