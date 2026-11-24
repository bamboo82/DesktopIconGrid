using System.Globalization;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Resources;
using DesktopIconGrid.ViewModels;

namespace DesktopIconGrid.App.Tests;

/// <summary>
/// 0.16.0 WP2: 指標處視窗的位置 on the Keys tab, as the dialog keeps it: the point, its picture, its boxes and the default.
/// </summary>
public class PointerAnchorSettingsTests
{
    /// <summary>Where a point in whole percent lies on the picture's canvas, as a click there would hit it.</summary>
    private static (double X, double Y) On(double x, double y) =>
        (SettingsViewModel.AnchorInset + (SettingsViewModel.AnchorSpanX * x / 100), SettingsViewModel.AnchorInset + (SettingsViewModel.AnchorSpanY * y / 100));

    private static void Click(SettingsViewModel viewModel, double x, double y)
    {
        var (left, top) = On(x, y);
        viewModel.MoveAnchorTo(left, top);
    }

    [Fact]
    public void None_chosen_shows_the_middle_of_the_top_edge_saves_nothing_and_Reset_is_off()
    {
        var viewModel = new SettingsViewModel(new LauncherSettings());
        Assert.Null(viewModel.PointerAnchor);
        Assert.False(viewModel.IsPointerAnchorSet); // 還原預設 is disabled
        Assert.Equal(new PointerAnchor(50, 0), viewModel.ShownAnchor);
        Assert.Equal(("50", "0"), (viewModel.PointerAnchorXText, viewModel.PointerAnchorYText));
        Assert.Equal(Strings.Settings_PointerAnchorDefault, viewModel.PointerAnchorStatus);
        Assert.Equal(string.Format(CultureInfo.CurrentCulture, Strings.Settings_PointerAnchorValue, 50, 0), viewModel.PointerAnchorValueText);
        Assert.True(viewModel.IsPointerAnchorValid);
        Assert.True(viewModel.CanSave);
        Assert.Null(viewModel.ToSettings().PointerAnchor);
    }

    [Fact]
    public void A_point_round_trips_through_the_settings_and_Reset_takes_it_away()
    {
        var viewModel = new SettingsViewModel(new LauncherSettings { PointerAnchor = new PointerAnchor(25, 75) });
        Assert.Equal(new PointerAnchor(25, 75), viewModel.PointerAnchor);
        Assert.Equal(("25", "75"), (viewModel.PointerAnchorXText, viewModel.PointerAnchorYText));
        Assert.True(viewModel.IsPointerAnchorSet);
        Assert.Equal(string.Empty, viewModel.PointerAnchorStatus);
        Assert.Equal(new PointerAnchor(25, 75), viewModel.ToSettings().PointerAnchor);

        Click(viewModel, 100, 100);
        Assert.Equal(new PointerAnchor(100, 100), viewModel.ToSettings().PointerAnchor);

        viewModel.ResetPointerAnchor();
        Assert.False(viewModel.IsPointerAnchorSet);
        Assert.Equal(("50", "0"), (viewModel.PointerAnchorXText, viewModel.PointerAnchorYText));
        Assert.Null(viewModel.ToSettings().PointerAnchor);

        // Set again from none, by a click on the very dot the default shows: that is a chosen point now (the top edge at the pointer).
        Click(viewModel, 50, 0);
        Assert.Equal(new PointerAnchor(50, 0), viewModel.ToSettings().PointerAnchor);
    }

    [Theory]
    [InlineData(4, 3, 0, 0)] // within 6 % of a corner dot
    [InlineData(55, 47, 50, 50)] // near the centre
    [InlineData(97, 56, 100, 50)]
    [InlineData(44, 94, 50, 100)]
    [InlineData(94, 6, 100, 0)] // 6 % each way: still snapped
    [InlineData(7, 50, 7, 50)] // 7 % away across: not snapped
    [InlineData(50, 43, 50, 43)]
    [InlineData(30, 70, 30, 70)] // between the dots
    [InlineData(33.4, 66.6, 33, 67)] // whole percents
    [InlineData(-20, 130, 0, 100)] // outside the picture: held to the window
    public void A_click_takes_the_whole_percent_under_it_and_snaps_to_a_dot_within_6_percent(double x, double y, int expectedX, int expectedY)
    {
        var viewModel = new SettingsViewModel(new LauncherSettings());
        Click(viewModel, x, y);
        Assert.Equal(new PointerAnchor(expectedX, expectedY), viewModel.PointerAnchor);
        Assert.Equal((expectedX.ToString(CultureInfo.InvariantCulture), expectedY.ToString(CultureInfo.InvariantCulture)), (viewModel.PointerAnchorXText, viewModel.PointerAnchorYText));
    }

    [Fact]
    public void The_mark_is_drawn_centred_on_the_point_and_the_edge_dots_sit_inside_the_border()
    {
        var viewModel = new SettingsViewModel(new LauncherSettings { PointerAnchor = new PointerAnchor(0, 0) });
        Assert.Equal((-4.0, -4.0), (viewModel.AnchorMarkLeft, viewModel.AnchorMarkTop)); // 3 in, less half the 14-unit ring
        viewModel.PointerAnchor = new PointerAnchor(100, 100);
        Assert.Equal((156.0, 92.0), (viewModel.AnchorMarkLeft, viewModel.AnchorMarkTop)); // the canvas is 166 × 102: 163 and 99 less 7
        viewModel.ResetPointerAnchor();
        Assert.Equal((76.0, -4.0), (viewModel.AnchorMarkLeft, viewModel.AnchorMarkTop)); // none: on the middle of the top edge
    }

