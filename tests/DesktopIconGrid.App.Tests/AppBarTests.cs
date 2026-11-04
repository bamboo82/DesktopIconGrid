using System.Windows;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Services;

namespace DesktopIconGrid.App.Tests;

/// <summary>The rectangle arithmetic behind docking; the shell calls themselves need a desktop.</summary>
public class AppBarTests
{
    private static readonly Rect Monitor = new(1920, 0, 2560, 1440); // a secondary monitor to the right of the primary

    [Fact]
    public void A_docked_bar_spans_its_edge_with_the_given_thickness()
    {
        Assert.Equal(new Rect(1920, 0, 2560, 72), AppBar.ProposedRect(LauncherSettings.DockTop, 72, Monitor));
        Assert.Equal(new Rect(1920, 1368, 2560, 72), AppBar.ProposedRect(LauncherSettings.DockBottom, 72, Monitor));
        Assert.Equal(new Rect(1920, 0, 96, 1440), AppBar.ProposedRect(LauncherSettings.DockLeft, 96, Monitor));
        Assert.Equal(new Rect(4384, 0, 96, 1440), AppBar.ProposedRect(LauncherSettings.DockRight, 96, Monitor));
    }

    [Fact]
    public void The_thickness_is_kept_after_the_shell_adjusts_the_rectangle()
    {
        // The taskbar sits at the bottom: for a bottom bar the shell moves the proposed strip up by 48 px and may
        // hand back a taller rectangle; the bar keeps 72 px at the inner side of what it was given.
        var granted = new Rect(1920, 1200, 2560, 192);
        Assert.Equal(new Rect(1920, 1320, 2560, 72), AppBar.KeepThickness(LauncherSettings.DockBottom, 72, granted));

        var grantedTop = new Rect(1920, 40, 2560, 300); // another bar already owns the first 40 px
        Assert.Equal(new Rect(1920, 40, 2560, 72), AppBar.KeepThickness(LauncherSettings.DockTop, 72, grantedTop));

        var grantedRight = new Rect(4000, 0, 480, 1440);
        Assert.Equal(new Rect(4384, 0, 96, 1440), AppBar.KeepThickness(LauncherSettings.DockRight, 96, grantedRight));

        var grantedLeft = new Rect(1980, 0, 480, 1440);
        Assert.Equal(new Rect(1980, 0, 96, 1440), AppBar.KeepThickness(LauncherSettings.DockLeft, 96, grantedLeft));
    }

    [Fact]
    public void An_auto_hidden_bar_collapses_to_a_two_pixel_strip_at_its_edge()
    {
        Assert.Equal(new Rect(1920, 0, 2560, 2), AppBar.Collapsed(LauncherSettings.DockTop, Monitor));
        Assert.Equal(new Rect(1920, 1438, 2560, 2), AppBar.Collapsed(LauncherSettings.DockBottom, Monitor));
        Assert.Equal(new Rect(4478, 0, 2, 1440), AppBar.Collapsed(LauncherSettings.DockRight, Monitor));
    }

    [Fact]
    public void Edges_map_to_the_shell_codes_and_their_orientation()
    {
        Assert.Equal(0u, AppBar.EdgeCode(LauncherSettings.DockLeft));
        Assert.Equal(1u, AppBar.EdgeCode(LauncherSettings.DockTop));
        Assert.Equal(2u, AppBar.EdgeCode(LauncherSettings.DockRight));
        Assert.Equal(3u, AppBar.EdgeCode(LauncherSettings.DockBottom));
        Assert.Throws<ArgumentOutOfRangeException>(() => AppBar.EdgeCode(LauncherSettings.DockNone));
        Assert.True(AppBar.IsHorizontal(LauncherSettings.DockTop));
        Assert.False(AppBar.IsHorizontal(LauncherSettings.DockLeft));
    }
}
