using System.Text.Json;
using System.Text.Json.Serialization;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Core.Storage;

namespace DesktopIconGrid.Core.Tests;

/// <summary>0.17.0 WP1: 不在工作列與 Alt+Tab 中顯示工具列 is a setting of the bar's, off by default, saved and synced with the others.</summary>
public class HideFromTaskbarSettingTests
{
    private static LauncherSettings Read(string json) => JsonSerializer.Deserialize<LauncherSettings>(json, LayoutStorage.JsonOptions)!;

    [Fact]
    public void It_is_off_by_default_written_only_when_on_and_read_back()
    {
        Assert.False(new LauncherSettings().HideFromTaskbar);

        var plain = JsonSerializer.Serialize(new LauncherSettings(), LayoutStorage.JsonOptions);
        Assert.DoesNotContain("hideFromTaskbar", plain, StringComparison.Ordinal);
        Assert.False(Read(plain).HideFromTaskbar);

        var json = JsonSerializer.Serialize(new LauncherSettings { HideFromTaskbar = true }, LayoutStorage.JsonOptions);
        using (var document = JsonDocument.Parse(json))
        {
            Assert.True(document.RootElement.GetProperty("hideFromTaskbar").GetBoolean());
        }

        Assert.True(Read(json).HideFromTaskbar);
    }

    [Fact]
    public void It_is_copied_as_sync_copies_settings()
    {
        var copy = new LauncherSettings();
        copy.CopyFrom(new LauncherSettings { HideFromTaskbar = true });
        Assert.True(copy.HideFromTaskbar);
        Assert.True(copy.Clone().HideFromTaskbar);

        copy.CopyFrom(new LauncherSettings()); // another PC has the bar on the taskbar
        Assert.False(copy.HideFromTaskbar);
    }

    /// <summary>The settings as 0.16.1 has them: no such setting, and what it does not know kept as it came.</summary>
    private sealed class OlderSettings
    {
        public bool CloseToTray { get; set; } = true;

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtensionData { get; set; }
    }

    [Fact]
    public void An_older_version_keeps_it_untouched()
    {
        var json = JsonSerializer.Serialize(new LauncherSettings { HideFromTaskbar = true, CloseToTray = false }, LayoutStorage.JsonOptions);
        var older = JsonSerializer.Deserialize<OlderSettings>(json, LayoutStorage.JsonOptions)!;
        var back = Read(JsonSerializer.Serialize(older, LayoutStorage.JsonOptions));
        Assert.True(back.HideFromTaskbar);
        Assert.False(back.CloseToTray);
    }
}
