using System.Text.Json;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Core.Storage;

namespace DesktopIconGrid.Core.Tests;

/// <summary>Restoring the bar to the monitor it was on, in device pixels.</summary>
public class WindowPlacementTests
{
    private static readonly MonitorBounds Primary = new(@"\\.\DISPLAY1", 0, 0, 1920, 1080);
    private static readonly MonitorBounds Secondary = new(@"\\.\DISPLAY2", 1920, 0, 2560, 1440);

    private static WindowPlacement Saved(int left = 100, int top = 50, int width = 400, int height = 80, string monitor = @"\\.\DISPLAY2") => new()
    {
        Left = 10, Top = 20, Width = 300, Height = 60, // WPF units from the old scale; irrelevant while the monitor exists
        Monitor = monitor, PixelLeft = left, PixelTop = top, PixelWidth = width, PixelHeight = height,
    };

    [Fact]
    public void The_bar_returns_to_the_same_place_on_its_monitor()
    {
        Assert.Equal(new PixelRect(2020, 50, 400, 80), Saved().ResolvePixels([Primary, Secondary]));
        Assert.Equal(new PixelRect(2020, 50, 400, 80), Saved(monitor: @"\\.\display2").ResolvePixels([Primary, Secondary]));
    }

    [Fact]
    public void A_bar_that_would_stick_out_is_pulled_inside_its_monitor()
    {
        Assert.Equal(new PixelRect(4080, 0, 400, 80), Saved(left: 2400, top: -10).ResolvePixels([Primary, Secondary]));
        Assert.Equal(new PixelRect(1920, 1360, 2560, 80), Saved(width: 3000, top: 1400).ResolvePixels([Primary, Secondary]));
    }

    [Fact]
    public void Without_its_monitor_or_without_pixel_data_the_caller_falls_back_to_the_unit_values()
    {
        Assert.Null(Saved().ResolvePixels([Primary]));
        Assert.Null(Saved().ResolvePixels([]));
        Assert.Null(new WindowPlacement { Left = 10, Top = 20, Width = 300, Height = 60 }.ResolvePixels([Primary, Secondary]));
        Assert.Null(Saved(width: 0).ResolvePixels([Primary, Secondary]));
        Assert.True(Saved().IsUsable);
    }

    [Fact]
    public void Pixel_data_round_trips_through_the_layout_file_format()
    {
        var json = JsonSerializer.Serialize(Saved(), LayoutStorage.JsonOptions);
        var loaded = JsonSerializer.Deserialize<WindowPlacement>(json, LayoutStorage.JsonOptions)!;

        Assert.Equal(@"\\.\DISPLAY2", loaded.Monitor);
        Assert.Equal(new PixelRect(2020, 50, 400, 80), loaded.ResolvePixels([Primary, Secondary]));

        // A file written by an older version has no pixel data and still loads.
        var old = JsonSerializer.Deserialize<WindowPlacement>("""{"left":10,"top":20,"width":300,"height":60}""", LayoutStorage.JsonOptions)!;
        Assert.True(old.IsUsable);
        Assert.False(old.HasPixels);
    }
}
