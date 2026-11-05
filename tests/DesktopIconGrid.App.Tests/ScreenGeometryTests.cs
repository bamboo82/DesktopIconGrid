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
    public void A_mixed_dpi_layout_never_produces_a_false_off_screen_verdict()
    {
        // Laptop at 150 % (primary, 2880×1620 px) with a 100 % monitor to its right (1920×1080 px).
        var laptop = new Rect(0, 0, 2880, 1620);
        var external = new Rect(2880, 0, 1920, 1080);

        // A bar saved while on the external monitor is in that monitor's units (100 %): at the primary's 150 %
        // it would be computed as off every screen, at 100 % it is on the external monitor, so it stays put.
        var barOnExternal = new Rect(3500, 300, 400, 80);
        Assert.False(ScreenGeometry.IsVisibleOn(new Rect(5250, 450, 600, 120), [laptop, external]));
        Assert.True(ScreenGeometry.IsVisibleAtAnyScale(barOnExternal, [laptop, external], primaryScale: 1.5));

        // A bar far outside every monitor at every plausible scale is reset.
        Assert.False(ScreenGeometry.IsVisibleAtAnyScale(new Rect(6000, 4000, 400, 80), [laptop, external], primaryScale: 1.5));
        Assert.False(ScreenGeometry.IsVisibleAtAnyScale(new Rect(-900, -900, 400, 80), [laptop, external], primaryScale: 1.5));
    }

    [Fact]
    public void Monitors_are_enumerated_in_pixels_or_the_virtual_screen_is_used()
    {
        var monitors = ScreenGeometry.MonitorsInPixels();

        Assert.NotEmpty(monitors);
        Assert.All(monitors, m => Assert.True(m.Width > 0 && m.Height > 0));
        Assert.InRange(ScreenGeometry.PrimaryScale(), 0.5, 5.0);
    }

    [Fact]
    public void Monitors_carry_device_names_and_agree_with_the_plain_enumeration()
    {
        var monitors = ScreenGeometry.Monitors();

        Assert.All(monitors, monitor =>
        {
            Assert.StartsWith(@"\\.\", monitor.DeviceName);
            Assert.True(monitor.Width > 0 && monitor.Height > 0, monitor.ToString());
        });
        if (monitors.Count > 0) // a build machine without a display enumerates none
        {
            Assert.Equal(monitors.Count, ScreenGeometry.MonitorsInPixels().Count);
        }
    }
}
