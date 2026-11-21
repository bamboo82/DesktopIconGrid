using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace DesktopIconGrid.Services;

/// <summary>Fonts for text drawn with a <see cref="Core.Models.TextLook"/> (0.15.0 WP7b).</summary>
public static class LookFonts
{
    /// <summary>
    /// <paramref name="font"/> first, then <paramref name="own"/>, the font the text has without a look, for what the chosen
    /// one lacks (a Latin font has no Chinese); <paramref name="own"/> alone when the name cannot be a family.
    /// </summary>
    public static FontFamily Chain(string font, FontFamily own)
    {
        ArgumentNullException.ThrowIfNull(own);
        if (string.IsNullOrWhiteSpace(font))
        {
            return own;
        }

        try
        {
            return new FontFamily($"{font.Trim()}, {own.Source}");
        }
        catch (ArgumentException)
        {
            return own;
        }
    }

    /// <summary>
    /// The largest size up to <paramref name="size"/> at which <paramref name="text"/> is at most <paramref name="width"/>
    /// wide in <paramref name="family"/> and <paramref name="weight"/>: a size too large for where it is drawn is made
    /// smaller rather than cut at the sides.
    /// </summary>
    public static double FitSize(string text, double size, double width, FontFamily family, FontWeight weight)
    {
        ArgumentNullException.ThrowIfNull(family);
        if (string.IsNullOrEmpty(text) || size <= 0 || width <= 0)
        {
            return size;
        }

        var typeface = new Typeface(family, FontStyles.Normal, weight, FontStretches.Normal);
        var formatted = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, size, Brushes.Black, 1.0);
        var drawn = formatted.WidthIncludingTrailingWhitespace;
        return drawn > width ? size * width / drawn : size;
    }
}
