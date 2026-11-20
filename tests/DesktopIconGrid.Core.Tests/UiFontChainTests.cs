using DesktopIconGrid.Core.Fonts;
using DesktopIconGrid.Core.Models;

namespace DesktopIconGrid.Core.Tests;

/// <summary>0.13.0 WP4: the font of the app's text, and its size.</summary>
public class UiFontChainTests
{
    private const string Taipei = "file:///C:/Users/me/AppData/Local/Microsoft/Windows/Fonts/TaipeiSansTCBeta-Light.ttf#Taipei Sans TC Beta";

    [Theory]
    [InlineData(null, null, "Segoe UI")] // Windows' font, Chinese as Windows draws it: as before 0.12.0
    [InlineData(null, Taipei, "Segoe UI, " + Taipei)] // as 0.12.0 made it
    [InlineData("Calibri", null, "Calibri, Segoe UI")]
    [InlineData("Calibri", Taipei, "Calibri, " + Taipei + ", Segoe UI")]
    [InlineData("  Calibri ", Taipei, "Calibri, " + Taipei + ", Segoe UI")]
    [InlineData("segoe ui", null, "segoe ui")] // Windows' own chosen by name: once
    public void The_chosen_font_comes_first_then_the_Chinese_one_then_Windows_own(string? chosen, string? chinese, string chain)
    {
        Assert.Equal(chain, UiFontChain.Build(chosen, chinese, "Segoe UI"));
    }

    [Theory]
    [InlineData("Calibri", true)]
    [InlineData("Microsoft JhengHei UI", true)]
    [InlineData("微軟正黑體", true)]
    [InlineData("", false)]
    [InlineData("Arial, Comic Sans MS", false)] // two families
    [InlineData("file:///C:/x.ttf#Evil", false)] // a font from a file: never from a layout
    [InlineData(@"\\server\share\x.ttf#Evil", false)]
    [InlineData("Tab\tName", false)]
    public void Only_a_plain_family_name_is_taken(string name, bool taken)
    {
        Assert.Equal(taken, UiFontChain.IsFamilyName(name));
        Assert.False(UiFontChain.IsFamilyName(new string('x', UiFontChain.MaxNameLength + 1)));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(100, null)] // the default: stored as nothing
    [InlineData(90, 90)]
    [InlineData(125, 125)]
    [InlineData(150, 150)]
    [InlineData(120, 120)] // between the offered sizes: kept
    [InlineData(80, 90)]
    [InlineData(400, 150)]
    [InlineData(-5, 90)]
    public void The_text_size_stays_between_90_and_150_percent(int? stored, int? kept)
    {
        var settings = new LauncherSettings { TextScale = stored };
        settings.Normalize();

        Assert.Equal(kept, settings.TextScale);
        Assert.Equal((kept ?? 100) / 100.0, settings.TextFactor);
    }

    [Fact]
    public void A_font_that_is_no_familys_name_becomes_Windows_own_and_both_settings_copy_and_clone()
    {
        var settings = new LauncherSettings { UiFont = "file:///C:/x.ttf#Evil", TextScale = 125 };
        Assert.True(settings.Normalize());
        Assert.Null(settings.UiFont);

        var source = new LauncherSettings { UiFont = " Calibri ", TextScale = 110 };
        var copy = new LauncherSettings();
        copy.CopyFrom(source);
        Assert.Equal(("Calibri", 110), (copy.UiFont, copy.TextScale));
        Assert.Equal(("Calibri", 110), (copy.Clone().UiFont, copy.Clone().TextScale));
    }
}
