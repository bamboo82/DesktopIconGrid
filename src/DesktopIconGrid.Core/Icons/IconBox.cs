namespace DesktopIconGrid.Core.Icons;

/// <summary>A rectangle of pixels in a bitmap.</summary>
public readonly record struct PixelBox(int X, int Y, int Width, int Height);

/// <summary>
/// How much of its bitmap an icon fills (0.13.0 WP2). Asked for a file's image at a size the file has no image for,
/// the shell may answer with the file's small icon centred in a bitmap of the size asked, inside a thin frame drawn at
/// the bitmap's edge: PCMan's 48 × 48 icon in 64, 96 and 128 px bitmaps, with a frame 2 or 3 px wide (review 33 R2).
/// Pixels in, a box out, so it is tested without Windows.
/// </summary>
public static class IconBox
{
    /// <summary>Without a frame, a centred icon drawn on less than this share of the bitmap's width and height is one the shell padded.</summary>
    public const double SmallShare = 0.7;

    /// <summary>A pixel with more alpha than this is drawn (32-bit BGRA, alpha the fourth byte).</summary>
    public const byte VisibleAlpha = 8;

    /// <summary>A frame is at most this share of the bitmap's side wide, rounded, at least one pixel: 3 px at 64, 4 at 96, 5 at 128.</summary>
    public const double MaxFrameShare = 0.04;

    /// <summary>A ring of a frame is drawn on at least this share of each of its four sides (its corners may be rounded off).</summary>
    public const double FrameCover = 0.9;

    /// <summary>
    /// How far in from the edge the icon is measured: past the frame the shell draws around a padded icon, to the empty
    /// ring inside it; 0 when the bitmap has no such frame. A frame is found by what it is, not by a width (review 33 R2):
    /// the outermost rings of the bitmap, each drawn on nearly its whole length (<see cref="FrameCover"/>) on all four
    /// sides, at most <see cref="MaxFrameShare"/> of the side wide, and then a ring with nothing drawn on it; one ring that
    /// is only partly drawn between them is the frame's soft edge. An icon that fills its bitmap to the edge has no empty
    /// ring there, and a thick border drawn by design is wider than a frame: neither is taken for one.
    /// </summary>
    public static int FrameInset(ReadOnlySpan<byte> pixels, int width, int height, int stride)
    {
        if (!Readable(pixels, width, height, stride))
        {
            return 0;
        }

        var side = Math.Min(width, height);
        var widest = Math.Max(1, (int)Math.Round(side * MaxFrameShare, MidpointRounding.AwayFromZero));
        var rings = 0;
        while (rings < widest && 2 * (rings + 1) < side && Cover(pixels, width, height, stride, rings) >= FrameCover)
        {
            rings++;
        }

        if (rings == 0)
        {
            return 0;
        }

        for (var ring = rings; ring <= rings + 1 && ring <= widest && 2 * (ring + 1) < side; ring++)
        {
            if (IsEmpty(pixels, width, height, stride, ring))
            {
                return ring;
            }
        }

        return 0;
    }

    /// <summary>
    /// The box of the drawn pixels of a 32-bit BGRA bitmap of <paramref name="width"/> × <paramref name="height"/> with
    /// rows of <paramref name="stride"/> bytes, from <paramref name="inset"/> pixels in from each edge;
    /// <see langword="null"/> when nothing there is drawn.
    /// </summary>
    public static PixelBox? Drawn(ReadOnlySpan<byte> pixels, int width, int height, int stride, int inset = 0)
    {
        if (!Readable(pixels, width, height, stride) || inset < 0 || 2 * inset >= Math.Min(width, height))
        {
            return null;
        }

        int left = width, top = height, right = -1, bottom = -1;
        for (var y = inset; y < height - inset; y++)
        {
            var row = pixels.Slice(y * stride, width * 4);
            for (var x = inset; x < width - inset; x++)
            {
                if (row[(x * 4) + 3] <= VisibleAlpha)
                {
                    continue;
                }

                left = Math.Min(left, x);
                right = Math.Max(right, x);
                top = Math.Min(top, y);
                bottom = Math.Max(bottom, y);
            }
        }

        return right < 0 ? null : new PixelBox(left, top, right - left + 1, bottom - top + 1);
    }

