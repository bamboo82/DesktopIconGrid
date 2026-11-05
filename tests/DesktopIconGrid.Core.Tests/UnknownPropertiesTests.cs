using System.Text.Json;
using System.Text.Json.Nodes;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Core.Storage;
using DesktopIconGrid.Core.Sync;

namespace DesktopIconGrid.Core.Tests;

/// <summary>
/// Properties a newer version writes must survive this version's load and save, here and on other PCs through
/// sync copies: otherwise a PC on an older version silently resets what a newer one set (its language, a dock
/// edge, a per-device location…) and the linked copy carries the loss everywhere.
/// </summary>
public sealed class UnknownPropertiesTests : IDisposable
{
    private const string ShortcutId = "11111111-1111-1111-1111-111111111111";
    private const string GroupId = "22222222-2222-2222-2222-222222222222";
    private const string InnerId = "33333333-3333-3333-3333-333333333333";
    private const string SpecialId = "44444444-4444-4444-4444-444444444444";

    private const string Layout = $$"""
        {
          "version": 1,
          "futureRoot": { "a": 1, "b": [true, null, "x"] },
          "settings": { "iconSize": 48, "futureSetting": "kept" },
          "window": { "left": 10, "top": 20, "width": 300, "height": 80 },
          "items": [
            {
              "$type": "shortcut", "id": "{{ShortcutId}}", "name": "Editor", "location": "C:\\ed.exe",
              "futureShortcut": [1, 2, 3],
              "textIcon": { "text": "編", "futureGlyph": "E70F" }
            },
            {
              "$type": "group", "id": "{{GroupId}}", "name": "Tools", "futureGroup": true,
              "items": [
                { "$type": "shortcut", "id": "{{InnerId}}", "name": "Inner", "location": "https://example.com", "futureInner": { "deep": "y" } }
              ]
            },
            { "$type": "special", "id": "{{SpecialId}}", "name": "CPU", "kind": "cpu-usage", "settings": { "style": "ring" }, "futureSpecial": 3.5 }
          ]
        }
        """;

