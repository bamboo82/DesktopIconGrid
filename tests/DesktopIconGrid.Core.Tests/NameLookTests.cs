using System.Text.Json;
using System.Text.Json.Serialization;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Core.SpecialIcons;
using DesktopIconGrid.Core.Storage;
using DesktopIconGrid.Core.Sync;

namespace DesktopIconGrid.Core.Tests;

/// <summary>0.16.0 WP4: a widget's name has a look of its own, stored and synced with the item.</summary>
public sealed class NameLookTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "DesktopIconGrid.Tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static readonly TextLook Name = new() { Font = "Georgia", Size = 14, Bold = true, Color = "#FFEE00", TileColor = "#2D4A6E", OffsetY = 10 };

    [Fact]
    public void A_widget_without_one_writes_nothing_and_one_with_one_writes_it_beside_its_text_look()
    {
        var plain = JsonSerializer.Serialize<LauncherItem>(new SpecialIconItem { Kind = SpecialIconKinds.Clock, Name = "Clock" }, LayoutStorage.JsonOptions);
        Assert.DoesNotContain("nameLook", plain, StringComparison.Ordinal);

        var json = JsonSerializer.Serialize<LauncherItem>(
            new SpecialIconItem { Kind = SpecialIconKinds.Clock, Name = "Clock", TextLook = new TextLook { Color = "#00FF00" }, NameLook = Name }, LayoutStorage.JsonOptions);
        using var document = JsonDocument.Parse(json);
        Assert.Equal("#FFEE00", document.RootElement.GetProperty("nameLook").GetProperty("color").GetString());
        Assert.Equal("#00FF00", document.RootElement.GetProperty("textLook").GetProperty("color").GetString());
        var read = Assert.IsType<SpecialIconItem>(JsonSerializer.Deserialize<LauncherItem>(json, LayoutStorage.JsonOptions));
        Assert.Equal(Name, read.NameLook);
        Assert.Equal(new TextLook { Color = "#00FF00" }, read.TextLook);
    }

    [Fact]
    public void It_survives_the_layout_file_the_model_copies_and_a_sync_copy()
    {
        var storage = new LayoutStorage(Path.Combine(_directory, "layout.json"));
        var cpu = new SpecialIconItem { Kind = SpecialIconKinds.CpuUsage, Name = "CPU", NameLook = Name };
        storage.Save(new LauncherLayout { Items = [cpu] });

        var loaded = storage.Load().Layout;
        Assert.Equal(Name, Assert.IsType<SpecialIconItem>(loaded.Items[0]).NameLook);

        var model = LauncherModel.FromLayout(loaded);
        Assert.Equal(Name, ((SpecialIconItem)model.Items[0].Clone()).NameLook);
        var target = new SpecialIconItem { Kind = SpecialIconKinds.CpuUsage };
        target.CopyContentFrom(model.Items[0]);
        Assert.Equal(Name, target.NameLook);

        var other = new SpecialIconItem();
        other.CopyContentFrom(new SpecialIconItem { Kind = SpecialIconKinds.CpuUsage, NameLook = new TextLook { Color = "nope" } });
        Assert.Null(other.NameLook); // a copy is tidied as it is taken

        // Through sync: the copy another PC downloads has it as it was.
        var profile = SyncProfile.FromLayout("Work", model.ToLayout(), "PC-1", DateTimeOffset.UtcNow, "0.16.0");
        var arrived = SyncProfile.FromJson(profile.ToJson());
        Assert.Equal(Name, Assert.IsType<SpecialIconItem>(arrived.Layout.Items[0]).NameLook);
    }

    [Fact]
    public void A_name_look_with_bad_values_is_tidied_at_load_and_what_a_newer_version_wrote_is_kept()
    {
        const string Json = """
            {"version":1,"items":[
              {"$type":"special","kind":"clock","name":"Clock","nameLook":{"color":"#ff0000","size":1000,"offsetY":2.5,"glow":3}},
              {"$type":"special","kind":"cpu-usage","name":"CPU","nameLook":{"color":"x","tileColor":"blue"}}
            ]}
            """;
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "layout.json");
        File.WriteAllText(path, Json);

        var model = LauncherModel.FromLayout(new LayoutStorage(path).Load().Layout);

        var clockLook = Assert.IsType<SpecialIconItem>(model.Items[0]).NameLook!;
        Assert.Equal(("#FF0000", (double?)TextIcon.MaxFontSize, 0), (clockLook.Color, clockLook.Size, clockLook.OffsetY));
        Assert.Equal(3, clockLook.ExtensionData!["glow"].GetInt32());
        Assert.Null(Assert.IsType<SpecialIconItem>(model.Items[1]).NameLook); // nothing valid is no look
    }

    /// <summary>A widget as 0.15.0 reads it: no name look, and what it does not know kept as it came.</summary>
    private sealed class OlderWidget
    {
        public string? Kind { get; set; }

        public string? Name { get; set; }

        public TextLook? TextLook { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtensionData { get; set; }
    }

    [Fact]
    public void An_older_version_keeps_it_untouched_when_it_loads_and_saves()
    {
        var json = JsonSerializer.Serialize(new SpecialIconItem { Kind = SpecialIconKinds.Clock, Name = "Clock", NameLook = Name }, LayoutStorage.JsonOptions);
        var older = JsonSerializer.Deserialize<OlderWidget>(json, LayoutStorage.JsonOptions)!;
        Assert.True(older.ExtensionData!.ContainsKey("nameLook"));

        var back = JsonSerializer.Deserialize<SpecialIconItem>(JsonSerializer.Serialize(older, LayoutStorage.JsonOptions), LayoutStorage.JsonOptions)!;
        Assert.Equal(Name, back.NameLook);
        Assert.Equal("Clock", back.Name);
    }
}
