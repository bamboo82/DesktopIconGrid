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
    public void Normalize_upper_cases_valid_colors()
    {
        var settings = new LauncherSettings { BackgroundColor = "#abcdef" };

        settings.Normalize();

        Assert.Equal("#ABCDEF", settings.BackgroundColor);
    }
}