    /// <summary>
    /// The box of an icon the shell padded: inside a frame (<see cref="FrameInset"/>), any icon, as it is smaller than
    /// its bitmap; without one, an icon drawn on less than <see cref="SmallShare"/> of the bitmap's width and of its
    /// height. Either way it is centred: the two margins of each side differ by at most a tenth of it (at least 2 px).
    /// <see langword="null"/> for an icon that fills its bitmap, one with transparent margins under the limit, one in a
    /// corner, and an empty bitmap: those are left as they are.
    /// </summary>
    public static PixelBox? SmallAndCentred(ReadOnlySpan<byte> pixels, int width, int height, int stride)
    {
        var inset = FrameInset(pixels, width, height, stride);
        if (Drawn(pixels, width, height, stride, inset) is not { } box)
        {
            return null;
        }

        if (inset == 0 && (box.Width >= SmallShare * width || box.Height >= SmallShare * height))
        {
            return null;
        }

        var leftRight = Math.Abs(box.X - (width - box.X - box.Width));
        var topBottom = Math.Abs(box.Y - (height - box.Y - box.Height));
        return leftRight <= Math.Max(2, width / 10) && topBottom <= Math.Max(2, height / 10) ? box : null;
    }

    /// <summary>
    /// The square, as wide as the longer side of <paramref name="box"/> and centred on it, that is cut out of a padded
    /// icon: an icon narrower than it is tall keeps its proportions and its own place in its square. Kept inside the
    /// bitmap of <paramref name="width"/> × <paramref name="height"/>.
    /// </summary>
    public static PixelBox Square(PixelBox box, int width, int height)
    {
        var side = Math.Min(Math.Max(box.Width, box.Height), Math.Min(width, height));
        var x = Math.Clamp(box.X + ((box.Width - side) / 2), 0, width - side);
        var y = Math.Clamp(box.Y + ((box.Height - side) / 2), 0, height - side);
        return new PixelBox(x, y, side, side);
    }

    private static bool Readable(ReadOnlySpan<byte> pixels, int width, int height, int stride) =>
        width > 0 && height > 0 && stride >= width * 4 && pixels.Length >= stride * height;

    private static bool IsDrawn(ReadOnlySpan<byte> pixels, int stride, int x, int y) => pixels[(y * stride) + (x * 4) + 3] > VisibleAlpha;

    /// <summary>The smallest share of its four sides that ring <paramref name="ring"/> (0 at the edge) has drawn.</summary>
    private static double Cover(ReadOnlySpan<byte> pixels, int width, int height, int stride, int ring)
    {
        int last = width - 1 - ring, bottom = height - 1 - ring, top = 0, under = 0, left = 0, right = 0;
        for (var x = ring; x <= last; x++)
        {
            top += IsDrawn(pixels, stride, x, ring) ? 1 : 0;
            under += IsDrawn(pixels, stride, x, bottom) ? 1 : 0;
        }

        for (var y = ring; y <= bottom; y++)
        {
            left += IsDrawn(pixels, stride, ring, y) ? 1 : 0;
            right += IsDrawn(pixels, stride, last, y) ? 1 : 0;
        }

        double across = last - ring + 1, down = bottom - ring + 1;
        return Math.Min(Math.Min(top / across, under / across), Math.Min(left / down, right / down));
    }

    /// <summary>Whether nothing is drawn on ring <paramref name="ring"/>.</summary>
    private static bool IsEmpty(ReadOnlySpan<byte> pixels, int width, int height, int stride, int ring)
    {
        int last = width - 1 - ring, bottom = height - 1 - ring;
        for (var x = ring; x <= last; x++)
        {
            if (IsDrawn(pixels, stride, x, ring) || IsDrawn(pixels, stride, x, bottom))
            {
                return false;
            }
        }

        for (var y = ring; y <= bottom; y++)
        {
            if (IsDrawn(pixels, stride, ring, y) || IsDrawn(pixels, stride, last, y))
            {
                return false;
            }
        }

        return true;
    }
}
