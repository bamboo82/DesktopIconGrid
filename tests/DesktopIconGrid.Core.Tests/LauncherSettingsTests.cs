using DesktopIconGrid.Core.Models;

namespace DesktopIconGrid.Core.Tests;

public class LauncherSettingsTests
{
    [Theory]
    [InlineData(15, 16)]
    [InlineData(0, 16)]
    [InlineData(300, 256)]
    [InlineData(50, 50)]
    [InlineData(72, 72)]
    [InlineData(16, 16)]
    [InlineData(256, 256)]
    public void An_icon_size_is_held_to_16_to_256_and_not_snapped_to_the_list(int requested, int expected)
    {
        // 0.15.0 WP8: before, 50 snapped to 48.
        Assert.Equal(expected, LauncherSettings.ClampIconSize(requested));
        var settings = new LauncherSettings { IconSize = requested };
        Assert.Equal(requested != expected, settings.Normalize());
        Assert.Equal(expected, settings.IconSize);
    }

    [Theory]
    [InlineData("72", 72)]
    [InlineData("72 px", 72)]
    [InlineData(" 96PX ", 96)]
    [InlineData("300", 256)]
    [InlineData("99999999999", 256)]
    [InlineData("0", 16)]
    [InlineData("-5", 16)]
    [InlineData("７２", 72)] // as a Chinese input method types it
    [InlineData("abc", 40)]
    [InlineData("", 40)]
    [InlineData("72.5", 40)]
    [InlineData("px", 40)]
    public void The_icon_size_box_takes_a_whole_number_to_the_nearer_end_and_anything_else_gives_the_size_before(string text, int expected)
    {
        Assert.Equal(expected, LauncherSettings.ParseIconSize(text, previous: 40));
    }

    [Fact]
    public void Settings_list_thirteen_icon_sizes_from_16_to_128()
    {
        Assert.Equal([16, 20, 24, 28, 32, 40, 48, 56, 64, 72, 80, 96, 128], LauncherSettings.IconSizes);
    }

    [Fact]
    public void Normalize_clamps_and_reports_changes()
    {
        var settings = new LauncherSettings { IconSize = 300, CellPadding = -1, BackgroundColor = "purple" };

        Assert.True(settings.Normalize());
        Assert.Equal(256, settings.IconSize);
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
        var settings = new LauncherSettings { ToggleHotkey = "  Ctrl+Alt+Space ", ShowAtPointerHotkey = " Win+Alt+Space  " };
        settings.Normalize();
        Assert.Equal("Ctrl+Alt+Space", settings.ToggleHotkey);
        Assert.Equal("Win+Alt+Space", settings.ShowAtPointerHotkey);

        var blank = new LauncherSettings { ToggleHotkey = "   ", ShowAtPointerHotkey = "" };
        blank.Normalize();
        Assert.Null(blank.ToggleHotkey);
        Assert.Null(blank.ShowAtPointerHotkey);
        Assert.Null(new LauncherSettings().ShowAtPointerHotkey); // off until the user picks one

        var copy = new LauncherSettings();
        copy.CopyFrom(settings);
        Assert.Equal("Ctrl+Alt+Space", copy.ToggleHotkey);
        Assert.Equal("Win+Alt+Space", copy.ShowAtPointerHotkey);
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
