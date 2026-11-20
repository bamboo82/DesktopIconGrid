namespace DesktopIconGrid.Core.Icons;

/// <summary>A rectangle of pixels in a bitmap.</summary>
public readonly record struct PixelBox(int X, int Y, int Width, int Height);

/// <summary>
/// How much of its bitmap an icon fills (0.13.0 WP2). Asked for a file's image at a size the file has no image for,
/// the shell may answer with the file's small icon centred in a bitmap of the size asked: on a 48 px bar PCMan's icon was
/// drawn about 32 px, with a thin frame, beside 48 px neighbours. Pixels in, a box out, so it is tested without Windows.
/// </summary>
public static class IconBox
{
    /// <summary>Below this share of the bitmap's width and height, a centred icon is one the shell padded.</summary>
    public const double SmallShare = 0.7;

    /// <summary>A pixel with more alpha than this is drawn (32-bit BGRA, alpha the fourth byte).</summary>
    public const byte VisibleAlpha = 8;

    /// <summary>
    /// How wide a frame at the bitmap's edge is left out of the measure: one pixel per 48 of the side, at least one. The
    /// shell drew a thin frame around PCMan's padded icon; one drawn on a 48 px image that was stretched to 96 is two wide.
    /// </summary>
    public static int FrameWidth(int side) => Math.Max(1, side / 48);

    /// <summary>
    /// The box of the drawn pixels of a 32-bit BGRA bitmap of <paramref name="width"/> × <paramref name="height"/> with
    /// rows of <paramref name="stride"/> bytes, leaving out the frame at its edge (<see cref="FrameWidth"/>);
    /// <see langword="null"/> when nothing inside it is drawn.
    /// </summary>
    public static PixelBox? Drawn(ReadOnlySpan<byte> pixels, int width, int height, int stride)
    {
        var frameX = FrameWidth(width);
        var frameY = FrameWidth(height);
        if (width <= 2 * frameX || height <= 2 * frameY || stride < width * 4 || pixels.Length < stride * height)
        {
            return null;
        }

        int left = width, top = height, right = -1, bottom = -1;
        for (var y = frameY; y < height - frameY; y++)
        {
            var row = pixels.Slice(y * stride, width * 4);
            for (var x = frameX; x < width - frameX; x++)
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
    /// The box of a small icon centred in its bitmap: drawn on less than <see cref="SmallShare"/> of the bitmap's width
    /// and of its height, with margins left and right, and top and bottom, that differ by at most a tenth of the side
    /// (at least 2 px). <see langword="null"/> for an icon that fills its bitmap, one with real transparent margins less
    /// than that, one small by design but off centre, and an empty bitmap: those are left as they are.
    /// </summary>
    public static PixelBox? SmallAndCentred(ReadOnlySpan<byte> pixels, int width, int height, int stride)
    {
        if (Drawn(pixels, width, height, stride) is not { } box
            || box.Width >= SmallShare * width
            || box.Height >= SmallShare * height)
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
}