    private readonly string _directory = Directory.CreateTempSubdirectory("dig-unknown-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string FilePath => Path.Combine(_directory, "layout.json");

    [Fact]
    public void A_layout_keeps_unknown_properties_at_every_level_through_load_and_save()
    {
        File.WriteAllText(FilePath, Layout);
        var storage = new LayoutStorage(FilePath);

        storage.Save(storage.Load().Layout);

        AssertLayoutKeepsUnknownProperties(JsonNode.Parse(File.ReadAllText(FilePath))!);
    }

    [Fact]
    public void The_app_s_own_round_trip_through_the_model_and_its_edits_keeps_them_too()
    {
        File.WriteAllText(FilePath, Layout);
        var storage = new LayoutStorage(FilePath);
        var model = LauncherModel.FromLayout(storage.Load().Layout);

        // What the app does between load and save: edit settings through a copy, edit an item through a copy, rename.
        var settings = model.Settings.Clone();
        settings.ShowLabels = true;
        model.ApplySettings(settings);
        var edited = (ShortcutItem)model.Find(Guid.Parse(ShortcutId))!.Clone();
        edited.Parameters = "--fast";
        model.Update(edited);
        Assert.True(model.Rename(Guid.Parse(InnerId), "Inner renamed"));
        storage.Save(model.ToLayout());

        var saved = JsonNode.Parse(File.ReadAllText(FilePath))!;
        AssertLayoutKeepsUnknownProperties(saved);
        Assert.True(saved["settings"]!["showLabels"]!.GetValue<bool>());
        Assert.Equal("--fast", saved["items"]![0]!["parameters"]!.GetValue<string>());
    }

    [Fact]
    public void A_sync_copy_keeps_unknown_properties_through_FromJson_and_ToJson()
    {
        var copy = $$"""
            {
              "format": 1, "name": "Work", "savedAt": "2026-11-05T12:00:00+00:00", "savedBy": "Laptop",
              "futureProfile": { "device": "Laptop" },
              "layout": {{Layout}}
            }
            """;

        var json = JsonNode.Parse(SyncProfile.FromJson(copy).ToJson())!;

        Assert.True(JsonNode.DeepEquals(JsonNode.Parse("""{ "device": "Laptop" }"""), json["futureProfile"]));
        AssertLayoutKeepsUnknownProperties(json["layout"]!, expectWindow: false);
    }

    [Fact]
    public void A_copy_loaded_from_sync_and_saved_again_from_this_PC_passes_them_on()
    {
        var loaded = SyncProfile.FromJson($$"""{ "name": "Work", "layout": {{Layout}} }""");
        var model = LauncherModel.FromLayout(loaded.Layout); // what loading a copy does (ReplaceLayout)

        var uploaded = SyncProfile.FromLayout("Work", model.ToLayout(), "Desktop", DateTimeOffset.UtcNow, "0.7.0", "device-1");

        AssertLayoutKeepsUnknownProperties(JsonNode.Parse(uploaded.ToJson())!["layout"]!, expectWindow: false);
    }

    [Fact]
    public void Clones_and_copies_never_share_the_unknown_properties()
    {
        var settings = new LauncherSettings { ExtensionData = new() { ["x"] = Element("1") } };
        var settingsCopy = settings.Clone();
        Assert.NotSame(settings.ExtensionData, settingsCopy.ExtensionData);
        Assert.Equal(1, settingsCopy.ExtensionData!["x"].GetInt32());

        var target = new LauncherSettings();
        target.CopyFrom(settings);
        Assert.NotSame(settings.ExtensionData, target.ExtensionData);
        Assert.Equal(1, target.ExtensionData!["x"].GetInt32());

        var group = new GroupItem
        {
            ExtensionData = new() { ["g"] = Element("true") },
            Items = [new ShortcutItem { ExtensionData = new() { ["s"] = Element("\"v\"") } }],
        };
        var groupCopy = (GroupItem)group.Clone();
        Assert.NotSame(group.ExtensionData, groupCopy.ExtensionData);
        Assert.NotSame(group.Items[0].ExtensionData, groupCopy.Items[0].ExtensionData);
        Assert.Equal("v", groupCopy.Items[0].ExtensionData!["s"].GetString());

        Assert.Null(new ShortcutItem().Clone().ExtensionData);
    }

    [Fact]
    public void The_discriminator_is_written_once_and_never_kept_as_an_unknown_property()
    {
        File.WriteAllText(FilePath, Layout);
        var storage = new LayoutStorage(FilePath);

        var layout = storage.Load().Layout;

        Assert.All(layout.Items, item => Assert.False(item.ExtensionData?.ContainsKey(LauncherItemConverter.Discriminator) ?? false));
        storage.Save(layout);
        var text = File.ReadAllText(FilePath);
        Assert.Equal(4, CountOf(text, "\"$type\"")); // three items and the one inside the group
        Assert.DoesNotContain("extensionData", text, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertLayoutKeepsUnknownProperties(JsonNode layout, bool expectWindow = true)
    {
        Same("""{ "a": 1, "b": [true, null, "x"] }""", layout["futureRoot"]);
        Same("\"kept\"", layout["settings"]!["futureSetting"]);
        Assert.Equal(48, layout["settings"]!["iconSize"]!.GetValue<int>());

        var items = layout["items"]!.AsArray();
        Same("[1, 2, 3]", items[0]!["futureShortcut"]);
        Same("\"E70F\"", items[0]!["textIcon"]!["futureGlyph"]);
        Same("true", items[1]!["futureGroup"]);
        Same("""{ "deep": "y" }""", items[1]!["items"]![0]!["futureInner"]);
        Same("3.5", items[2]!["futureSpecial"]);
        Assert.Equal("shortcut", items[0]!["$type"]!.GetValue<string>());
        Assert.Equal("group", items[1]!["$type"]!.GetValue<string>());
        Assert.Equal("special", items[2]!["$type"]!.GetValue<string>());

        if (expectWindow)
        {
            Assert.Equal(300, layout["window"]!["width"]!.GetValue<double>());
        }
        else
        {
            Assert.Null(layout["window"]);
        }
    }

    private static void Same(string expectedJson, JsonNode? actual) =>
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(expectedJson), actual), $"expected {expectedJson}, got {actual?.ToJsonString() ?? "nothing"}");

    private static JsonElement Element(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static int CountOf(string text, string value)
    {
        var count = 0;
        for (var index = text.IndexOf(value, StringComparison.Ordinal); index >= 0; index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
