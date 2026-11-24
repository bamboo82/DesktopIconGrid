using System.Text.Json;
using System.Text.Json.Serialization;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Core.Storage;

namespace DesktopIconGrid.Core.Tests;

/// <summary>0.16.0 WP2: where the window at the pointer sits, as the settings store and sync it.</summary>
public class PointerAnchorTests
{
    private static LauncherSettings Read(string json) => JsonSerializer.Deserialize<LauncherSettings>(json, LayoutStorage.JsonOptions)!;

    [Fact]
    public void It_is_written_only_when_set_and_read_back_beside_the_pointer_keys()
    {
        var plain = JsonSerializer.Serialize(new LauncherSettings(), LayoutStorage.JsonOptions);
        Assert.DoesNotContain("pointerAnchor", plain, StringComparison.Ordinal);

        var chosen = new LauncherSettings { ShowAtPointerHotkey = "Ctrl+Alt+P", PointerAnchor = new PointerAnchor(0, 100) };
        Assert.False(chosen.Normalize());
        var json = JsonSerializer.Serialize(chosen, LayoutStorage.JsonOptions);
        using (var document = JsonDocument.Parse(json))
        {
            var anchor = document.RootElement.GetProperty("pointerAnchor");
            Assert.Equal((0, 100), (anchor.GetProperty("x").GetInt32(), anchor.GetProperty("y").GetInt32()));
            Assert.Equal(2, anchor.EnumerateObject().Count());
        }

        Assert.Equal(new PointerAnchor(0, 100), Read(json).PointerAnchor);
        Assert.Null(Read(plain).PointerAnchor);
    }

    [Theory]
    [InlineData("{\"x\":150,\"y\":0}")] // out of range
    [InlineData("{\"x\":50,\"y\":-1}")]
    [InlineData("{\"x\":50.5,\"y\":0}")] // not a whole number
    [InlineData("{\"x\":50}")] // one missing
    [InlineData("{\"y\":50}")]
    [InlineData("{}")]
    [InlineData("{\"x\":\"50\",\"y\":0}")] // text
    [InlineData("{\"x\":{\"deep\":[1,2]},\"y\":0}")]
    [InlineData("[50,0]")]
    [InlineData("50")]
    [InlineData("\"centre\"")]
    [InlineData("true")]
    [InlineData("null")]
    public void Anything_but_two_whole_percents_is_absent_and_the_rest_is_still_read(string anchor)
    {
        var settings = Read($"{{\"iconSize\":48,\"pointerAnchor\":{anchor},\"showAtPointerHotkey\":\"Ctrl+Alt+P\"}}");
        Assert.Null(settings.PointerAnchor);
        Assert.Equal(48, settings.IconSize);
        Assert.Equal("Ctrl+Alt+P", settings.ShowAtPointerHotkey);
    }

    [Theory]
    [InlineData("{\"x\":0,\"y\":0}", 0, 0)]
    [InlineData("{\"x\":100,\"y\":100}", 100, 100)]
    [InlineData("{\"y\":0,\"x\":50,\"note\":\"kept out\"}", 50, 0)] // order and other properties do not matter
    [InlineData("{\"X\":25,\"Y\":75}", 25, 75)]
    public void Two_whole_percents_from_0_to_100_are_an_anchor(string anchor, int x, int y)
    {
        Assert.Equal(new PointerAnchor(x, y), Read($"{{\"pointerAnchor\":{anchor}}}").PointerAnchor);
    }

    [Fact]
    public void Normalising_drops_one_out_of_range_and_a_copy_keeps_it()
    {
        var settings = new LauncherSettings { PointerAnchor = new PointerAnchor(101, 50) };
        Assert.True(settings.Normalize());
        Assert.Null(settings.PointerAnchor);

        var kept = new LauncherSettings { PointerAnchor = new PointerAnchor(25, 75) };
        Assert.False(kept.Normalize());
        var copy = new LauncherSettings();
        copy.CopyFrom(kept); // what a sync or a restore brings in
        Assert.Equal(new PointerAnchor(25, 75), copy.PointerAnchor);
        Assert.Equal(new PointerAnchor(25, 75), copy.Clone().PointerAnchor);
    }

    /// <summary>The settings as 0.15.0 has them: no anchor, and what it does not know kept as it came.</summary>
    private sealed class OlderSettings
    {
        public string? ShowAtPointerHotkey { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtensionData { get; set; }
    }

    [Fact]
    public void An_older_version_keeps_it_untouched()
    {
        var json = JsonSerializer.Serialize(new LauncherSettings { ShowAtPointerHotkey = "Ctrl+Alt+P", PointerAnchor = new PointerAnchor(100, 0) }, LayoutStorage.JsonOptions);
        var older = JsonSerializer.Deserialize<OlderSettings>(json, LayoutStorage.JsonOptions)!;
        var back = Read(JsonSerializer.Serialize(older, LayoutStorage.JsonOptions));
        Assert.Equal(new PointerAnchor(100, 0), back.PointerAnchor);
        Assert.Equal("Ctrl+Alt+P", back.ShowAtPointerHotkey);
    }
}
