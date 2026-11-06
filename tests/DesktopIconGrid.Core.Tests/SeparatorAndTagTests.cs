using System.Text.Json;
using System.Text.Json.Nodes;
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

    /// <summary>
    /// What 0.6.0 does to a layout it loads and saves, from its code at 4f527ac: an item of a kind it does not know
    /// keeps its JSON, but its name is trimmed, set to "Group" when empty (<c>LauncherModel.Adopt</c>) and written back
    /// (<c>LauncherItemConverter.Write</c>). Shortcuts, special icons and groups it knows; it looks inside groups.
    /// </summary>
    private static string ThroughVersion060(string layoutJson)
    {
        var root = JsonNode.Parse(layoutJson)!.AsObject();
        Visit(root["items"] as JsonArray);
        return root.ToJsonString();

        static void Visit(JsonArray? items)
        {
            foreach (var item in (items ?? []).OfType<JsonObject>())
            {
                var kind = item["$type"]?.GetValue<string>() ?? string.Empty;
                if (kind.Equals("group", StringComparison.OrdinalIgnoreCase))
                {
                    Visit(item["items"] as JsonArray);
                }
                else if (!kind.Equals("shortcut", StringComparison.OrdinalIgnoreCase) && !kind.Equals("special", StringComparison.OrdinalIgnoreCase))
                {
                    var name = (item["name"]?.GetValue<string>() ?? string.Empty).Trim();
                    item["name"] = name.Length == 0 ? "Group" : name;
                }
            }
        }
    }

    private static string Save(LauncherModel model) => JsonSerializer.Serialize(model.ToLayout(), LayoutStorage.JsonOptions);

    private static LauncherModel Load(string json) => LauncherModel.FromLayout(JsonSerializer.Deserialize<LauncherLayout>(json, LayoutStorage.JsonOptions)!);

    [Fact]
    public void Lines_and_gaps_are_saved_with_a_name_for_0_6_0_and_loaded_without_one()
    {
        var model = LauncherModel.FromLayout(new LauncherLayout
        {
            Items =
            [
                new SeparatorItem(),
                new SeparatorItem { Kind = SeparatorItem.Gap },
                new SeparatorItem { Kind = SeparatorItem.Label, Name = "Work" },
                new SeparatorItem { Kind = "dots" }, // a newer kind: its name is the newer version's business
            ],
        });

        var json = Save(model);

        Assert.Equal(["Divider", "Gap", "Work", string.Empty], JsonNode.Parse(json)!["items"]!.AsArray().Select(i => i!["name"]!.GetValue<string>()));
        Assert.Equal([string.Empty, string.Empty, "Work", string.Empty], Load(json).Items.Select(i => i.Name));
    }

    [Fact]
    public void A_bar_saved_by_0_6_0_on_the_way_keeps_its_lines_and_gaps_without_a_name()
    {
        // The emulation is faithful to the bug: a line saved by 0.7.0 ("name": "") came back from 0.6.0 as "Group".
        Assert.Contains("\"Group\"", ThroughVersion060("""{"items":[{"$type":"separator","kind":"line","name":""}]}"""), StringComparison.Ordinal);

        var model = LauncherModel.FromLayout(new LauncherLayout
        {
            Items =
            [
                new ShortcutItem { Name = "A", Location = @"C:\a.exe" },
                new SeparatorItem(),
                new GroupItem { Name = "Tools", Items = [new SeparatorItem { Kind = SeparatorItem.Gap }, new ShortcutItem { Name = "B", Location = @"C:\b.exe" }] },
                new SeparatorItem { Kind = SeparatorItem.Label, Name = "Work" },
            ],
        });

        var back = Load(ThroughVersion060(Save(model)));

        Assert.Equal(string.Empty, back.Items[1].Name);
        Assert.Equal(string.Empty, ((GroupItem)back.Items[2]).Items[0].Name);
        Assert.Equal("Work", back.Items[3].Name);
        Assert.DoesNotContain(back.Items.Concat(((GroupItem)back.Items[2]).Items), item => item.Name == "Group");
    }

    [Fact]
    public void A_line_already_named_Group_loses_the_name_and_an_item_from_a_newer_version_keeps_having_none()
    {
        const string json = """
            {"items":[
              {"$type":"separator","kind":"line","name":"Group"},
              {"$type":"hologram","name":"","beam":"blue"}
            ]}
            """;

        var model = Load(json);

        Assert.Equal(string.Empty, model.Items[0].Name);
        var newer = Assert.IsType<UnknownItem>(model.Items[1]);
        Assert.Equal(string.Empty, newer.Name); // 0.7.0 named it "Group" and saved that, as 0.6.0 does with lines
        var saved = JsonNode.Parse(Save(model))!["items"]!.AsArray();
        Assert.Equal("Divider", saved[0]!["name"]!.GetValue<string>());
        Assert.Equal(string.Empty, saved[1]!["name"]!.GetValue<string>());
        Assert.Equal("blue", saved[1]!["beam"]!.GetValue<string>());
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
