using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Media;
using DesktopIconGrid.Core.Models;

namespace DesktopIconGrid.Services;

/// <summary>
/// Whether this PC's symbol font has a symbol (0.12.0 WP3). A code point a font lacks would be drawn as an empty box,
/// or by a fallback font as something else, so a symbol neither font has is drawn as the placeholder icon instead.
/// Each character is looked up once; the fonts do not change while the program runs.
/// </summary>
public static class SymbolGlyphs
{
    /// <summary>The families of <see cref="SymbolCatalog.FontFamily"/>, the one Windows 11 has first.</summary>
    public static IReadOnlyList<string> Families { get; } =
        SymbolCatalog.FontFamily.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static readonly ConcurrentDictionary<int, bool> Known = new();
    private static readonly ConcurrentDictionary<string, GlyphTypeface?> Typefaces = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether Segoe Fluent Icons or Segoe MDL2 Assets, as installed here, has <paramref name="codePoint"/>.</summary>
    public static bool Has(int codePoint) =>
        Known.GetOrAdd(codePoint, code => Families.Any(family => Has(family, code)));

    /// <summary>Whether the installed family <paramref name="family"/> has <paramref name="codePoint"/>; false when it is not installed.</summary>
    public static bool Has(string family, int codePoint) =>
        Typefaces.GetOrAdd(family, Load) is { } typeface && typeface.CharacterToGlyphMap.ContainsKey(codePoint);

    /// <summary>The glyph typeface of an installed family only: a name WPF does not know must not resolve to a fallback font.</summary>
    private static GlyphTypeface? Load(string family)
    {
        var installed = TextIconFonts.Find(family);
        if (installed is null)
        {
            return null;
        }

        var typeface = new Typeface(installed.Family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        return typeface.TryGetGlyphTypeface(out var glyphs) ? glyphs : null;
    }
}
