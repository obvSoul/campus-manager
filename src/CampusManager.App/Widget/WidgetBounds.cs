using System.Windows;

namespace CampusManager.App.Widget;

public static class WidgetBounds
{
    public const double DefaultWidth = 340, DefaultHeight = 520, CollapsedHeight = 46;

    /// <summary>
    /// Position du widget : celle enregistrée si elle est encore visible sur un écran, sinon en haut à droite
    /// de l'écran principal (le moniteur a pu être débranché).
    /// </summary>
    public static Rect Resolve(Rect? saved, IReadOnlyList<Rect> screens, Rect primaryWorkArea)
    {
        var def = new Rect(primaryWorkArea.Right - DefaultWidth - 24, primaryWorkArea.Top + 24, DefaultWidth, DefaultHeight);
        if (saved is not { } b) return def;
        var visible = screens.Any(d => b.X >= d.X - 50 && b.X < d.Right - 50 && b.Y >= d.Y - 10 && b.Y < d.Bottom - 50);
        return visible ? b : def;
    }
}
