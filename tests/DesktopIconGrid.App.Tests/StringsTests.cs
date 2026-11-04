using System.Collections;
using System.Globalization;
using System.Text.RegularExpressions;
using DesktopIconGrid.Resources;

namespace DesktopIconGrid.App.Tests;

/// <summary>The translated string tables: complete, with the same format placeholders, and reachable from the regional cultures.</summary>
public class StringsTests
{
    [Theory]
    [InlineData("zh-Hant")]
    [InlineData("zh-Hans")]
    public void Every_string_is_translated_with_the_same_placeholders(string cultureName)
    {
        var culture = CultureInfo.GetCultureInfo(cultureName);
        var neutral = Strings.ResourceManager.GetResourceSet(CultureInfo.InvariantCulture, true, false)!;
        var translated = Strings.ResourceManager.GetResourceSet(culture, true, false);
        Assert.NotNull(translated);
        var count = 0;
        foreach (DictionaryEntry entry in neutral)
        {
            var key = (string)entry.Key;
            var text = translated!.GetString(key);
            Assert.False(string.IsNullOrWhiteSpace(text), $"{key} has no {cultureName} text");
            Assert.Equal(Placeholders((string)entry.Value!), Placeholders(text!));
            count++;
        }

        Assert.True(count > 300, $"only {count} strings");
    }

    [Fact]
    public void Regional_cultures_fall_back_to_their_script()
    {
        Assert.Equal("取消", Strings.ResourceManager.GetString("Common_Cancel", CultureInfo.GetCultureInfo("zh-TW")));
        Assert.Equal("取消", Strings.ResourceManager.GetString("Common_Cancel", CultureInfo.GetCultureInfo("zh-HK")));
        Assert.Equal("儲存", Strings.ResourceManager.GetString("Common_Save", CultureInfo.GetCultureInfo("zh-TW")));
        Assert.Equal("保存", Strings.ResourceManager.GetString("Common_Save", CultureInfo.GetCultureInfo("zh-CN")));
        Assert.Equal("Save", Strings.ResourceManager.GetString("Common_Save", CultureInfo.GetCultureInfo("en-GB")));
        Assert.Equal("Save", Strings.ResourceManager.GetString("Common_Save", CultureInfo.GetCultureInfo("ja-JP")));
    }

    private static string Placeholders(string text) =>
        string.Join(",", Regex.Matches(text, @"\{\d+\}").Select(m => m.Value).Order());
}
