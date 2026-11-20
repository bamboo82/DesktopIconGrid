using DesktopIconGrid.Core.Icons;

namespace DesktopIconGrid.Core.Tests;

/// <summary>0.13.0 WP2: the box of an icon's drawn pixels, and whether the shell padded a small icon.</summary>
public class IconBoxTests
{
    /// <summary>A transparent 32-bit BGRA bitmap of <paramref name="side"/> × <paramref name="side"/>.</summary>
    private static byte[] Bitmap(int side) => new byte[side * side * 4];

    private static void Fill(byte[] pixels, int side, int x, int y, int width, int height, byte alpha = 255)
    {
        for (var row = y; row < y + height; row++)
        {
            for (var column = x; column < x + width; column++)
            {
                var at = ((row * side) + column) * 4;
                pixels[at] = 40;
                pixels[at + 1] = 80;
                pixels[at + 2] = 160;
                pixels[at + 3] = alpha;
            }
        }
    }

    /// <summary>A frame <paramref name="thickness"/> px wide at the bitmap's edge.</summary>
    private static void Frame(byte[] pixels, int side, int thickness = 1)
    {
        Fill(pixels, side, 0, 0, side, thickness);
        Fill(pixels, side, 0, side - thickness, side, thickness);
        Fill(pixels, side, 0, 0, thickness, side);
        Fill(pixels, side, side - thickness, 0, thickness, side);
    }

    /// <summary>Takes the pixel at <paramref name="x"/>, <paramref name="y"/> away: a frame's rounded corner.</summary>
    private static void Clear(byte[] pixels, int side, int x, int y) => pixels[(((y * side) + x) * 4) + 3] = 0;

    [Theory]
    [InlineData(64, 2, 8)] // what the shell gave for PCMan at 64: 48 of 64 (75 %), over the limit without its frame
    [InlineData(96, 2, 24)] // at 96: 50 %
    [InlineData(128, 3, 40)] // at 128: 37.5 %, a frame 3 px wide
    public void PCMans_48_px_icon_in_the_shells_frame_is_found_at_every_size(int side, int frame, int at)
    {
        // Review 33 R2: a frame by a width (1, 2, 2 px) missed it at 64 and 128, and at 64 the icon is over 70 %.
        var pixels = Bitmap(side);
        Frame(pixels, side, frame);
        if (side == 128)
        {
            Clear(pixels, side, 0, 0); // its top row: 126 of 128 drawn
            Clear(pixels, side, 127, 0);
        }

        Fill(pixels, side, at, at, 48, 48);

        Assert.Equal(frame, IconBox.FrameInset(pixels, side, side, side * 4));
        Assert.Equal(new PixelBox(at, at, 48, 48), IconBox.SmallAndCentred(pixels, side, side, side * 4));
    }

    [Fact]
    public void An_icon_without_a_frame_that_fills_most_of_its_bitmap_stays_the_shells()
    {
        // notepad.exe at 96 on the review session's PC: no frame, drawn on 93 × 85.
        var pixels = Bitmap(96);
        Fill(pixels, 96, 1, 5, 93, 85);

        Assert.Equal(0, IconBox.FrameInset(pixels, 96, 96, 96 * 4));
        Assert.Null(IconBox.SmallAndCentred(pixels, 96, 96, 96 * 4));
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
    public void A_full_96_px_icon_and_a_square_with_a_border_are_no_frame_and_left_as_they_are()
    {
        var pixels = Bitmap(96);
        Fill(pixels, 96, 0, 0, 96, 96);
        Assert.Equal(0, IconBox.FrameInset(pixels, 96, 96, 96 * 4)); // no empty ring inside the edge
        Assert.Equal(new PixelBox(0, 0, 96, 96), IconBox.Drawn(pixels, 96, 96, 96 * 4));
        Assert.Null(IconBox.SmallAndCentred(pixels, 96, 96, 96 * 4));

        // A border drawn by design, 6 px: wider than a frame (4 px at 96), so the whole is measured.
        var bordered = Bitmap(96);
        Frame(bordered, 96, 6);
        Fill(bordered, 96, 40, 40, 16, 16);
        Assert.Equal(0, IconBox.FrameInset(bordered, 96, 96, 96 * 4));
        Assert.Null(IconBox.SmallAndCentred(bordered, 96, 96, 96 * 4));
    }

    [Fact]
    public void A_framed_32_px_icon_in_96_is_measured_inside_its_frame()
    {
        var pixels = Bitmap(96);
        Frame(pixels, 96);
        Fill(pixels, 96, 32, 32, 32, 32);

        Assert.Equal(1, IconBox.FrameInset(pixels, 96, 96, 96 * 4));
        Assert.Equal(new PixelBox(32, 32, 32, 32), IconBox.SmallAndCentred(pixels, 96, 96, 96 * 4));
    }

    [Fact]
    public void A_frame_with_a_soft_inner_edge_and_one_stretched_to_256_are_found()
    {
        // 2 px drawn, a third ring partly drawn (the soft edge), then the empty ring.
        var soft = Bitmap(96);
        Frame(soft, 96, 2);
        Fill(soft, 96, 2, 2, 40, 1, alpha: 120);
        Fill(soft, 96, 16, 16, 64, 64);
        Assert.Equal(3, IconBox.FrameInset(soft, 96, 96, 96 * 4));
        Assert.Equal(new PixelBox(16, 16, 64, 64), IconBox.SmallAndCentred(soft, 96, 96, 96 * 4)); // 67 %

        var large = Bitmap(256);
        Frame(large, 256, 5);
        Fill(large, 256, 104, 104, 48, 48);
        Assert.Equal(5, IconBox.FrameInset(large, 256, 256, 256 * 4));
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
        Assert.Equal(0, IconBox.FrameInset([], 0, 0, 0));
    }

    [Fact]
    public void Real_margins_under_the_limit_and_an_icon_off_centre_are_left_as_they_are()
    {
        // Margins by design: 76 of 96 is more than 70 % of the side.
        var designed = Bitmap(96);
        Fill(designed, 96, 10, 10, 76, 76);
        Assert.Null(IconBox.SmallAndCentred(designed, 96, 96, 96 * 4));

        // Small, but in a corner: drawn so on purpose, not padded by the shell; in a frame too.
        var corner = Bitmap(96);
        Fill(corner, 96, 4, 4, 32, 32);
        Assert.Null(IconBox.SmallAndCentred(corner, 96, 96, 96 * 4));
        var framedCorner = Bitmap(96);
        Frame(framedCorner, 96);
        Fill(framedCorner, 96, 4, 4, 32, 32);
        Assert.Null(IconBox.SmallAndCentred(framedCorner, 96, 96, 96 * 4));

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
