using System.Text.Json;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Core.Storage;

namespace DesktopIconGrid.Core.Tests;

/// <summary>Separators between icons and colour tags on items: stored, repaired and copied.</summary>
public class SeparatorAndTagTests
{
    [Fact]
    public void A_separator_is_written_with_its_own_type_and_read_back()
    {
        var label = new SeparatorItem { Name = "Work", Kind = SeparatorItem.Label };

        var json = JsonSerializer.Serialize<LauncherItem>(label, LayoutStorage.JsonOptions);
        var read = Assert.IsType<SeparatorItem>(JsonSerializer.Deserialize<LauncherItem>(json, LayoutStorage.JsonOptions));

        Assert.Contains("\"$type\": \"separator\"", json.Replace("\":\"", "\": \"", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Equal(label.Id, read.Id);
        Assert.Equal("Work", read.Name);
        Assert.Equal(SeparatorItem.Label, read.Kind);
    }

    [Fact]
    public void Separators_are_kept_in_groups_with_an_empty_caption_and_an_unknown_kind_is_drawn_as_a_line()
    {
        var layout = new LauncherLayout
        {
            Items =
            [
                new SeparatorItem { Kind = " GAP " },
                new GroupItem { Name = "Tools", Items = [new ShortcutItem { Location = @"C:\a.exe" }, new SeparatorItem { Kind = "dots" }, new SeparatorItem { Kind = "" }] },
            ],
        };

        var model = LauncherModel.FromLayout(layout);

        var gap = Assert.IsType<SeparatorItem>(model.Items[0]);
        Assert.Equal(string.Empty, gap.Name); // not given a name like "Group"
        Assert.Equal(SeparatorItem.Gap, gap.EffectiveKind);
        var children = ((GroupItem)model.Items[1]).Items;
        Assert.Equal(3, children.Count);
        Assert.Equal("dots", ((SeparatorItem)children[1]).Kind); // kept for the version that knows it
        Assert.Equal(SeparatorItem.Line, ((SeparatorItem)children[1]).EffectiveKind);
        Assert.Equal(SeparatorItem.Line, ((SeparatorItem)children[2]).Kind);
        Assert.Equal(0, model.RepairCount);
    }

    [Fact]
    public void Colour_tags_are_stored_in_their_own_spelling_and_unknown_ones_are_kept()
    {
        Assert.Equal(ColorTags.Red, ColorTags.Normalize(" RED "));
        Assert.Equal("teal", ColorTags.Normalize(" teal "));
        Assert.Null(ColorTags.Normalize("  "));
        Assert.Null(ColorTags.Find("teal"));
        Assert.Equal("#3B82F6", ColorTags.Find("Blue")!.Color);

        var model = LauncherModel.FromLayout(new LauncherLayout { Items = [new ShortcutItem { Location = "x", ColorTag = "Green" }] });
        Assert.Equal(ColorTags.Green, model.Items[0].ColorTag);

        var json = JsonSerializer.Serialize<LauncherItem>(model.Items[0], LayoutStorage.JsonOptions);
        Assert.Equal(ColorTags.Green, JsonSerializer.Deserialize<LauncherItem>(json, LayoutStorage.JsonOptions)!.ColorTag);
        Assert.DoesNotContain("colorTag", JsonSerializer.Serialize<LauncherItem>(new ShortcutItem { Location = "x" }, LayoutStorage.JsonOptions), StringComparison.Ordinal);

        var copy = new ShortcutItem();
        copy.CopyContentFrom(model.Items[0]);
        Assert.Equal(ColorTags.Green, copy.ColorTag);
        Assert.Equal(ColorTags.Green, model.Items[0].Clone().ColorTag);
    }
}
