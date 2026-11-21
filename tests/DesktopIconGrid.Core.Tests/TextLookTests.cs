using System.Text.Json;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Core.SpecialIcons;
using DesktopIconGrid.Core.Storage;

namespace DesktopIconGrid.Core.Tests;

/// <summary>0.15.0 WP7b: the look of a row line's caption and of a widget's text.</summary>
public sealed class TextLookTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "DesktopIconGrid.Tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static readonly TextLook Full = new()
    {
        Font = "Segoe UI",
        Size = 16,
        Bold = true,
        Color = "#FFEE00",
        TileColor = "#2D4A6E",
        OffsetY = -10,
    };

    [Fact]
    public void Nothing_set_is_no_look_and_an_item_without_one_writes_nothing()
    {
        Assert.Null(new TextLook().Normalized());
        Assert.Null(new TextLook { Font = "  ", Size = double.NaN, Color = "red", TileColor = "#123", OffsetY = 25 }.Normalized());

        var json = JsonSerializer.Serialize<LauncherItem>(new SeparatorItem { Kind = SeparatorItem.RowLine, Name = "工作" }, LayoutStorage.JsonOptions);
        Assert.DoesNotContain("captionLook", json, StringComparison.Ordinal);
        json = JsonSerializer.Serialize<LauncherItem>(new SpecialIconItem { Kind = SpecialIconKinds.Clock, Name = "Clock" }, LayoutStorage.JsonOptions);
        Assert.DoesNotContain("textLook", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Normalized_trims_holds_the_size_to_its_range_and_drops_what_is_not_valid()
    {
        var look = new TextLook { Font = "  Segoe UI ", Size = 500, Bold = false, Color = "#abcdef", TileColor = "blue", OffsetY = 20 }.Normalized()!;

        Assert.Equal(new TextLook { Font = "Segoe UI", Size = TextIcon.MaxFontSize, Bold = false, Color = "#ABCDEF", OffsetY = 20 }, look);
        Assert.Equal(TextIcon.MinFontSize, new TextLook { Size = 1 }.Normalized()!.Size);
        Assert.False(look.Bold); // not bold is a choice of its own: a widget drawn bold today can be drawn plain
        Assert.Equal(0, new TextLook { Color = "#000000", OffsetY = -21 }.Normalized()!.OffsetY);
    }

    [Fact]
    public void Only_what_is_set_is_written_and_it_is_read_back()
    {
        var json = JsonSerializer.Serialize(new TextLook { Color = "#FF0000" }, LayoutStorage.JsonOptions);
        Assert.Equal("""{"color":"#FF0000"}""", json.Replace(" ", string.Empty, StringComparison.Ordinal).Replace("\n", string.Empty, StringComparison.Ordinal).Replace("\r", string.Empty, StringComparison.Ordinal));

        var full = JsonSerializer.Serialize(Full, LayoutStorage.JsonOptions);
        using var document = JsonDocument.Parse(full);
        Assert.Equal(
            ["font", "size", "bold", "color", "tileColor", "offsetY"],
            document.RootElement.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal(Full, JsonSerializer.Deserialize<TextLook>(full, LayoutStorage.JsonOptions));
    }

    [Fact]
    public void A_row_lines_caption_look_and_a_widgets_text_look_survive_the_layout_file_the_model_and_copies()
    {
        var storage = new LayoutStorage(Path.Combine(_directory, "layout.json"));
        var line = new SeparatorItem { Kind = SeparatorItem.RowLine, Name = "工作", CaptionLook = Full };
        var clock = new SpecialIconItem { Kind = SpecialIconKinds.Clock, Name = "Clock", TextLook = new TextLook { Color = "#00FF00", OffsetY = 5 } };
        storage.Save(new LauncherLayout { Items = [line, clock] });

        var loaded = storage.Load().Layout;
        Assert.Equal(Full, Assert.IsType<SeparatorItem>(loaded.Items[0]).CaptionLook);
        Assert.Equal(clock.TextLook, Assert.IsType<SpecialIconItem>(loaded.Items[1]).TextLook);

        var model = LauncherModel.FromLayout(loaded);
        var copy = (SeparatorItem)model.Items[0].Clone();
        Assert.Equal(Full, copy.CaptionLook);
        var target = new SpecialIconItem { Kind = SpecialIconKinds.Clock };
        target.CopyContentFrom(model.Items[1]);
        Assert.Equal(clock.TextLook, target.TextLook);

        var other = new SeparatorItem();
        other.CopyContentFrom(new SeparatorItem { Kind = SeparatorItem.RowLine, CaptionLook = new TextLook { Color = "nope" } });
        Assert.Null(other.CaptionLook); // a copy is tidied as it is taken
    }

    [Fact]
    public void A_look_with_bad_values_is_tidied_at_load_and_what_a_newer_version_wrote_is_kept()
    {
        const string Json = """
            {"version":7,"items":[
              {"$type":"separator","kind":"rowline","name":"工作","captionLook":{"color":"blue","size":1000,"offsetY":2.5}},
              {"$type":"special","kind":"clock","name":"Clock","textLook":{"color":"#ff0000","glow":3}},
              {"$type":"special","kind":"cpu-usage","name":"CPU","textLook":{"color":"x"}}
            ]}
            """;
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "layout.json");
        File.WriteAllText(path, Json);

        var model = LauncherModel.FromLayout(new LayoutStorage(path).Load().Layout);

        Assert.Equal(new TextLook { Size = TextIcon.MaxFontSize }, Assert.IsType<SeparatorItem>(model.Items[0]).CaptionLook);
        var clockLook = Assert.IsType<SpecialIconItem>(model.Items[1]).TextLook!;
        Assert.Equal("#FF0000", clockLook.Color);
        Assert.Equal(3, clockLook.ExtensionData!["glow"].GetInt32());
        Assert.Null(Assert.IsType<SpecialIconItem>(model.Items[2]).TextLook); // nothing valid is no look

        var saved = JsonSerializer.Serialize(clockLook, LayoutStorage.JsonOptions);
        Assert.Contains("\"glow\": 3", saved, StringComparison.Ordinal); // written back as it came
    }
}
