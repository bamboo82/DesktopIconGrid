using System.Text.Json;
using System.Text.Json.Serialization;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Core.SpecialIcons;
using DesktopIconGrid.Core.Storage;

namespace DesktopIconGrid.Core.Tests;

public class TextIconTests
{
    [Fact]
    public void Normalized_trims_clamps_and_validates()
    {
        var icon = new TextIcon("  備忘錄  ", "  SimSun ", 500, true, "#abcdef").Normalized();

        Assert.NotNull(icon);
        Assert.Equal("備忘錄", icon.Text);
        Assert.Equal("SimSun", icon.FontFamily);
        Assert.Equal(TextIcon.MaxFontSize, icon.FontSize);
        Assert.True(icon.Bold);
        Assert.Equal("#ABCDEF", icon.Color);
    }

    [Fact]
    public void Over_the_picture_and_its_corner_are_written_only_when_set_and_read_back()
    {
        // 0.14.0 WP6: the text or symbol drawn on the item's picture.
        var plain = JsonSerializer.Serialize(new TextIcon("A").Normalized(), LayoutStorage.JsonOptions);
        Assert.DoesNotContain("overImage", plain, StringComparison.Ordinal);
        Assert.DoesNotContain("place", plain, StringComparison.Ordinal);

        var centre = new TextIcon("A") { OverImage = true, Place = TextIcon.PlaceCenter }.Normalized()!;
        var centreJson = JsonSerializer.Serialize(centre, LayoutStorage.JsonOptions);
        Assert.True(JsonDocument.Parse(centreJson).RootElement.GetProperty("overImage").GetBoolean());
        Assert.DoesNotContain("place", centreJson, StringComparison.Ordinal); // the middle is not written
        Assert.Null(centre.Corner);

        var corner = new TextIcon("2") { OverImage = true, Place = "BOTTOMright" }.Normalized()!;
        Assert.Equal(TextIcon.PlaceBottomRight, corner.Place);
        var read = JsonSerializer.Deserialize<TextIcon>(JsonSerializer.Serialize(corner, LayoutStorage.JsonOptions), LayoutStorage.JsonOptions)!;
        Assert.Equal(corner, read);
        Assert.Equal((true, TextIcon.PlaceBottomRight), (read.OverImage, read.Corner));
    }

    [Fact]
    public void Another_place_is_the_middle_a_place_without_the_picture_is_dropped_and_an_old_file_draws_the_tile()
    {
        var unknown = JsonSerializer.Deserialize<TextIcon>("""{"text":"A","overImage":true,"place":"middleLeft"}""", LayoutStorage.JsonOptions)!;
        Assert.Null(unknown.Corner);
        Assert.Null(unknown.Normalized()!.Place);

        var alone = new TextIcon("A") { Place = TextIcon.PlaceTopLeft };
        Assert.Null(alone.Corner); // read only with the picture
        Assert.Null(alone.Normalized()!.Place);

        var old = JsonSerializer.Deserialize<TextIcon>("""{"text":"備","fontSize":14}""", LayoutStorage.JsonOptions)!;
        Assert.False(old.OverImage); // a file without them: the text tile, as before
        Assert.Null(old.Corner);
        Assert.Equal(TextIcon.Corners, [TextIcon.PlaceTopLeft, TextIcon.PlaceTopRight, TextIcon.PlaceBottomLeft, TextIcon.PlaceBottomRight]);
    }

