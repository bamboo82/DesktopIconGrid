using DesktopIconGrid.Core.Models;

namespace DesktopIconGrid.Core.Tests;

public class LauncherSettingsTests
{
    [Theory]
    [InlineData(0, 16)]
    [InlineData(20, 16)]
    [InlineData(21, 24)]
    [InlineData(40, 32)]
    [InlineData(41, 48)]
    [InlineData(1000, 64)]
    public void NearestIconSize_snaps_to_supported_sizes(int requested, int expected)
    {
        Assert.Equal(expected, LauncherSettings.NearestIconSize(requested));
    }

    [Fact]
    public void Normalize_clamps_and_reports_changes()
    {
        var settings = new LauncherSettings { IconSize = 33, CellPadding = -1, BackgroundColor = "purple" };

        Assert.True(settings.Normalize());
        Assert.Equal(32, settings.IconSize);
        Assert.Equal(LauncherSettings.MinCellPadding, settings.CellPadding);
        Assert.Equal(LauncherSettings.DefaultBackgroundColor, settings.BackgroundColor);
        Assert.False(settings.Normalize());
    }

    [Fact]
    public void Normalize_clamps_opacity()
    {
        var settings = new LauncherSettings { Opacity = 5 };
        Assert.True(settings.Normalize());
        Assert.Equal(LauncherSettings.MinOpacity, settings.Opacity);

        settings.Opacity = 250;
        settings.Normalize();
        Assert.Equal(100, settings.Opacity);

        var copy = new LauncherSettings();
        copy.CopyFrom(new LauncherSettings { Opacity = 60 });
        Assert.Equal(60, copy.Opacity);
    }

    [Fact]
    public void Normalize_trims_hotkey_and_copies_it()
    {
        var settings = new LauncherSettings { ToggleHotkey = "  Ctrl+Alt+Space " };
        settings.Normalize();
        Assert.Equal("Ctrl+Alt+Space", settings.ToggleHotkey);

        var blank = new LauncherSettings { ToggleHotkey = "   " };
        blank.Normalize();
        Assert.Null(blank.ToggleHotkey);

        var copy = new LauncherSettings();
        copy.CopyFrom(settings);
        Assert.Equal("Ctrl+Alt+Space", copy.ToggleHotkey);
    }

    [Fact]
    public void Normalize_falls_back_to_a_solid_backdrop_and_copies_the_look_settings()
    {
        var settings = new LauncherSettings { BarBackdrop = " MICA " };
        Assert.False(settings.Normalize()); // case and spacing are not a change of meaning
        Assert.Equal(LauncherSettings.BackdropMica, settings.BarBackdrop);

        settings.BarBackdrop = "frost";
        Assert.True(settings.Normalize());
        Assert.Equal(LauncherSettings.BackdropSolid, settings.BarBackdrop);

        var copy = new LauncherSettings();
        copy.CopyFrom(new LauncherSettings { BarBackdrop = LauncherSettings.BackdropAcrylic, FluentDialogs = false, StartWithWindows = true, StartHidden = true });
        Assert.Equal(LauncherSettings.BackdropAcrylic, copy.BarBackdrop);
        Assert.False(copy.FluentDialogs);
        Assert.True(copy.StartWithWindows);
        Assert.True(copy.StartHidden);
        Assert.False(new LauncherSettings().StartWithWindows);
        Assert.True(new LauncherSettings().FluentDialogs);
        Assert.Equal(LauncherSettings.BackdropSolid, new LauncherSettings().BarBackdrop);
    }

    [Fact]
    public void Normalize_validates_the_dock_edge_and_copies_docking()
    {
        var settings = new LauncherSettings { DockEdge = " Bottom " };
        Assert.False(settings.Normalize());
        Assert.Equal(LauncherSettings.DockBottom, settings.DockEdge);

        settings.DockEdge = "middle";
        Assert.True(settings.Normalize());
        Assert.Equal(LauncherSettings.DockNone, settings.DockEdge);

        var copy = new LauncherSettings();
        copy.CopyFrom(new LauncherSettings { DockEdge = LauncherSettings.DockLeft, AutoHide = true });
        Assert.Equal(LauncherSettings.DockLeft, copy.DockEdge);
        Assert.True(copy.AutoHide);
        Assert.Equal(LauncherSettings.DockNone, new LauncherSettings().DockEdge);
    }

    [Fact]
    public void Normalize_upper_cases_valid_colors()
    {
        var settings = new LauncherSettings { BackgroundColor = "#abcdef" };

        settings.Normalize();

        Assert.Equal("#ABCDEF", settings.BackgroundColor);
    }
}
