using System.Text.Json;
using System.Text.Json.Serialization;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Core.Storage;

namespace DesktopIconGrid.Core.Tests;

/// <summary>0.14.0 WP2: the dialogs' and menus' own font and text size, beside the bar's.</summary>
public class DialogTextTests
{
    [Theory]
    [InlineData(null, null)] // the bar's size
    [InlineData(100, 100)] // 100 % whatever the bar's size: kept, unlike the bar's 100
    [InlineData(90, 90)]
    [InlineData(150, 150)]
    [InlineData(120, 120)]
    [InlineData(80, 90)]
    [InlineData(400, 150)]
    [InlineData(-5, 90)]
    public void The_dialogs_size_stays_between_90_and_150_percent_and_100_is_kept(int? stored, int? kept)
    {
        var settings = new LauncherSettings { DialogTextScale = stored };

        Assert.Equal(stored != kept, settings.Normalize());
        Assert.Equal(kept, settings.DialogTextScale);
    }

    [Theory]
    [InlineData(null, null, 1.0)] // as 0.13.0 with nothing set
    [InlineData(150, null, 1.5)] // the bar's
    [InlineData(150, 100, 1.0)] // the bar at 150 %, the dialogs at 100 %
    [InlineData(null, 150, 1.5)] // and the reverse
    [InlineData(90, 125, 1.25)]
    public void The_dialogs_take_their_own_size_else_the_bars(int? bar, int? dialog, double factor)
    {
        var settings = new LauncherSettings { TextScale = bar, DialogTextScale = dialog };

        Assert.Equal(factor, settings.DialogTextFactor, 3);
        Assert.Equal((bar ?? 100) / 100.0, settings.TextFactor, 3); // the bar keeps its own
    }

    [Theory]
    [InlineData(null, null, false)] // the bar's font
    [InlineData("", "", false)] // Windows' own
    [InlineData("   ", "", true)]
    [InlineData(" Calibri ", "Calibri", true)]
    [InlineData("Calibri", "Calibri", false)]
    [InlineData("file:///C:/x.ttf#Evil", "", true)] // a font from a file: never from a layout; Windows' own, as for the bar
    [InlineData("Arial, Comic Sans MS", "", true)]
    public void A_dialog_font_is_a_familys_name_or_Windows_own(string? stored, string? kept, bool changed)
    {
        var settings = new LauncherSettings { DialogFont = stored };

        Assert.Equal(changed, settings.Normalize());
        Assert.Equal(kept, settings.DialogFont);
    }

    [Theory]
    [InlineData("Calibri", null, "Calibri")] // the bar's
    [InlineData(null, null, null)] // Windows' own for both, as 0.13.0
    [InlineData("Calibri", "", null)] // Windows' own while the bar has Calibri
    [InlineData("Calibri", "Georgia", "Georgia")]
    [InlineData(null, "Georgia", "Georgia")]
    public void The_dialogs_ask_for_their_own_family_else_the_bars(string? bar, string? dialog, string? family)
    {
        Assert.Equal(family, new LauncherSettings { UiFont = bar, DialogFont = dialog }.DialogFamily);
    }

    /// <summary>The settings of 0.13.0, as that version reads and writes them: the bar's pair is known, the dialogs' is not.</summary>
    private sealed class OlderSettings
    {
        public string? UiFont { get; set; }

        public int? TextScale { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtensionData { get; set; }
    }

    [Fact]
    public void Both_are_written_only_when_chosen_read_back_sync_and_survive_an_older_version()
    {
        var plain = JsonSerializer.Serialize(new LauncherSettings(), LayoutStorage.JsonOptions);
        Assert.DoesNotContain("dialogFont", plain, StringComparison.Ordinal);
        Assert.DoesNotContain("dialogTextScale", plain, StringComparison.Ordinal);

        var chosen = new LauncherSettings { UiFont = "Calibri", TextScale = 150, DialogFont = string.Empty, DialogTextScale = 100 };
        Assert.False(chosen.Normalize());
        var json = JsonSerializer.Serialize(chosen, LayoutStorage.JsonOptions);
        var read = JsonSerializer.Deserialize<LauncherSettings>(json, LayoutStorage.JsonOptions)!;
        Assert.Equal((string.Empty, (int?)100), (read.DialogFont, read.DialogTextScale));
        Assert.Equal(("Calibri", (int?)150), (read.UiFont, read.TextScale));

        // 0.13.0 draws both with the bar's pair and writes the dialogs' back as it found them.
        var older = JsonSerializer.Deserialize<OlderSettings>(json, LayoutStorage.JsonOptions)!;
        Assert.Equal(("Calibri", (int?)150), (older.UiFont, older.TextScale));
        Assert.Equal(string.Empty, older.ExtensionData!["dialogFont"].GetString());
        Assert.Equal(100, older.ExtensionData["dialogTextScale"].GetInt32());
        var back = JsonSerializer.Deserialize<LauncherSettings>(JsonSerializer.Serialize(older, LayoutStorage.JsonOptions), LayoutStorage.JsonOptions)!;
        Assert.Equal((string.Empty, (int?)100), (back.DialogFont, back.DialogTextScale));

        // What a sync or a restore brings in is copied whole.
        var copy = new LauncherSettings();
        copy.CopyFrom(new LauncherSettings { DialogFont = "Georgia", DialogTextScale = 125 });
        Assert.Equal(("Georgia", (int?)125), (copy.DialogFont, copy.DialogTextScale));
        Assert.Equal(("Georgia", (int?)125), (copy.Clone().DialogFont, copy.Clone().DialogTextScale));
    }

    [Theory]
    [InlineData("Arial", null, "Arial", "Arial", "")] // the dialogs take the bar's
    [InlineData("Gone", null, null, null, "Gone:Bar")] // missing: Windows' own for both, told once, for the bar
    [InlineData("Arial", "Gone", "Arial", null, "Gone:Dialogs")]
    [InlineData("Gone", "", null, null, "Gone:Bar")] // Windows' own for the dialogs: nothing to find
    [InlineData("Gone", "Georgia", null, "Georgia", "Gone:Bar")]
    [InlineData(null, "Georgia", null, "Georgia", "")]
    [InlineData("Gone", "Gone", null, null, "Gone:Bar Gone:Dialogs")] // the same family chosen for both: told for each
    public void A_family_this_PC_lacks_is_Windows_own_and_told_of_for_whose_text(string? bar, string? dialogs, string? barFound, string? dialogsFound, string told)
    {
        var settings = new LauncherSettings { UiFont = bar, DialogFont = dialogs };
        var missing = new List<string>();

        var found = Fonts.UiFontChain.Found(settings, family => family != "Gone", (family, use) => missing.Add($"{family}:{use}"));

        Assert.Equal((barFound, dialogsFound), found);
        Assert.Equal(told, string.Join(' ', missing));
    }
}