    [Fact]
    public void Up_or_down_is_written_only_when_not_zero_and_read_back_on_text_and_symbol_through_the_layout_file()
    {
        // 0.15.0 WP7a: 垂直位移, whole percent of the tile, negative is up.
        var plain = JsonSerializer.Serialize(new TextIcon("A").Normalized(), LayoutStorage.JsonOptions);
        Assert.DoesNotContain("offsetY", plain, StringComparison.Ordinal);
        Assert.Equal(0, JsonSerializer.Deserialize<TextIcon>(plain, LayoutStorage.JsonOptions)!.OffsetY);

        var up = new TextIcon("記", Fit: true) { OffsetY = -20 }.Normalized()!;
        var upJson = JsonSerializer.Serialize(up, LayoutStorage.JsonOptions);
        Assert.Equal(-20, JsonDocument.Parse(upJson).RootElement.GetProperty("offsetY").GetInt32());
        Assert.Equal(up, JsonSerializer.Deserialize<TextIcon>(upJson, LayoutStorage.JsonOptions));

        var symbol = SymbolCatalog.Icon(SymbolCatalog.All[0], background: "#2D4A6E") with { OverImage = true, Place = TextIcon.PlaceBottomRight, OffsetY = 20 };
        symbol = symbol.Normalized()!;
        Assert.Equal(20, symbol.OffsetY);
        Assert.Equal(symbol, JsonSerializer.Deserialize<TextIcon>(JsonSerializer.Serialize(symbol, LayoutStorage.JsonOptions), LayoutStorage.JsonOptions));

        var directory = Path.Combine(Path.GetTempPath(), "DesktopIconGrid.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var storage = new LayoutStorage(Path.Combine(directory, "layout.json"));
            storage.Save(new LauncherLayout { Items = [new ShortcutItem { Name = "Notes", Location = "notepad.exe", TextIcon = up }, new GroupItem { Name = "G", TextIcon = symbol }] });
            var loaded = storage.Load().Layout;
            Assert.Equal(up, loaded.Items[0].TextIcon);
            Assert.Equal(symbol, loaded.Items[1].TextIcon);
            Assert.Equal(-20, LauncherModel.FromLayout(loaded).Items[0].TextIcon!.OffsetY);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData(-20, -20)]
    [InlineData(20, 20)]
    [InlineData(7, 7)]
    [InlineData(21, 0)]
    [InlineData(-21, 0)]
    [InlineData(int.MinValue, 0)]
    public void Up_or_down_outside_minus_20_to_20_is_zero(int stored, int normalized)
    {
        Assert.Equal(normalized, new TextIcon("A") { OffsetY = stored }.Normalized()!.OffsetY);
    }

    [Theory]
    [InlineData("2.5", 0)]
    [InlineData("99999999999", 0)]
    [InlineData("40", 0)] // read, then out of range
    [InlineData("\"-7\"", -7)] // a number in quotes, as the layout reads its other numbers
    [InlineData("\"up\"", 0)]
    [InlineData("true", 0)]
    [InlineData("null", 0)]
    [InlineData("{\"by\":[1,2]}", 0)]
    [InlineData("[3]", 0)]
    public void Anything_else_at_load_is_zero_and_the_rest_of_the_icon_is_still_read(string value, int expected)
    {
        var json = "{\"text\":\"A\",\"offsetY\":" + value + ",\"bold\":true}";

        var icon = JsonSerializer.Deserialize<TextIcon>(json, LayoutStorage.JsonOptions)!.Normalized()!;

        Assert.Equal(expected, icon.OffsetY);
        Assert.True(icon.Bold); // what follows the odd value is read as before
    }

    [Fact]
    public void Normalized_drops_empty_text_and_bad_values()
    {
        Assert.Null(new TextIcon("   ").Normalized());

        var icon = new TextIcon(new string('x', 40), " ", double.NaN, false, "red").Normalized();

        Assert.NotNull(icon);
        Assert.Equal(TextIcon.MaxTextLength, icon.Text.Length);
        Assert.Null(icon.FontFamily);
        Assert.Equal(TextIcon.DefaultFontSize, icon.FontSize);
        Assert.Null(icon.Color);
    }

    [Fact]
    public void Normalized_validates_the_tile_colour_as_it_does_the_text_colour()
    {
        var icon = new TextIcon("A", Fit: true, Background: "#2d4a6e").Normalized();
        Assert.Equal("#2D4A6E", icon!.Background);
        Assert.True(icon.Fit);

        Assert.Null(new TextIcon("A", Background: "navy").Normalized()!.Background);
        Assert.Null(new TextIcon("A", Background: "#12345").Normalized()!.Background);
        Assert.Null(new TextIcon("A").Normalized()!.Background);
    }

    [Fact]
    public void Fit_and_tile_colour_are_written_only_when_set_and_read_back()
    {
        var plain = JsonSerializer.Serialize(new TextIcon("記"), LayoutStorage.JsonOptions);
        Assert.DoesNotContain("fit", plain, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("background", plain, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(new TextIcon("記"), JsonSerializer.Deserialize<TextIcon>(plain, LayoutStorage.JsonOptions));

        var symbol = new TextIcon("\uE715", SymbolCatalog.FontFamily, 26, Color: "#FFFFFF", Fit: true, Background: "#2D4A6E");
        var json = JsonSerializer.Serialize(symbol, LayoutStorage.JsonOptions);
        using (var document = JsonDocument.Parse(json))
        {
            Assert.True(document.RootElement.GetProperty("fit").GetBoolean());
            Assert.Equal("#2D4A6E", document.RootElement.GetProperty("background").GetString());
            Assert.Equal(26, document.RootElement.GetProperty("fontSize").GetDouble()); // what an older version draws
        }

        Assert.Equal(symbol, JsonSerializer.Deserialize<TextIcon>(json, LayoutStorage.JsonOptions));
    }

    /// <summary>The text icon of 0.11.0, as that version reads and writes it.</summary>
    private sealed record OlderTextIcon(string Text, string? FontFamily = null, double FontSize = 14, bool Bold = false, string? Color = null)
    {
        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtensionData { get; init; }
    }

    [Fact]
    public void An_older_version_draws_a_symbol_as_text_of_its_size_and_keeps_fit_and_tile_colour_through_its_save()
    {
        var symbol = new TextIcon("\uE715", SymbolCatalog.FontFamily, 26, Color: null, Fit: true, Background: "#2D4A6E");

        var older = JsonSerializer.Deserialize<OlderTextIcon>(JsonSerializer.Serialize(symbol, LayoutStorage.JsonOptions), LayoutStorage.JsonOptions)!;
        Assert.Equal(("\uE715", SymbolCatalog.FontFamily, 26.0), (older.Text, older.FontFamily, older.FontSize));
        Assert.Equal(["background", "fit"], older.ExtensionData!.Keys.Order(StringComparer.Ordinal));

        var savedByOlder = JsonSerializer.Serialize(older, LayoutStorage.JsonOptions);
        Assert.Equal(symbol, JsonSerializer.Deserialize<TextIcon>(savedByOlder, LayoutStorage.JsonOptions));
    }

    [Fact]
    public void The_built_in_pictures_are_symbols_and_the_emoji_older_versions_stored_is_recognised_but_kept()
    {
        // The review session's design (A3): Send to PC is Send, Voice is Microphone, as symbols without a tile colour.
        Assert.Equal(SymbolCatalog.Icon(SymbolCatalog.Find("\uE724")!), BuiltInTextIcons.SendToDevice);
        Assert.Equal(SymbolCatalog.Icon(SymbolCatalog.Find("\uE720")!), BuiltInTextIcons.Voice);
        Assert.True(SymbolCatalog.IsSymbol(BuiltInTextIcons.SendToDevice));
        Assert.Null(BuiltInTextIcons.SendToDevice.Background);
        Assert.Equal(BuiltInTextIcons.SendToDevice, BuiltInTextIcons.For(SpecialIconKinds.SendToDevice));
        Assert.Equal(BuiltInTextIcons.Voice, BuiltInTextIcons.For(SpecialIconKinds.Voice));
        Assert.Null(BuiltInTextIcons.For(SpecialIconKinds.Clock));

        var outbox = new TextIcon("\U0001F4E4", "Segoe UI Emoji", 22); // a new Send to PC icon of 0.11.0
        var microphone = new TextIcon("\U0001F3A4", "Segoe UI Emoji", 22); // a new Voice icon of 0.11.0
        Assert.True(BuiltInTextIcons.IsOlderDefault(SpecialIconKinds.SendToDevice, outbox));
        Assert.True(BuiltInTextIcons.IsOlderDefault(SpecialIconKinds.Voice, microphone));

        // Anything else is the user's: changed in any way, of another kind, or none at all.
        Assert.False(BuiltInTextIcons.IsOlderDefault(SpecialIconKinds.SendToDevice, outbox with { Color = "#FF0000" }));
        Assert.False(BuiltInTextIcons.IsOlderDefault(SpecialIconKinds.SendToDevice, outbox with { FontSize = 30 }));
        Assert.False(BuiltInTextIcons.IsOlderDefault(SpecialIconKinds.SendToDevice, outbox with { Bold = true }));
        Assert.False(BuiltInTextIcons.IsOlderDefault(SpecialIconKinds.SendToDevice, microphone));
        Assert.False(BuiltInTextIcons.IsOlderDefault(SpecialIconKinds.Clock, outbox));
        Assert.False(BuiltInTextIcons.IsOlderDefault(SpecialIconKinds.SendToDevice, null));

        // Loading does not rewrite it: the layout keeps what is stored.
        var layout = new LauncherLayout { Items = [new SpecialIconItem { Name = "To the den", Kind = SpecialIconKinds.SendToDevice, TextIcon = outbox }] };
        Assert.Equal(outbox, LauncherModel.FromLayout(layout).Items[0].TextIcon);
    }

    [Fact]
    public void Text_icon_survives_the_layout_file_and_model_copies()
    {
        var directory = Path.Combine(Path.GetTempPath(), "DesktopIconGrid.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var storage = new LayoutStorage(Path.Combine(directory, "layout.json"));
            var shortcut = new ShortcutItem { Name = "Notes", Location = "notepad.exe", TextIcon = new TextIcon("記", "PMingLiU", 20, true, "#112233") };
            var group = new GroupItem { Name = "G", TextIcon = new TextIcon("工具") };
            var mail = new ShortcutItem { Name = "Mail", Location = "mail.exe", TextIcon = SymbolCatalog.Icon(SymbolCatalog.Find("\uE715")!, background: "#2D4A6E") };
            storage.Save(new LauncherLayout { Items = [shortcut, group, mail] });

            var loaded = storage.Load().Layout;
            var loadedShortcut = Assert.IsType<ShortcutItem>(loaded.Items[0]);
            Assert.Equal(new TextIcon("記", "PMingLiU", 20, true, "#112233"), loadedShortcut.TextIcon);
            Assert.Equal("工具", Assert.IsType<GroupItem>(loaded.Items[1]).TextIcon!.Text);
            Assert.Equal(mail.TextIcon, loaded.Items[2].TextIcon);

            var model = LauncherModel.FromLayout(loaded);
            Assert.Equal("記", model.Items[0].TextIcon!.Text);

            var edited = (ShortcutItem)model.Items[0].Clone();
            edited.TextIcon = new TextIcon(" 新 ");
            model.Update(edited);
            Assert.Equal("新", model.Items[0].TextIcon!.Text);

            edited.TextIcon = null;
            model.Update(edited);
            Assert.Null(model.Items[0].TextIcon);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
