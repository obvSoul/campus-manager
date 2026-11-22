using System.Windows;
using CampusManager.App.Widget;

namespace CampusManager.Tests;

public class WidgetBoundsTests
{
    static readonly Rect Work = new(0, 0, 1920, 1040);
    static readonly Rect[] Screens = [new(0, 0, 1920, 1080)];

    [Fact]
    public void NoSavedBounds_TopRightDefault() =>
        Assert.Equal(new Rect(1920 - 340 - 24, 24, 340, 520), WidgetBounds.Resolve(null, Screens, Work));

    [Fact]
    public void BoundsVisible_Kept()
    {
        var saved = new Rect(100, 200, 400, 600);
        Assert.Equal(saved, WidgetBounds.Resolve(saved, Screens, Work));
    }

    [Fact]
    public void BoundsOffScreen_FallBackToDefault() =>
        // Position enregistrée sur un 2ᵉ écran débranché depuis.
        Assert.Equal(new Rect(1920 - 340 - 24, 24, 340, 520), WidgetBounds.Resolve(new Rect(2500, 100, 340, 520), Screens, Work));
}
