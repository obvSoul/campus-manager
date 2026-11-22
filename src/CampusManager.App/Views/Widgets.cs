using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace CampusManager.App.Views;

/// <summary>Petits éléments d'interface construits en code (barres, pastilles, points d'urgence).</summary>
public static class W
{
    public static TextBlock Text(string s, string? style = null, double? size = null, bool ellipsis = true, FontWeight? weight = null)
    {
        var t = new TextBlock { Text = s };
        if (ellipsis) t.TextTrimming = TextTrimming.CharacterEllipsis;
        if (style != null) t.Style = (Style)Application.Current.FindResource(style);
        if (size != null) t.FontSize = size.Value;
        if (weight != null) t.FontWeight = weight.Value;
        return t;
    }

    public static T Res<T>(this T e, DependencyProperty p, string key) where T : FrameworkElement { e.SetResourceReference(p, key); return e; }

    /// <summary>Barre de progression arrondie.</summary>
    public static Grid Bar(double ratio, double height = 8, string fill = "Accent", string track = "Bg")
    {
        var g = new Grid { Height = height, ClipToBounds = true };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Clamp(ratio, 0, 1), GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - Math.Clamp(ratio, 0, 1), GridUnitType.Star) });
        var back = new Border { CornerRadius = new CornerRadius(height / 2 + 1) }.Res(Border.BackgroundProperty, track);
        Grid.SetColumnSpan(back, 2);
        g.Children.Add(back);
        g.Children.Add(new Border { CornerRadius = new CornerRadius(height / 2 + 1) }.Res(Border.BackgroundProperty, fill));
        return g;
    }

    /// <summary>Couleur d'une urgence (late, 24h, 48h, 7d…).</summary>
    public static string UrgencyBrush(string u) => u switch { "late" or "24h" => "Bad", "48h" => "Warn", "7d" => "Accent", _ => "Muted" };

    public static Ellipse Dot(string u) => new Ellipse { Width = 9, Height = 9, VerticalAlignment = VerticalAlignment.Center }.Res(Shape.FillProperty, UrgencyBrush(u));

    public static Border Pill(string text, string brush, double size = 11) => new Border
    {
        CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 1, 8, 1), VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock { Text = text, Foreground = Brushes.White, FontSize = size },
    }.Res(Border.BackgroundProperty, brush);

    public static Border Tag(string text, string bg = "Hover", string fg = "Muted")
    {
        var t = new TextBlock { Text = text, FontSize = 11.5 }.Res(TextBlock.ForegroundProperty, fg);
        return new Border { CornerRadius = new CornerRadius(5), Padding = new Thickness(7, 1, 7, 1), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left, Child = t }
            .Res(Border.BackgroundProperty, bg);
    }
}
