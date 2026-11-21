using System.Globalization;

namespace DesktopIconGrid.Core.Models;

/// <summary>A colour as red, green and blue, 0 to 255 each.</summary>
public readonly record struct Rgb(byte R, byte G, byte B);

/// <summary>A colour as hue (0 up to 360), saturation and value (0 to 1 each).</summary>
public readonly record struct Hsv(double H, double S, double V);

/// <summary>The colour picker's conversions (0.15.0 WP4): <c>#RRGGBB</c>, red-green-blue and hue-saturation-value.</summary>
public static class ColourMath
{
    /// <summary><paramref name="rgb"/> as <c>#RRGGBB</c>, in capitals.</summary>
    public static string ToHex(Rgb rgb) => string.Create(CultureInfo.InvariantCulture, $"#{rgb.R:X2}{rgb.G:X2}{rgb.B:X2}");

    /// <summary>
    /// A colour code as typed: <c>#RRGGBB</c>, or the six digits without <c>#</c>, spaces around allowed; anything else
    /// is no colour.
    /// </summary>
    public static bool TryParseHex(string? text, out Rgb rgb)
    {
        var value = (text ?? string.Empty).Trim();
        if (!value.StartsWith('#'))
        {
            value = "#" + value;
        }

        if (!LauncherSettings.IsValidColor(value))
        {
            rgb = default;
            return false;
        }

        var (r, g, b) = ThemeCatalog.ParseRgb(value);
        rgb = new Rgb((byte)r, (byte)g, (byte)b);
        return true;
    }

    /// <summary>One channel as typed: a whole number from 0 to 255, spaces around allowed; anything else is none.</summary>
    public static bool TryParseChannel(string? text, out byte value)
    {
        value = 0;
        var trimmed = (text ?? string.Empty).Trim();
        if (trimmed.Length is 0 or > 3 || !trimmed.All(char.IsAsciiDigit))
        {
            return false;
        }

        var number = int.Parse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture);
        if (number > 255)
        {
            return false;
        }

        value = (byte)number;
        return true;
    }

    /// <summary>
    /// <paramref name="rgb"/> as hue, saturation and value. A grey has no hue (0 here) and black no saturation (0 here):
    /// the picker keeps its own then, so that its marks do not jump.
    /// </summary>
    public static Hsv ToHsv(Rgb rgb)
    {
        double r = rgb.R / 255.0, g = rgb.G / 255.0, b = rgb.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;
        double hue = 0;
        if (delta > 0)
        {
            hue = max == r ? 60 * (((g - b) / delta) % 6)
                : max == g ? 60 * (((b - r) / delta) + 2)
                : 60 * (((r - g) / delta) + 4);
            if (hue < 0)
            {
                hue += 360;
            }
        }

        return new Hsv(hue % 360, max > 0 ? delta / max : 0, max);
    }

    /// <summary><paramref name="hsv"/> as red, green and blue, each rounded to the nearest whole value.</summary>
    public static Rgb ToRgb(Hsv hsv)
    {
        var hue = ((hsv.H % 360) + 360) % 360;
        var saturation = Math.Clamp(hsv.S, 0, 1);
        var value = Math.Clamp(hsv.V, 0, 1);
        var chroma = value * saturation;
        var x = chroma * (1 - Math.Abs(((hue / 60) % 2) - 1));
        var m = value - chroma;
        var (r, g, b) = (int)(hue / 60) switch
        {
            0 => (chroma, x, 0.0),
            1 => (x, chroma, 0.0),
            2 => (0.0, chroma, x),
            3 => (0.0, x, chroma),
            4 => (x, 0.0, chroma),
            _ => (chroma, 0.0, x),
        };
        return new Rgb(Channel(r + m), Channel(g + m), Channel(b + m));

        static byte Channel(double share) => (byte)Math.Clamp((int)Math.Round(share * 255, MidpointRounding.AwayFromZero), 0, 255);
    }
}
