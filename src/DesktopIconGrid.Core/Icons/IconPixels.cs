namespace DesktopIconGrid.Core.Icons;

/// <summary>How many pixels an icon is asked of its source in (0.15.0 WP8).</summary>
public static class IconPixels
{
    /// <summary>The sizes icon files are drawn in by convention; the shell's largest image is 256.</summary>
    public static readonly IReadOnlyList<int> SourceSizes = [16, 20, 24, 32, 40, 48, 64, 72, 80, 96, 128, 256];

    /// <summary>
    /// The size to ask an icon's source for when it is drawn <paramref name="iconSize"/> units wide at
    /// <paramref name="scale"/>: the first of <see cref="SourceSizes"/> at or above the pixels it covers, else 256. So the
    /// image is scaled down, never a smaller one stretched up (but past 256 pixels, where the shell has nothing larger).
    /// </summary>
    public static int SourceSize(int iconSize, double scale)
    {
        // A hair under, so that 20 units at 120 % are 24 pixels and not 25.
        var pixels = Math.Ceiling(iconSize * (scale > 0 ? scale : 1) - 0.01);
        foreach (var size in SourceSizes)
        {
            if (size >= pixels)
            {
                return size;
            }
        }

        return SourceSizes[^1];
    }
}
