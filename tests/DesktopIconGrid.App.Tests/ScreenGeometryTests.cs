using System.Windows;
using DesktopIconGrid.Services;

namespace DesktopIconGrid.App.Tests;

public class ScreenGeometryTests
{
    private static readonly Rect Left = new(0, 0, 1920, 1080);
    private static readonly Rect Right = new(1920, 0, 1920, 1080);
    private static readonly Rect Below = new(0, 1080, 1920, 1080); // L-shaped with Left and Right: (1920,1080)-(3840,2160) is dead

    [Fact]
    public void A_bar_on_either_of_two_side_by_side_monitors_is_visible()
    {
        Assert.True(ScreenGeometry.IsVisibleOn(new Rect(100, 100, 400, 80), [Left, Right]));
        Assert.True(ScreenGeometry.IsVisibleOn(new Rect(2500, 900, 400, 80), [Left, Right]));
        Assert.True(ScreenGeometry.IsVisibleOn(new Rect(1700, 500, 400, 80), [Left, Right])); // straddling the seam
    }

    [Fact]
    public void A_bar_in_the_dead_corner_of_an_L_shaped_layout_is_not_visible()
    {
        var bar = new Rect(2400, 1500, 400, 80); // inside the virtual screen's bounding box, on no monitor

        Assert.False(ScreenGeometry.IsVisibleOn(bar, [Left, Right, Below]));
        Assert.True(ScreenGeometry.IsVisibleOn(new Rect(100, 1500, 400, 80), [Left, Right, Below]));
    }

    [Fact]
    public void A_sliver_does_not_count_as_visible()
    {
        Assert.False(ScreenGeometry.IsVisibleOn(new Rect(1900, 100, 400, 80), [Left])); // 20 px showing
        Assert.True(ScreenGeometry.IsVisibleOn(new Rect(1880, 100, 400, 80), [Left])); // 40 px showing
        Assert.False(ScreenGeometry.IsVisibleOn(new Rect(100, 1070, 400, 80), [Left])); // 10 px tall strip
        Assert.False(ScreenGeometry.IsVisibleOn(new Rect(-500, -500, 400, 80), [Left, Right]));
    }

    [Fact]
    public void Monitors_are_enumerated_or_the_virtual_screen_is_used()
    {
        var monitors = ScreenGeometry.Monitors();

        Assert.NotEmpty(monitors);
        Assert.All(monitors, m => Assert.True(m.Width > 0 && m.Height > 0));
    }
}
