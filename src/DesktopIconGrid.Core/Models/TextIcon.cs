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
public sealed record TextIcon(
    string Text,
    string? FontFamily = null,
    double FontSize = TextIcon.DefaultFontSize,
    bool Bold = false,
    string? Color = null)
{
    public const int MaxTextLength = 12;
    public const double DefaultFontSize = 14;
    public const double MinFontSize = 6;
    public const double MaxFontSize = 72;

    /// <summary>宋體 (SimSun) first, then the Traditional Chinese 新細明體, then the Windows UI font.</summary>
    public const string DefaultFontFamily = "SimSun, PMingLiU, Segoe UI";

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
        };
    }
}
