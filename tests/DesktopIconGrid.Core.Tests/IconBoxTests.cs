using DesktopIconGrid.Core.Icons;

namespace DesktopIconGrid.Core.Tests;

/// <summary>0.13.0 WP2: the box of an icon's drawn pixels, and whether the shell padded a small icon.</summary>
public class IconBoxTests
{
    /// <summary>A transparent 32-bit BGRA bitmap of <paramref name="side"/> × <paramref name="side"/>.</summary>
    private static byte[] Bitmap(int side) => new byte[side * side * 4];

    private static void Fill(byte[] pixels, int side, int x, int y, int width, int height)
    {
        for (var row = y; row < y + height; row++)
        {
            for (var column = x; column < x + width; column++)
            {
                var at = ((row * side) + column) * 4;
                pixels[at] = 40;
                pixels[at + 1] = 80;
                pixels[at + 2] = 160;
                pixels[at + 3] = 255;
            }
        }
    }

    private static void Frame(byte[] pixels, int side)
    {
        Fill(pixels, side, 0, 0, side, 1);
        Fill(pixels, side, 0, side - 1, side, 1);
        Fill(pixels, side, 0, 0, 1, side);
        Fill(pixels, side, side - 1, 0, 1, side);
    }

    [Fact]
    public void A_32_px_icon_centred_in_96_is_small_and_centred()
    {
        var pixels = Bitmap(96);
        Fill(pixels, 96, 32, 32, 32, 32);

        Assert.Equal(new PixelBox(32, 32, 32, 32), IconBox.Drawn(pixels, 96, 96, 96 * 4));
        Assert.Equal(new PixelBox(32, 32, 32, 32), IconBox.SmallAndCentred(pixels, 96, 96, 96 * 4));
    }

    [Fact]
    public void A_full_96_px_icon_is_left_as_it_is()
    {
        var pixels = Bitmap(96);
        Fill(pixels, 96, 0, 0, 96, 96);

        Assert.Equal(new PixelBox(2, 2, 92, 92), IconBox.Drawn(pixels, 96, 96, 96 * 4)); // the edge's frame left out
        Assert.Null(IconBox.SmallAndCentred(pixels, 96, 96, 96 * 4));
    }

    [Fact]
    public void A_framed_32_px_icon_in_96_is_measured_without_its_frame()
    {
        var pixels = Bitmap(96);
        Frame(pixels, 96);
        Fill(pixels, 96, 32, 32, 32, 32);

        Assert.Equal(new PixelBox(32, 32, 32, 32), IconBox.SmallAndCentred(pixels, 96, 96, 96 * 4));
    }

    [Fact]
    public void A_frame_stretched_with_its_icon_is_left_out_too()
    {
        // A 1 px frame drawn on 48 px and stretched to 96 is 2 px wide; at 256 it is 5 px.
        Assert.Equal([1, 1, 2, 5], new[] { 16, 48, 96, 256 }.Select(IconBox.FrameWidth));

        var pixels = Bitmap(96);
        Fill(pixels, 96, 0, 0, 96, 2);
        Fill(pixels, 96, 0, 94, 96, 2);
        Fill(pixels, 96, 0, 0, 2, 96);
        Fill(pixels, 96, 94, 0, 2, 96);
        Fill(pixels, 96, 16, 16, 64, 64);
        Assert.Equal(new PixelBox(16, 16, 64, 64), IconBox.SmallAndCentred(pixels, 96, 96, 96 * 4)); // 64 of 96 is 67 %

        var large = Bitmap(256);
        Fill(large, 256, 0, 0, 256, 5);
        Fill(large, 256, 0, 251, 256, 5);
        Fill(large, 256, 0, 0, 5, 256);
        Fill(large, 256, 251, 0, 5, 256);
        Fill(large, 256, 104, 104, 48, 48);
        Assert.Equal(new PixelBox(104, 104, 48, 48), IconBox.SmallAndCentred(large, 256, 256, 256 * 4));
    }

    [Fact]
    public void The_cut_out_square_keeps_a_narrow_icon_in_the_middle_of_it()
    {
        Assert.Equal(new PixelBox(32, 32, 32, 32), IconBox.Square(new PixelBox(32, 32, 32, 32), 96, 96));
        Assert.Equal(new PixelBox(32, 32, 32, 32), IconBox.Square(new PixelBox(38, 32, 20, 32), 96, 96)); // a bottle
        Assert.Equal(new PixelBox(30, 0, 36, 36), IconBox.Square(new PixelBox(30, 2, 36, 20), 96, 96)); // kept inside
        Assert.Equal(new PixelBox(0, 0, 10, 10), IconBox.Square(new PixelBox(0, 0, 12, 10), 10, 10));
    }

    [Fact]
    public void An_empty_bitmap_has_no_box()
    {
        Assert.Null(IconBox.Drawn(Bitmap(96), 96, 96, 96 * 4));
        Assert.Null(IconBox.SmallAndCentred(Bitmap(96), 96, 96, 96 * 4));
        Assert.Null(IconBox.SmallAndCentred([], 0, 0, 0));
    }

    [Fact]
    public void Real_margins_under_the_limit_and_an_icon_off_centre_are_left_as_they_are()
    {
        // Margins by design: 76 of 96 is more than 70 % of the side.
        var designed = Bitmap(96);
        Fill(designed, 96, 10, 10, 76, 76);
        Assert.Null(IconBox.SmallAndCentred(designed, 96, 96, 96 * 4));

        // Small, but in a corner: drawn so on purpose, not padded by the shell.
        var corner = Bitmap(96);
        Fill(corner, 96, 4, 4, 32, 32);
        Assert.Null(IconBox.SmallAndCentred(corner, 96, 96, 96 * 4));

        // A row stride wider than the pixels, as GDI gives some bitmaps.
        var wide = new byte[96 * 100 * 4];
        for (var row = 32; row < 64; row++)
        {
            for (var column = 32; column < 64; column++)
            {
                wide[(row * 400) + (column * 4) + 3] = 255;
            }
        }

        Assert.Equal(new PixelBox(32, 32, 32, 32), IconBox.SmallAndCentred(wide, 96, 96, 400));
    }
}
