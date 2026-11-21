using System.Windows;
using System.Windows.Media;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Services;
using DesktopIconGrid.Views;

namespace DesktopIconGrid.SpecialIcons;

/// <summary>
/// A widget's <see cref="TextLook"/> as its tile draws it (0.15.0 WP7b), read by the tile template's triggers: only what
/// the look sets changes, so a widget without one is drawn exactly as before.
/// <list type="bullet">
/// <item>The font (before the bar's) and the colour go to the value and to its caption; the size, the weight and up or
/// down to the value only.</item>
/// <item>The size is in the tile's 100 units, the units the value is drawn in (the clock's time at 42 for up to four
/// characters), so it grows with the icon as the gauge does; where it would not fit it is made smaller, never cut at the
/// sides.</item>
/// <item>The colour is the normal state's: a value's warning and alarm colours still win in theirs. On a plate without a
/// colour of its own the text is white or near-black, whichever reads better, as a text icon's on a tile colour.</item>
/// <item>The plate is the tile's fill, with a text icon's tile corner.</item>
/// <item>While the bar is see-through, text in a chosen colour keeps an edge, in the tone that stands out from that colour;
/// on a plate it needs none.</item>
/// </list>
/// </summary>
public sealed class GaugeLook
{
    /// <summary>A text icon's tile corner (6 at 32 px) in the tile's 100 units.</summary>
    public const double PlateCorner = TextIconTile.CornerAt32 * 100 / 32;

    public GaugeLook(TextLook look, FontFamily ownFamily)
    {
        ArgumentNullException.ThrowIfNull(look);
        ArgumentNullException.ThrowIfNull(ownFamily);
        Look = look;
        HasFont = look.Font is not null;
        Family = HasFont ? LookFonts.Chain(look.Font!, ownFamily) : ownFamily;
        HasSize = look.Size is not null;
        Size = look.Size ?? 0;
        HasBold = look.Bold is not null;
        Weight = look.Bold == true ? FontWeights.Bold : FontWeights.Normal;
        var plate = LauncherSettings.IsValidColor(look.TileColor) ? look.TileColor : null;
        HasPlate = plate is not null;
        PlateBrush = plate is null ? null : ThemeManager.ToBrush(plate);
        var text = LauncherSettings.IsValidColor(look.Color) ? look.Color : plate is null ? null : ThemeCatalog.TextOnTile(plate);
        HasColor = text is not null;
        TextBrush = text is null ? null : ThemeManager.ToBrush(text);
        IsLightText = text is not null && ThemeCatalog.Luminance(text) > 0.5;
        if (look.OffsetY != 0)
        {
            var shift = new TranslateTransform(0, look.OffsetY); // a percent of the tile is a unit of its 100
            shift.Freeze();
            Shift = shift;
        }
    }

    public TextLook Look { get; }

    public bool HasFont { get; }

    /// <summary>The look's font, then the bar's for what it does not have.</summary>
    public FontFamily Family { get; }

    public bool HasSize { get; }

    /// <summary>The value's size in the tile's 100 units.</summary>
    public double Size { get; }

    public bool HasBold { get; }

    public FontWeight Weight { get; }

    /// <summary>The text has a colour of its own in the normal state: the chosen one, or the plate's contrast colour.</summary>
    public bool HasColor { get; }

    public Brush? TextBrush { get; }

    /// <summary>The text's own colour is light: its see-through edge is near-black, else white.</summary>
    public bool IsLightText { get; }

    /// <summary>Text in a colour of its own stands on the see-through background, not on a plate: it takes an edge.</summary>
    public bool EdgesColor => HasColor && !HasPlate;

    public bool HasPlate { get; }

    public Brush? PlateBrush { get; }

    public CornerRadius PlateCornerRadius => new(PlateCorner);

    public bool HasShift => Shift is not null;

    /// <summary>Up or down in the tile's units; <see langword="null"/> at 0.</summary>
    public Transform? Shift { get; }
}
