using DesktopIconGrid.Core.Icons;

namespace DesktopIconGrid.Core.Tests;

/// <summary>0.15.0 WP8: the pixels an icon is asked of its source in.</summary>
public class IconPixelsTests
{
    [Theory]
    [InlineData(72, 1.0, 72)]
    [InlineData(72, 1.5, 128)] // 108 pixels: the 128 image, scaled down
    [InlineData(32, 1.0, 32)]
    [InlineData(32, 1.25, 40)]
    [InlineData(32, 1.5, 48)]
    [InlineData(32, 2.0, 64)]
    [InlineData(28, 1.0, 32)] // no 28 image by convention: the next one
    [InlineData(20, 1.0, 20)]
    [InlineData(20, 1.2, 24)] // 24 pixels, though 20 × 1.2 is a hair over 24 in floating point
    [InlineData(16, 1.0, 16)]
    [InlineData(96, 1.25, 128)]
    [InlineData(128, 1.0, 128)]
    [InlineData(128, 1.5, 256)]
    [InlineData(256, 1.0, 256)]
    [InlineData(200, 1.5, 256)] // past the shell's largest: its 256 image
    [InlineData(32, 0, 32)] // no scale known: 1
    public void The_first_usual_size_at_or_above_the_pixels_drawn_is_asked(int iconSize, double scale, int expected)
    {
        Assert.Equal(expected, IconPixels.SourceSize(iconSize, scale));
    }
}
