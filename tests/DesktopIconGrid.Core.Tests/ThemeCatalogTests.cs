using DesktopIconGrid.Core.Models;

namespace DesktopIconGrid.Core.Tests;

public class ThemeCatalogTests
{
    [Fact]
    public void Builtin_themes_have_unique_ids_and_valid_colours()
    {
        var ids = ThemeCatalog.Builtin.Select(t => t.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains(ThemeCatalog.ClassicId, ids);

        foreach (var theme in ThemeCatalog.Builtin)
        {
            foreach (var colour in new[] { theme.Background, theme.Border, theme.Foreground, theme.Hover, theme.HoverBorder, theme.Pressed, theme.Grip, theme.Accent, theme.DropTarget })
            {
                Assert.Matches("^#[0-9A-F]{6}([0-9A-F]{2})?$", colour);
            }

            Assert.False(theme.IsCustom);
        }
    }

    [Fact]
    public void Find_is_case_insensitive_and_tolerant()
    {
        Assert.Same(ThemeCatalog.Graphite, ThemeCatalog.Find(" Graphite "));
        Assert.Null(ThemeCatalog.Find("neon"));
        Assert.Null(ThemeCatalog.Find(null));
        Assert.True(ThemeCatalog.IsKnown("custom"));
        Assert.False(ThemeCatalog.IsKnown("neon"));
    }

    [Fact]
    public void Resolve_falls_back_to_classic_and_builds_custom_from_background()
    {
        Assert.Same(ThemeCatalog.Classic, ThemeCatalog.Resolve(new LauncherSettings { Theme = "unknown" }));
        Assert.Same(ThemeCatalog.Midnight, ThemeCatalog.Resolve(new LauncherSettings { Theme = "MIDNIGHT" }));

        var custom = ThemeCatalog.Resolve(new LauncherSettings { Theme = ThemeCatalog.CustomId, BackgroundColor = "#123456" });
        Assert.True(custom.IsCustom);
        Assert.Equal("#123456", custom.Background);
    }

    [Fact]
    public void Custom_palette_adapts_to_light_and_dark_backgrounds()
    {
        var dark = ThemeCatalog.CreateCustom("#202020");
        var light = ThemeCatalog.CreateCustom("#f0f0f0");

        Assert.Equal("#FFFFFF", dark.Foreground);
        Assert.Equal("#202020", light.Foreground);
        Assert.Equal("#F0F0F0", light.Background);
        Assert.Equal("#787878", light.Border);
        Assert.NotEqual(dark.Hover, light.Hover);
        Assert.Equal(LauncherSettings.DefaultBackgroundColor, ThemeCatalog.CreateCustom("nope").Background);
    }

    [Theory]
    [InlineData("#000000", 0.0)]
    [InlineData("#FFFFFF", 1.0)]
    [InlineData("#FF0000", 0.299)]
    public void Luminance_is_computed(string hex, double expected)
    {
        Assert.Equal(expected, ThemeCatalog.Luminance(hex), 3);
    }

    [Fact]
    public void Settings_without_theme_migrate_to_custom_when_colour_was_changed()
    {
        var untouched = new LauncherSettings { Theme = null };
        untouched.Normalize();
        Assert.Equal(ThemeCatalog.ClassicId, untouched.Theme);

        var recoloured = new LauncherSettings { Theme = null, BackgroundColor = "#123456" };
        recoloured.Normalize();
        Assert.Equal(ThemeCatalog.CustomId, recoloured.Theme);

        var unknown = new LauncherSettings { Theme = "neon" };
        Assert.True(unknown.Normalize());
        Assert.Equal(ThemeCatalog.ClassicId, unknown.Theme);
    }
}
