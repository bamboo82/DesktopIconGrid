using DesktopIconGrid.Core.Models;

namespace DesktopIconGrid.Core.Tests;

/// <summary>0.15.0 WP4: the colour picker's conversions.</summary>
public class ColourMathTests
{
    [Fact]
    public void Every_colour_goes_to_hue_saturation_and_value_and_back_unchanged()
    {
        for (var r = 0; r <= 255; r += 5)
        {
            for (var g = 0; g <= 255; g += 5)
            {
                for (var b = 0; b <= 255; b += 5)
                {
                    var rgb = new Rgb((byte)r, (byte)g, (byte)b);
                    Assert.Equal(rgb, ColourMath.ToRgb(ColourMath.ToHsv(rgb)));
                }
            }
        }
    }

    [Theory]
    [InlineData("#FF0000", 0, 1, 1)]
    [InlineData("#00FF00", 120, 1, 1)]
    [InlineData("#0000FF", 240, 1, 1)]
    [InlineData("#FFFF00", 60, 1, 1)]
    [InlineData("#FF00FF", 300, 1, 1)]
    [InlineData("#808080", 0, 0, 128 / 255.0)] // a grey has no hue
    [InlineData("#000000", 0, 0, 0)]
    [InlineData("#73806E", 103.33, 0.1406, 0.502)] // the default custom background
    public void Known_colours_have_their_hue_saturation_and_value(string hex, double hue, double saturation, double value)
    {
        Assert.True(ColourMath.TryParseHex(hex, out var rgb));
        var hsv = ColourMath.ToHsv(rgb);
        Assert.Equal(hue, hsv.H, 2);
        Assert.Equal(saturation, hsv.S, 3);
        Assert.Equal(value, hsv.V, 3);
        Assert.Equal(hex, ColourMath.ToHex(ColourMath.ToRgb(hsv)));
    }

    [Theory]
    [InlineData("#73806e", "#73806E")]
    [InlineData(" 73806E ", "#73806E")] // without # and with spaces
    [InlineData("#FFFFFF", "#FFFFFF")]
    public void A_colour_code_is_read_with_or_without_its_hash(string text, string expected)
    {
        Assert.True(ColourMath.TryParseHex(text, out var rgb));
        Assert.Equal(expected, ColourMath.ToHex(rgb));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("#12345")]
    [InlineData("#1234567")]
    [InlineData("#12345G")]
    [InlineData("purple")]
    [InlineData("##123456")]
    public void What_is_not_a_colour_code_is_refused(string? text)
    {
        Assert.False(ColourMath.TryParseHex(text, out _));
    }

    [Theory]
    [InlineData("0", true, 0)]
    [InlineData(" 255 ", true, 255)]
    [InlineData("128", true, 128)]
    [InlineData("256", false, 0)]
    [InlineData("-1", false, 0)]
    [InlineData("1.5", false, 0)]
    [InlineData("abc", false, 0)]
    [InlineData("", false, 0)]
    [InlineData("0255", false, 0)]
    public void A_channel_is_a_whole_number_from_0_to_255(string text, bool accepted, byte value)
    {
        Assert.Equal(accepted, ColourMath.TryParseChannel(text, out var channel));
        Assert.Equal(value, channel);
    }

    [Fact]
    public void A_hue_out_of_range_wraps_and_saturation_and_value_are_held_to_0_to_1()
    {
        Assert.Equal(new Rgb(255, 0, 0), ColourMath.ToRgb(new Hsv(360, 1, 1)));
        Assert.Equal(new Rgb(0, 0, 255), ColourMath.ToRgb(new Hsv(-120, 1, 1)));
        Assert.Equal(new Rgb(255, 255, 255), ColourMath.ToRgb(new Hsv(10, -1, 2)));
    }
}
