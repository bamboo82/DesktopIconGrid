using System.Text.Json;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Core.Storage;

namespace DesktopIconGrid.Core.Tests;

/// <summary>0.15.0 WP4b: the custom theme's own hover colour (with its strength) and focus border colour.</summary>
public class ThemeHoverFocusTests
{
    private static LauncherSettings Custom(string background, string? hover = null, int? strength = null, string? focus = null) => new()
    {
        Theme = ThemeCatalog.CustomId,
        BackgroundColor = background,
        HoverColor = hover,
        HoverStrength = strength,
        FocusColor = focus,
    };

    [Theory]
    [InlineData("#73806E")] // dark set
    [InlineData("#F3F3F3")] // light set
    [InlineData("#2D4A6E")]
    public void Without_colours_of_its_own_the_custom_theme_is_exactly_the_derived_one(string background)
    {
        var resolved = ThemeCatalog.Resolve(Custom(background));
        var derived = ThemeCatalog.CreateCustom(background);

        Assert.Equal(derived, resolved);
        Assert.Equal((derived.Hover, derived.HoverBorder, derived.Accent), (resolved.PointerOver, resolved.PointerOverBorder, resolved.Focus));
        Assert.Equal((derived.Pressed, derived.DropTarget), (resolved.Pressed, resolved.DropTarget));
    }

    [Fact]
    public void The_hover_colour_lies_at_its_strength_and_the_outline_and_pressed_state_are_stronger_by_the_derived_ratios()
    {
        // The default background takes the dark set: hover white at 46/255, its outline and pressed state at 80.
        var palette = ThemeCatalog.Resolve(Custom("#73806E", hover: "#ff0000", strength: 50));
        Assert.Equal("#80FF0000", palette.PointerOver); // 50 % is 127.5, rounded up
        Assert.Equal("#DEFF0000", palette.PointerOverBorder); // 127.5 × 80 / 46 = 221.7
        Assert.Equal("#DEFF0000", palette.Pressed);
        Assert.Equal("#2EFFFFFF", palette.Hover); // the search box, the notice row and text tiles keep the derived fill

        var unsized = ThemeCatalog.Resolve(Custom("#73806E", hover: "#FF0000"));
        Assert.Equal(("#2EFF0000", "#50FF0000"), (unsized.PointerOver, unsized.PointerOverBorder)); // the derived overlay's own strength

        var light = ThemeCatalog.Resolve(Custom("#F3F3F3", hover: "#00AA00", strength: 100));
        Assert.Equal(("#FF00AA00", "#FF00AA00", "#FF00AA00"), (light.PointerOver, light.PointerOverBorder, light.Pressed)); // held to opaque
    }

    [Fact]
    public void The_focus_colour_borders_focus_and_fills_a_drop_target_at_the_derived_alpha_and_the_accent_stays()
    {
        var dark = ThemeCatalog.Resolve(Custom("#73806E", focus: "#ff8800"));
        Assert.Equal("#FF8800", dark.Focus);
        Assert.Equal("#4AFF8800", dark.DropTarget);
        Assert.Equal("#4C8BF5", dark.Accent); // the launch frame, the search box and the missing badge keep it

        var light = ThemeCatalog.Resolve(Custom("#F3F3F3", focus: "#FF8800"));
        Assert.Equal("#40FF8800", light.DropTarget);
    }

    [Fact]
    public void A_built_in_theme_takes_no_colours_of_its_own()
    {
        var settings = Custom("#73806E", hover: "#FF0000", strength: 50, focus: "#FF8800");
        settings.Theme = "graphite";
        Assert.Equal(ThemeCatalog.Graphite, ThemeCatalog.Resolve(settings));
    }

    [Fact]
    public void Unticking_starts_from_the_derived_colour_and_strength()
    {
        var dark = ThemeCatalog.CreateCustom("#73806E");
        Assert.Equal(("#FFFFFF", 18), (ThemeCatalog.DerivedHoverColor(dark), ThemeCatalog.DerivedHoverStrength(dark)));

        var light = ThemeCatalog.CreateCustom("#F3F3F3");
        Assert.Equal(("#000000", 10), (ThemeCatalog.DerivedHoverColor(light), ThemeCatalog.DerivedHoverStrength(light))); // 8 % starts at the slider's 10
    }

    [Fact]
    public void The_focus_colour_is_hard_to_see_under_3_to_1()
    {
        Assert.True(ThemeCatalog.IsFocusHardToSee("#73806E", "#73806E"));
        Assert.False(ThemeCatalog.IsFocusHardToSee("#000000", "#FFFFFF"));
        Assert.False(ThemeCatalog.IsFocusHardToSee("#949494", "#FFFFFF")); // 3.03 : 1
        Assert.True(ThemeCatalog.IsFocusHardToSee("#959595", "#FFFFFF")); // 2.99 : 1
    }

    [Fact]
    public void The_settings_read_back_and_what_is_not_a_colour_or_out_of_range_is_dropped_or_held()
    {
        var settings = Custom("#73806E", hover: " #abcdef ", strength: 150, focus: "#123456");
        settings.Normalize();
        Assert.Equal(("#ABCDEF", (int?)100, "#123456"), (settings.HoverColor, settings.HoverStrength, settings.FocusColor));

        var json = JsonSerializer.Serialize(settings, LayoutStorage.JsonOptions);
        using (var document = JsonDocument.Parse(json))
        {
            Assert.Equal("#ABCDEF", document.RootElement.GetProperty("hoverColor").GetString());
            Assert.Equal(100, document.RootElement.GetProperty("hoverStrength").GetInt32());
            Assert.Equal("#123456", document.RootElement.GetProperty("focusColor").GetString());
        }

        var copy = new LauncherSettings();
        copy.CopyFrom(JsonSerializer.Deserialize<LauncherSettings>(json, LayoutStorage.JsonOptions)!);
        Assert.Equal(("#ABCDEF", (int?)100, "#123456"), (copy.HoverColor, copy.HoverStrength, copy.FocusColor));

        var bad = Custom("#73806E", hover: "purple", strength: 5, focus: "#12345");
        Assert.True(bad.Normalize());
        Assert.Equal(((string?)null, (int?)10, (string?)null), (bad.HoverColor, bad.HoverStrength, bad.FocusColor));

        var none = JsonSerializer.Serialize(new LauncherSettings(), LayoutStorage.JsonOptions);
        Assert.DoesNotContain("hoverColor", none, StringComparison.Ordinal); // absent: the derived colours, as before
        Assert.DoesNotContain("focusColor", none, StringComparison.Ordinal);
    }
}
