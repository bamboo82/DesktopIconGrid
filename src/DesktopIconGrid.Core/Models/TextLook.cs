using System.Text.Json;
using System.Text.Json.Serialization;

namespace DesktopIconGrid.Core.Models;

/// <summary>
/// How text the bar draws itself looks (0.15.0 WP7b, the owner's request): a row line's caption
/// (<see cref="SeparatorItem.CaptionLook"/>) and a widget's text (<see cref="SpecialIconItem.TextLook"/>). The text icon
/// window's look part edits it, so a text icon, a caption and a widget are set in one window. Every field is optional:
/// absent is that text's drawing before 0.15.0, pixel for pixel. Use <see cref="Normalized"/> before storing; it is
/// <see langword="null"/> when nothing is set, and nothing is written then.
/// </summary>
public sealed record TextLook
{
    /// <summary>A font family, or a chain as a text icon has; absent is the text's own font.</summary>
    public string? Font { get; init; }

    /// <summary>
    /// The size in units, <see cref="TextIcon.MinFontSize"/> to <see cref="TextIcon.MaxFontSize"/>; absent is the text's own.
    /// Where it is drawn it is made smaller when it would not fit its tile or strip, never cut at the sides.
    /// </summary>
    public double? Size { get; init; }

    /// <summary>Bold or not; absent is the text's own weight.</summary>
    public bool? Bold { get; init; }

    /// <summary>
    /// <c>#RRGGBB</c> in place of the theme's text colour in the normal state; a gauge's warning and alarm colours still win
    /// in theirs. The see-through text edge stays on over it.
    /// </summary>
    public string? Color { get; init; }

    /// <summary><c>#RRGGBB</c> plate: the rounded plate a text icon has, behind a caption's text, or a widget's tile.</summary>
    public string? TileColor { get; init; }

    /// <summary>
    /// Up or down in whole percent of the caption strip's or the tile's height, <see cref="TextIcon.MinOffsetY"/> to
    /// <see cref="TextIcon.MaxOffsetY"/>, negative is up; written only when it is not 0, anything else at load is 0.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    [JsonConverter(typeof(OffsetConverter))]
    public int OffsetY { get; init; }

    /// <summary>Properties written by a newer version that this one does not know; kept so saving never drops them.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; init; }

    /// <summary>Whether nothing is set (and nothing a newer version wrote is kept): the text is drawn as before.</summary>
    [JsonIgnore]
    public bool IsEmpty =>
        Font is null && Size is null && Bold is null && Color is null && TileColor is null && OffsetY == 0 && (ExtensionData is null || ExtensionData.Count == 0);

    /// <summary>A tidied copy: a blank font, a size that is no number, a colour that is not <c>#RRGGBB</c> and an offset
    /// out of range are absent; a size out of range is held to it. <see langword="null"/> when nothing is left.</summary>
    public TextLook? Normalized()
    {
        var look = this with
        {
            Font = string.IsNullOrWhiteSpace(Font) ? null : Font.Trim(),
            Size = Size is { } size && double.IsFinite(size) ? Math.Clamp(size, TextIcon.MinFontSize, TextIcon.MaxFontSize) : null,
            Color = LauncherSettings.IsValidColor(Color) ? Color!.ToUpperInvariant() : null,
            TileColor = LauncherSettings.IsValidColor(TileColor) ? TileColor!.ToUpperInvariant() : null,
            OffsetY = OffsetY is >= TextIcon.MinOffsetY and <= TextIcon.MaxOffsetY ? OffsetY : 0,
        };
        return look.IsEmpty ? null : look;
    }
}
