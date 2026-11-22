using System.Windows;

namespace CampusManager.App.Controls;

/// <summary>Propriétés attachées utilisées par les styles (état « actif », arrondi).</summary>
public static class Ui
{
    public static readonly DependencyProperty ActiveProperty = DependencyProperty.RegisterAttached("Active", typeof(bool), typeof(Ui),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));
    public static bool GetActive(DependencyObject o) => (bool)o.GetValue(ActiveProperty);
    public static void SetActive(DependencyObject o, bool v) => o.SetValue(ActiveProperty, v);

    public static readonly DependencyProperty RadiusProperty = DependencyProperty.RegisterAttached("Radius", typeof(CornerRadius), typeof(Ui),
        new FrameworkPropertyMetadata(new CornerRadius(7)));
    public static CornerRadius GetRadius(DependencyObject o) => (CornerRadius)o.GetValue(RadiusProperty);
    public static void SetRadius(DependencyObject o, CornerRadius v) => o.SetValue(RadiusProperty, v);

    /// <summary>Zone de dépôt survolée par un glisser de fichiers.</summary>
    public static readonly DependencyProperty DropOverProperty = DependencyProperty.RegisterAttached("DropOver", typeof(bool), typeof(Ui),
        new FrameworkPropertyMetadata(false));
    public static bool GetDropOver(DependencyObject o) => (bool)o.GetValue(DropOverProperty);
    public static void SetDropOver(DependencyObject o, bool v) => o.SetValue(DropOverProperty, v);
}