    [Fact]
    public void The_arrows_move_by_1_percent_Ctrl_from_dot_to_dot_and_Home_to_the_centre()
    {
        var viewModel = new SettingsViewModel(new LauncherSettings());
        viewModel.NudgeAnchor(0, 1, dotToDot: false); // from none: the middle of the top edge, one down
        Assert.Equal(new PointerAnchor(50, 1), viewModel.PointerAnchor);

        viewModel.NudgeAnchor(-1, 0, dotToDot: false);
        Assert.Equal(new PointerAnchor(49, 1), viewModel.PointerAnchor);

        viewModel.NudgeAnchor(1, 0, dotToDot: true); // to the next dot that way
        Assert.Equal(new PointerAnchor(50, 1), viewModel.PointerAnchor);
        viewModel.NudgeAnchor(1, 1, dotToDot: true);
        Assert.Equal(new PointerAnchor(100, 50), viewModel.PointerAnchor);
        viewModel.NudgeAnchor(1, 0, dotToDot: true); // at the edge: stays
        Assert.Equal(new PointerAnchor(100, 50), viewModel.PointerAnchor);
        viewModel.NudgeAnchor(-1, -1, dotToDot: true);
        Assert.Equal(new PointerAnchor(50, 0), viewModel.PointerAnchor);
        viewModel.NudgeAnchor(0, -1, dotToDot: false); // at the top edge: stays
        Assert.Equal(new PointerAnchor(50, 0), viewModel.PointerAnchor);

        viewModel.CentreAnchor();
        Assert.Equal(new PointerAnchor(50, 50), viewModel.PointerAnchor);
        Assert.Equal(("50", "50"), (viewModel.PointerAnchorXText, viewModel.PointerAnchorYText));
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("101")]
    [InlineData("-1")]
    [InlineData("50.5")]
    [InlineData("1e2")]
    [InlineData("+5")]
    public void A_box_that_is_no_whole_number_from_0_to_100_is_shown_as_wrong_and_Save_waits(string text)
    {
        var viewModel = new SettingsViewModel(new LauncherSettings { PointerAnchor = new PointerAnchor(25, 75) });
        viewModel.PointerAnchorYText = text;

        Assert.False(viewModel.IsPointerAnchorYValid);
        Assert.False(viewModel.IsPointerAnchorValid);
        Assert.False(viewModel.CanSave);
        Assert.StartsWith("⚠ ", viewModel.KeysTab);
        Assert.Equal(string.Format(CultureInfo.CurrentCulture, Strings.Settings_FixOnTab, Strings.Settings_TabKeys), viewModel.ProblemText);
        Assert.Equal(new PointerAnchor(25, 75), viewModel.PointerAnchor); // the point is as it was

        viewModel.PointerAnchorYText = " 60 "; // a whole number again, spaces around it
        Assert.True(viewModel.IsPointerAnchorValid);
        Assert.True(viewModel.CanSave);
        Assert.Equal(new PointerAnchor(25, 60), viewModel.ToSettings().PointerAnchor);
    }

    [Fact]
    public void The_boxes_and_the_picture_set_the_same_point()
    {
        var viewModel = new SettingsViewModel(new LauncherSettings());
        viewModel.PointerAnchorXText = "0"; // from none: the other value is the one shown
        Assert.Equal(new PointerAnchor(0, 0), viewModel.PointerAnchor);
        viewModel.PointerAnchorYText = "100";
        Assert.Equal(new PointerAnchor(0, 100), viewModel.PointerAnchor);
        Assert.Equal(string.Format(CultureInfo.CurrentCulture, Strings.Settings_PointerAnchorValue, 0, 100), viewModel.PointerAnchorValueText);

        viewModel.PointerAnchorXText = "x"; // wrong, then a click on the picture puts both boxes right
        Assert.False(viewModel.CanSave);
        Click(viewModel, 75, 25);
        Assert.Equal(("75", "25"), (viewModel.PointerAnchorXText, viewModel.PointerAnchorYText));
        Assert.True(viewModel.CanSave);

        viewModel.PointerAnchorXText = "x"; // and Reset does too
        viewModel.ResetPointerAnchor();
        Assert.Equal(("50", "0"), (viewModel.PointerAnchorXText, viewModel.PointerAnchorYText));
        Assert.True(viewModel.CanSave);
        Assert.Null(viewModel.ToSettings().PointerAnchor);
    }

    [Fact]
    public void A_point_out_of_range_in_the_settings_is_none()
    {
        var viewModel = new SettingsViewModel(new LauncherSettings { PointerAnchor = new PointerAnchor(150, 0) });
        Assert.Null(viewModel.PointerAnchor);
        Assert.Null(viewModel.ToSettings().PointerAnchor);
    }
}
