using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DesktopIconGrid.Core.Models;

/// <summary>
/// Draws an item's icon as text (for example 宋體 characters or an abbreviation) instead of an image.
/// Immutable so items can share instances safely; use <see cref="Normalized"/> before storing.
/// </summary>
/// <param name="Text">The characters to draw (up to <see cref="MaxTextLength"/>).</param>
/// <param name="FontFamily">Font family name; <see langword="null"/> uses <see cref="DefaultFontFamily"/>.</param>
/// <param name="FontSize">Font size in device-independent pixels.</param>
/// <param name="Bold">Draw bold.</param>
/// <param name="Color"><c>#RRGGBB</c> text colour; <see langword="null"/> uses the theme's text colour.</param>
/// <param name="Fit">
/// Scale the text to the tile instead of drawing it at <paramref name="FontSize"/>, so it grows with the icon size (a
/// symbol always has it). Written only when set: 0.11.0 and older keep it unread and draw the text at its size.
/// </param>
/// <param name="Background">
/// <c>#RRGGBB</c> tile colour, a filled square behind the text in place of the theme's hover fill and border;
/// <see langword="null"/> for the theme's look. Written only when set.
/// </param>
public sealed record TextIcon(
    string Text,
    string? FontFamily = null,
    double FontSize = TextIcon.DefaultFontSize,
    bool Bold = false,
    string? Color = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Fit = false,
    string? Background = null)
{
    public const int MaxTextLength = 12;
    public const double DefaultFontSize = 14;
    public const double MinFontSize = 6;
    public const double MaxFontSize = 72;

    /// <summary>宋體 (SimSun) first, then the Traditional Chinese 新細明體, then the Windows UI font.</summary>
    public const string DefaultFontFamily = "SimSun, PMingLiU, Segoe UI";

    /// <summary>新細明體 (PMingLiU) first: the chain for a Traditional Chinese UI.</summary>
    public const string TraditionalChineseFontFamily = "PMingLiU, SimSun, Segoe UI";

    /// <summary>The chain drawn for text icons without a font of their own, for the given UI language.</summary>
    public static string DefaultFontFamilyFor(CultureInfo? uiCulture) =>
        AppLanguage.IsTraditionalChinese(uiCulture) ? TraditionalChineseFontFamily : DefaultFontFamily;

    /// <summary>Properties written by a newer version that this one does not know; kept so saving never drops them.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; init; }

    /// <summary>The middle of the picture, where <see cref="Place"/> puts the text unless it names a corner; never written.</summary>
    public const string PlaceCenter = "center";

    public const string PlaceTopLeft = "topLeft";
    public const string PlaceTopRight = "topRight";
    public const string PlaceBottomLeft = "bottomLeft";
    public const string PlaceBottomRight = "bottomRight";

    /// <summary>The corners <see cref="Place"/> may name.</summary>
    public static readonly IReadOnlyList<string> Corners = [PlaceTopLeft, PlaceTopRight, PlaceBottomLeft, PlaceBottomRight];

    /// <summary>
    /// Draw the item's picture as it is drawn without a text icon, and the text or symbol over it (0.14.0 WP6, the owner's
    /// request). Written only when set: a version before 0.14.0 does not read it and draws the text tile alone, as before.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool OverImage { get; init; }

    /// <summary>
    /// Where the text sits on the picture: one of <see cref="Corners"/>, or <see langword="null"/> for the middle. Read only
    /// with <see cref="OverImage"/>; another value is the middle. Written only when it names a corner.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Place { get; init; }

    /// <summary>The corner the text is drawn in over the picture, or <see langword="null"/> for the middle (or no picture).</summary>
    [JsonIgnore]
    public string? Corner => OverImage ? CornerOf(Place) : null;

    private static string? CornerOf(string? place) =>
        Corners.FirstOrDefault(corner => string.Equals(corner, place?.Trim(), StringComparison.OrdinalIgnoreCase));

    public static readonly IReadOnlyList<double> SuggestedFontSizes =
        [8, 9, 10, 11, 12, 14, 16, 18, 20, 22, 24, 28, 32, 36, 40, 48];

    /// <summary>A trimmed, clamped copy; <see langword="null"/> when there is no text to draw.</summary>
    public TextIcon? Normalized()
    {
        var text = Text?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            return null;
        }

        if (text.Length > MaxTextLength)
        {
            text = text[..MaxTextLength];
        }

        var size = double.IsFinite(FontSize) ? Math.Clamp(FontSize, MinFontSize, MaxFontSize) : DefaultFontSize;
        return this with
        {
            Text = text,
            FontFamily = string.IsNullOrWhiteSpace(FontFamily) ? null : FontFamily.Trim(),
            FontSize = size,
            Color = LauncherSettings.IsValidColor(Color) ? Color!.ToUpperInvariant() : null,
            Background = LauncherSettings.IsValidColor(Background) ? Background!.ToUpperInvariant() : null,
            Place = OverImage ? CornerOf(Place) : null, // the middle and another value are not written
        };
    }
}
