using System.Windows;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Resources;
using DesktopIconGrid.ViewModels;
using DesktopIconGrid.Views;

namespace DesktopIconGrid.App.Tests;

/// <summary>0.14.0 WP6: a text or symbol icon drawn over the item's own picture.</summary>
[Collection(WpfWindowsCollection.Name)]
public sealed class TextOverPictureTests(WpfThread wpf)
{
    private static TextIcon Over(string? place = null, string? color = null, string? tile = null) =>
        new TextIcon("2", Color: color, Background: tile) { OverImage = true, Place = place }.Normalized()!;

    [Fact]
    public void The_text_takes_the_whole_tile_or_a_box_of_55_percent_in_its_corner()
    {
        Near(new Rect(0, 0, 48, 48), TextOverPicture.BoxFor(null, 48));
        Near(new Rect(0, 0, 26.4, 26.4), TextOverPicture.BoxFor(TextIcon.PlaceTopLeft, 48));
        Near(new Rect(48 - 26.4, 0, 26.4, 26.4), TextOverPicture.BoxFor(TextIcon.PlaceTopRight, 48));
        Near(new Rect(0, 48 - 26.4, 26.4, 26.4), TextOverPicture.BoxFor(TextIcon.PlaceBottomLeft, 48));
        Near(new Rect(48 - 26.4, 48 - 26.4, 26.4, 26.4), TextOverPicture.BoxFor(TextIcon.PlaceBottomRight, 48));
    }

    /// <summary>The same box within a thousandth of a unit: 48 × 0.55 is 26.400000000000002 in doubles.</summary>
    private static void Near(Rect expected, Rect actual)
    {
        Assert.Equal(expected.X, actual.X, 3);
        Assert.Equal(expected.Y, actual.Y, 3);
        Assert.Equal(expected.Width, actual.Width, 3);
        Assert.Equal(expected.Height, actual.Height, 3);
    }

    [Fact]
    public void Without_a_tile_colour_the_text_is_white_with_a_dark_edge_and_with_one_it_follows_the_contrast_rule()
    {
        Assert.Equal(("#FFFFFF", TextOverPicture.DarkOutline), TextOverPicture.ColorsFor(Over())); // white by default, not the theme's
        Assert.Equal(("#FFEE00", TextOverPicture.DarkOutline), TextOverPicture.ColorsFor(Over(color: "#FFEE00"))); // light text, dark edge
        Assert.Equal(("#202060", "#FFFFFF"), TextOverPicture.ColorsFor(Over(color: "#202060"))); // dark text, white edge
        Assert.Equal((ThemeCatalog.TextOnTile("#2D4A6E"), (string?)null), TextOverPicture.ColorsFor(Over(tile: "#2D4A6E"))); // a plate, no edge
        Assert.Equal(("#112233", (string?)null), TextOverPicture.ColorsFor(Over(color: "#112233", tile: "#FFFFFF")));
    }

    [Fact]
    public void The_tile_draws_only_the_text_over_the_picture_and_a_missing_symbol_leaves_the_picture_alone()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        wpf.Invoke(() =>
        {
            var tile = new TextIconTile { Icon = Over(TextIcon.PlaceBottomRight), TileSize = 48 };
            Assert.NotNull(tile.Overlay);
            Assert.Equal(TextIcon.PlaceBottomRight, tile.Overlay!.Icon!.Corner);
            Assert.Equal(48, tile.Overlay.TileSize);

            tile.Icon = new TextIcon("A");
            Assert.Null(tile.Overlay); // the tile as before

            var before = TextIconTile.HasGlyph;
            TextIconTile.HasGlyph = _ => false; // an older Windows
            try
            {
                tile.Icon = SymbolCatalog.Icon(SymbolCatalog.All[0]) with { OverImage = true };
                Assert.NotNull(tile.Overlay);
                Assert.Null(tile.Overlay!.Icon); // nothing over the picture, not the placeholder
                Assert.False(tile.ShowsPlaceholder);
            }
            finally
            {
                TextIconTile.HasGlyph = before;
            }
        });
    }

    [Fact]
    public void The_editor_offers_the_picture_only_with_one_keeps_the_choice_on_both_sides_and_saves_the_corner()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        wpf.Invoke(() =>
        {
            var without = new TextIconViewModel(null, "Notes");
            without.OverImage = true;
            Assert.False(without.OverImage); // no picture to keep: not offered
            Assert.False(without.ToTextIcon()!.OverImage);

            var editor = new TextIconViewModel(null, "Notes", picture: null, canOverImage: true);
            Assert.Equal((Strings.TextIcon_Hint, false), (editor.Hint, editor.ShowsPicture));
            editor.OverImage = true;
            Assert.Equal((Strings.TextIcon_HintOver, true, true), (editor.Hint, editor.ShowsPicture, editor.IsPlaceCenter));
            Assert.Equal((true, (string?)null), (editor.ToTextIcon()!.OverImage, editor.ToTextIcon()!.Place)); // the middle is not written

            editor.IsPlaceTopLeft = true;
            Assert.False(editor.IsPlaceCenter);
            Assert.Equal(TextIcon.PlaceTopLeft, editor.ToTextIcon()!.Place);
            Assert.Equal(TextIcon.PlaceTopLeft, editor.PreviewIcon!.Corner);

            editor.IsSymbol = true;
            editor.SelectedSymbol = editor.Symbols[0];
            Assert.True(editor.ShowsHint); // the second hint on the Symbol side too
            Assert.Equal((true, TextIcon.PlaceTopLeft), (editor.ToTextIcon()!.OverImage, editor.ToTextIcon()!.Corner));

            var reopened = new TextIconViewModel(editor.ToTextIcon(), "Notes", picture: null, canOverImage: true);
            Assert.True(reopened.OverImage && reopened.IsPlaceTopLeft && reopened.IsSymbol);
        });
    }
}
