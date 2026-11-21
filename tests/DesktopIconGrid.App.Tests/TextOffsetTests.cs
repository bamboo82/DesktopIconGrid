using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.ViewModels;
using DesktopIconGrid.Views;

namespace DesktopIconGrid.App.Tests;

/// <summary>0.15.0 WP7a: 垂直位移, a text or symbol icon moved up or down by whole percent of the tile.</summary>
[Collection(WpfWindowsCollection.Name)]
public sealed class TextOffsetTests(WpfThread wpf)
{
    private const int Side = 100;

    /// <summary>A wide text: fitted, it is far lower than the tile, so a fifth of the tile up or down keeps it whole.</summary>
    private const string Wide = "WWWWWW";

    [Fact]
    public void The_editor_keeps_the_offset_in_range_on_both_sides_resets_it_and_reopens_with_it()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        wpf.Invoke(() =>
        {
            var editor = new TextIconViewModel(null, "Notes");
            Assert.Equal((0, "0 %"), (editor.OffsetY, editor.OffsetText));
            Assert.Equal(0, editor.ToTextIcon()!.OffsetY);

            editor.OffsetY = -35;
            Assert.Equal((-20, "−20 %"), (editor.OffsetY, editor.OffsetText)); // held to its range, a real minus sign
            editor.OffsetY = 7;
            Assert.Equal((7, "+7 %"), (editor.OffsetY, editor.OffsetText));
            Assert.Equal(7, editor.PreviewIcon!.OffsetY); // the preview follows
            Assert.Equal(7, editor.ToTextIcon()!.OffsetY);

            editor.IsSymbol = true;
            editor.SelectedSymbol = editor.Symbols[0];
            Assert.Equal(7, editor.ToTextIcon()!.OffsetY); // the same on the Symbol side

            var reopened = new TextIconViewModel(editor.ToTextIcon(), "Notes");
            Assert.True(reopened.IsSymbol);
            Assert.Equal(7, reopened.OffsetY); // a symbol keeps its offset too

            reopened.ResetOffset();
            Assert.Equal(0, reopened.ToTextIcon()!.OffsetY);
        });
    }

    [Fact]
    public void The_window_has_the_slider_under_the_fields_with_its_number_and_Reset()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        wpf.Invoke(() =>
        {
            var viewModel = new TextIconViewModel(new TextIcon("記") { OffsetY = -5 }, "Notes");
            var window = new TextIconWindow { DataContext = viewModel };
            try
            {
                window.Show();
                Assert.Equal((-20.0, 20.0, -5.0), (window.OffsetSlider.Minimum, window.OffsetSlider.Maximum, window.OffsetSlider.Value));

                window.OffsetSlider.Value = 12;
                Assert.Equal(12, viewModel.OffsetY);

                window.ResetOffsetButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Assert.Equal(0, viewModel.OffsetY);
                Assert.Equal(0, window.OffsetSlider.Value);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Alone_on_a_tile_the_text_moves_by_its_share_of_the_tile(bool fit)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        wpf.Invoke(() =>
        {
            TextIconTile Tile(int offset) => new()
            {
                Icon = new TextIcon(fit ? Wide : "ab", FontSize: 20, Color: "#FF0000", Fit: fit) { OffsetY = offset }.Normalized(),
                TileSize = Side,
                Width = Side,
                Height = Side,
            };

            var middle = Centre(Tile(0), red: true);
            Assert.Equal(middle - 20, Centre(Tile(-20), red: true), 1.0); // negative is up
            Assert.Equal(middle + 20, Centre(Tile(20), red: true), 1.0);
            Assert.Equal(20, TextIconTile.ShiftFor(new TextIcon("a") { OffsetY = 20 }, Side));
            Assert.Equal(-2, TextIconTile.ShiftFor(new TextIcon("a") { OffsetY = -7 }, 32)); // −2.24, on whole units
        });
    }

    [Theory]
    [InlineData(null)]
    [InlineData(TextIcon.PlaceBottomRight)]
    [InlineData(TextIcon.PlaceTopLeft)]
    public void Over_a_picture_the_text_moves_by_its_share_of_the_tile_in_the_middle_and_in_a_corner(string? place)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        wpf.Invoke(() =>
        {
            TextOverPicture Over(int offset, string? tile = null) => new()
            {
                Icon = new TextIcon(Wide, Fit: true, Background: tile) { OverImage = true, Place = place, OffsetY = offset }.Normalized(),
                TileSize = Side,
                Width = Side,
                Height = Side,
            };

            foreach (var tile in new[] { null, "#2D4A6E" }) // the outline, then the plate, which moves with the text
            {
                var middle = Centre(Over(0, tile));
                Assert.Equal(middle - 20, Centre(Over(-20, tile)), 1.0);
                Assert.Equal(middle + 20, Centre(Over(20, tile)), 1.0);
            }
        });
    }

    [Fact]
    public void What_moves_past_the_tiles_edge_is_cut_alone_on_a_tile_and_over_a_picture()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        wpf.Invoke(() =>
        {
            // One letter fitted to a corner's box, then a fifth of the tile further down: it would end below the tile.
            var over = new TextOverPicture
            {
                Icon = new TextIcon("W", Fit: true, Background: "#2D4A6E") { OverImage = true, Place = TextIcon.PlaceBottomRight, OffsetY = 20 }.Normalized(),
                TileSize = Side,
                Width = Side,
                Height = Side,
            };
            Assert.Equal(Side - 1, InkRows(over, Side + 40).Bottom);

            // Alone on a tile the fitted box (or the text) moves inside the tile, which cuts its content at its edge.
            var alone = new TextIconTile
            {
                Icon = new TextIcon("W", Color: "#FF0000", Fit: true) { OffsetY = 20 }.Normalized(),
                TileSize = Side,
                Width = Side,
                Height = Side,
            };
            Assert.True(alone.TileBorder.ClipToBounds);
            Assert.Same(alone.FitBox, alone.TileBorder.Child);
            Assert.Equal(20, Assert.IsType<TranslateTransform>(alone.FitBox!.RenderTransform).Y);
            Assert.Equal(Transform.Identity, alone.TextElement.RenderTransform); // moved once, outside the box's scale

            alone.Icon = new TextIcon("ab", FontSize: 20) { OffsetY = -5 }.Normalized(); // drawn at its size: the text itself moves
            Assert.Null(alone.FitBox);
            Assert.Equal(-5, Assert.IsType<TranslateTransform>(alone.TextElement.RenderTransform).Y);
            alone.Icon = new TextIcon("ab", FontSize: 20).Normalized();
            Assert.Equal(Transform.Identity, alone.TextElement.RenderTransform);
        });
    }

    /// <summary>The middle of the rows that have ink (with <paramref name="red"/>, red ink only), from a render at 96 dpi.</summary>
    private static double Centre(FrameworkElement element, bool red = false)
    {
        var (top, bottom) = InkRows(element, Side, red);
        Assert.True(top >= 0, "nothing was drawn");
        return (top + bottom) / 2.0;
    }

    /// <summary>
    /// The first and last rows with ink (alpha over 40, past the edges' smoothing; with <paramref name="red"/>, red text
    /// only, not the faint hover fill and border the app's styles give a text tile), with the element laid out on a tile of
    /// <see cref="Side"/> and rendered into a target <paramref name="height"/> high: what it draws below the tile shows.
    /// </summary>
    private static (int Top, int Bottom) InkRows(FrameworkElement element, int height, bool red = false)
    {
        element.Measure(new Size(Side, Side));
        element.Arrange(new Rect(0, 0, Side, Side));
        element.UpdateLayout();
        var target = new RenderTargetBitmap(Side, height, 96, 96, PixelFormats.Pbgra32);
        target.Render(element);
        var stride = Side * 4;
        var pixels = new byte[stride * height];
        target.CopyPixels(pixels, stride, 0);
        int top = -1, bottom = -1;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < Side; x++)
            {
                var at = (y * stride) + (x * 4); // premultiplied blue, green, red, alpha
                if (red ? pixels[at + 2] > 150 && pixels[at + 1] < 100 : pixels[at + 3] > 40)
                {
                    top = top < 0 ? y : top;
                    bottom = y;
                    break;
                }
            }
        }

        return (top, bottom);
    }
}
