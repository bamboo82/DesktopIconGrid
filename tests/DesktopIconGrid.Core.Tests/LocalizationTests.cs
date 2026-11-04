using System.Collections;
using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Core.Resources;
using DesktopIconGrid.Core.SpecialItems;

namespace DesktopIconGrid.Core.Tests;

/// <summary>The language setting, the culture it selects, and the translated names of the ready-made items.</summary>
public class LocalizationTests
{
    [Fact]
    public void Normalize_canonicalises_the_language_and_falls_back_to_the_system_language()
    {
        var settings = new LauncherSettings { Language = " zh-tw " };
        settings.Normalize();
        Assert.Equal(LauncherSettings.LanguageTraditionalChinese, settings.Language);

        settings.Language = "klingon";
        Assert.True(settings.Normalize());
        Assert.Equal(LauncherSettings.LanguageSystem, settings.Language);
        Assert.False(settings.Normalize());

        var copy = new LauncherSettings();
        copy.CopyFrom(new LauncherSettings { Language = LauncherSettings.LanguageSimplifiedChinese });
        Assert.Equal(LauncherSettings.LanguageSimplifiedChinese, copy.Language);
    }

    [Fact]
    public void The_system_language_keeps_the_Windows_display_language()
    {
        Assert.Null(AppLanguage.CultureFor(LauncherSettings.LanguageSystem));
        Assert.Null(AppLanguage.CultureFor(null));
        Assert.Null(AppLanguage.CultureFor("  "));
        Assert.Equal("zh-TW", AppLanguage.CultureFor("zh-TW")!.Name);
        Assert.Equal("en", AppLanguage.CultureFor(" en ")!.Name);
    }

    [Theory]
    [InlineData("zh-TW", true, false)]
    [InlineData("zh-HK", true, false)]
    [InlineData("zh-Hant", true, false)]
    [InlineData("zh-CN", false, true)]
    [InlineData("zh-SG", false, true)]
    [InlineData("zh", false, true)]
    [InlineData("en-US", false, false)]
    [InlineData("ja-JP", false, false)]
    public void Chinese_scripts_are_told_apart_through_the_parent_chain(string name, bool traditional, bool simplified)
    {
        var culture = CultureInfo.GetCultureInfo(name);
        Assert.Equal(traditional, AppLanguage.IsTraditionalChinese(culture));
        Assert.Equal(simplified, AppLanguage.IsSimplifiedChinese(culture));
    }

    [Fact]
    public void Text_icons_default_to_PMingLiU_for_Traditional_Chinese_and_SimSun_otherwise()
    {
        Assert.StartsWith("PMingLiU", TextIcon.DefaultFontFamilyFor(CultureInfo.GetCultureInfo("zh-TW")), StringComparison.Ordinal);
        Assert.StartsWith("PMingLiU", TextIcon.DefaultFontFamilyFor(CultureInfo.GetCultureInfo("zh-HK")), StringComparison.Ordinal);
        Assert.Equal(TextIcon.DefaultFontFamily, TextIcon.DefaultFontFamilyFor(CultureInfo.GetCultureInfo("zh-CN")));
        Assert.Equal(TextIcon.DefaultFontFamily, TextIcon.DefaultFontFamilyFor(CultureInfo.GetCultureInfo("en-US")));
        Assert.Equal(TextIcon.DefaultFontFamily, TextIcon.DefaultFontFamilyFor(null));
        Assert.StartsWith("SimSun", TextIcon.DefaultFontFamily, StringComparison.Ordinal);
    }

    [Fact]
    public void Ready_made_items_are_named_in_the_UI_language()
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("zh-TW");
            var system = SpecialItemCatalog.Sections[0];
            Assert.Equal("系統", system.Title);
            Assert.Equal("本機", system.Items[0].Name);
            Assert.Equal("shell:MyComputerFolder", system.Items[0].Location);

            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("zh-CN");
            Assert.Equal("此电脑", SpecialItemCatalog.Sections[0].Items[0].Name);

            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
            Assert.Equal("This PC", SpecialItemCatalog.Sections[0].Items[0].Name);
            Assert.Equal(4, SpecialItemCatalog.Sections.Count);
            Assert.Equal(22, SpecialItemCatalog.All.Count());
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [Theory]
    [InlineData("zh-Hant")]
    [InlineData("zh-Hans")]
    public void Every_core_string_is_translated_with_the_same_placeholders(string cultureName)
    {
        var culture = CultureInfo.GetCultureInfo(cultureName);
        var neutral = CoreStrings.ResourceManager.GetResourceSet(CultureInfo.InvariantCulture, true, false)!;
        var translated = CoreStrings.ResourceManager.GetResourceSet(culture, true, false);
        Assert.NotNull(translated);
        foreach (DictionaryEntry entry in neutral)
        {
            var key = (string)entry.Key;
            var text = translated!.GetString(key);
            Assert.False(string.IsNullOrWhiteSpace(text), $"{key} has no {cultureName} text");
            Assert.Equal(Placeholders((string)entry.Value!), Placeholders(text!));
        }
    }

    private static string Placeholders(string text) =>
        string.Join(",", Regex.Matches(text, @"\{\d+\}").Select(m => m.Value).Order());
}
