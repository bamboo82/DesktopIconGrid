using System.Windows;
using System.Windows.Media;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Services;
using DesktopIconGrid.ViewModels;

namespace DesktopIconGrid.App.Tests;

/// <summary>0.15.0 WP4b: the custom theme's hover and focus border colours in Settings and on the bar's brushes.</summary>
public sealed class ThemeColourTests
{
    private static SettingsViewModel Settings(string background = "#73806E", string? hover = null, int? strength = null, string? focus = null) =>
        new(new LauncherSettings { Theme = ThemeCatalog.CustomId, BackgroundColor = background, HoverColor = hover, HoverStrength = strength, FocusColor = focus });

    private static string Hex(Brush brush) => ((SolidColorBrush)brush).Color.ToString(System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public void Both_are_automatic_by_default_show_the_derived_colours_and_save_nothing()
    {
        var settings = Settings();

        Assert.True(settings.HoverAutomatic);
        Assert.True(settings.FocusAutomatic);
        Assert.Equal(("#FFFFFF", "#4C8BF5"), (settings.HoverColorText, settings.FocusColorText)); // the dark set's
        Assert.Equal("#FFFFFFFF", Hex(settings.HoverChipBrush));
        var saved = settings.ToSettings();
        Assert.Equal(((string?)null, (int?)null, (string?)null), (saved.HoverColor, saved.HoverStrength, saved.FocusColor));
    }

    [Fact]
    public void Unticking_starts_from_the_derived_colour_and_what_is_chosen_is_saved()
    {
        var settings = Settings();

        settings.HoverAutomatic = false;
        Assert.Equal(("#FFFFFF", 18), (settings.HoverColorText, settings.HoverStrength));
        settings.HoverColorText = "#ff0000";
        settings.HoverStrength = 50;
        settings.FocusAutomatic = false;
        settings.FocusColorText = "#FF8800";

        var saved = settings.ToSettings();
        Assert.Equal(("#FF0000", (int?)50, "#FF8800"), (saved.HoverColor, saved.HoverStrength, saved.FocusColor));
        Assert.Equal("#80FF0000", Hex(settings.PreviewPointerOverBrush)); // the preview's icon under the pointer
        Assert.Equal("#FFFF8800", Hex(settings.PreviewFocusBrush));
        Assert.Equal("#FFFF0000", Hex(settings.HoverChipBrush)); // the chip shows the colour, solid

        settings.HoverAutomatic = true; // ticked again: the derived colour, and nothing saved
        Assert.Equal("#FFFFFF", settings.HoverColorText);
        Assert.Null(settings.ToSettings().HoverColor);
        Assert.Null(settings.ToSettings().HoverStrength);
    }

    [Fact]
    public void While_automatic_the_colours_follow_the_background()
    {
        var settings = Settings();
        settings.BackgroundColor = "#F3F3F3"; // a light background: black overlays and the deeper blue

        Assert.Equal(("#000000", "#2563EB"), (settings.HoverColorText, settings.FocusColorText));
        Assert.Equal(10, settings.HoverStrength); // 8 % starts at the slider's 10
    }

    [Fact]
    public void A_chosen_focus_colour_too_close_to_the_background_is_warned_about_and_still_saved()
    {
        var settings = Settings();
        settings.FocusAutomatic = false;

        settings.FocusColorText = "#73806E"; // the background itself
        Assert.True(settings.IsFocusHardToSee);
        Assert.Equal("#73806E", settings.ToSettings().FocusColor);

        settings.FocusColorText = "#000000";
        Assert.False(settings.IsFocusHardToSee);

        settings.FocusColorText = "#73806E";
        settings.FocusAutomatic = true; // automatic is not judged
        Assert.False(settings.IsFocusHardToSee);
    }

    [Fact]
    public void A_box_that_holds_no_colour_says_so_and_saves_none()
    {
        var settings = Settings();
        settings.HoverAutomatic = false;
        settings.HoverColorText = "red";

        Assert.False(settings.IsHoverColorValid);
        var saved = settings.ToSettings();
        Assert.Null(saved.HoverColor);
        Assert.Null(saved.HoverStrength);
    }

    [Fact]
    public void Colours_from_the_layout_open_unticked_with_their_values()
    {
        var settings = Settings(hover: "#00AA00", strength: 30, focus: "#FF8800");

        Assert.False(settings.HoverAutomatic);
        Assert.False(settings.FocusAutomatic);
        Assert.Equal(("#00AA00", 30, "#FF8800"), (settings.HoverColorText, settings.HoverStrength, settings.FocusColorText));
    }

    [Fact]
    public void The_bars_brushes_carry_the_pointer_over_and_focus_colours_and_the_accent_keeps_its_own()
    {
        var palette = ThemeCatalog.Resolve(new LauncherSettings { Theme = ThemeCatalog.CustomId, HoverColor = "#FF0000", HoverStrength = 50, FocusColor = "#FF8800" });
        var resources = new ResourceDictionary();

        ThemeManager.Apply(palette, resources);

        Assert.Equal("#80FF0000", Hex((Brush)resources[ThemeManager.PointerOverKey]));
        Assert.Equal("#DEFF0000", Hex((Brush)resources[ThemeManager.PointerOverBorderKey]));
        Assert.Equal("#DEFF0000", Hex((Brush)resources[ThemeManager.PressedKey]));
        Assert.Equal("#FFFF8800", Hex((Brush)resources[ThemeManager.FocusKey]));
        Assert.Equal("#4AFF8800", Hex((Brush)resources[ThemeManager.DropTargetKey]));
        Assert.Equal("#FF4C8BF5", Hex((Brush)resources[ThemeManager.AccentKey]));
        Assert.Equal("#2EFFFFFF", Hex((Brush)resources[ThemeManager.HoverKey]));
    }
}
