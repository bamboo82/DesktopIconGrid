using System.Globalization;
using System.Windows;
using System.Windows.Media;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Services;

namespace DesktopIconGrid.Views;

/// <summary>
/// Text or a symbol drawn over an item's own picture (0.14.0 WP6, <see cref="TextIcon.OverImage"/>; the review session's
/// design). The picture is drawn under it as it is drawn without a text icon; this draws only the text:
/// <list type="bullet">
/// <item>In the middle it takes the whole tile: at its size, or fitted (a symbol always) with the margin fitted text has
/// on a tile of its own. In a corner it takes a box of <see cref="CornerShare"/> of the tile's side, and is always fitted
/// to it.</item>
/// <item>Without a tile colour the text has a <see cref="Outline"/>-unit outline in the contrasting tone, near-black under
/// light text and white under dark, and its colour is white unless it has its own (not the theme's: it stands on a
/// picture, not on the bar).</item>
/// <item>With a tile colour that colour is a plate just behind the text, its box and <see cref="PlatePadding"/> more, corner
/// <see cref="PlateCorner"/>, not the whole tile; the text's colour follows the tile colour's contrast rule.</item>
/// </list>
/// The text is drawn as its outline geometry, so the outline keeps its width when the text is fitted to any size.
/// </summary>
public sealed class TextOverPicture : FrameworkElement
{
    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon), typeof(TextIcon), typeof(TextOverPicture), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TileSizeProperty = DependencyProperty.Register(
        nameof(TileSize), typeof(double), typeof(TextOverPicture),
        new FrameworkPropertyMetadata(32.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MaxFontSizeProperty = DependencyProperty.Register(
        nameof(MaxFontSize), typeof(double), typeof(TextOverPicture), new FrameworkPropertyMetadata(double.PositiveInfinity, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>The share of the tile's side a corner's box takes.</summary>
    public const double CornerShare = 0.55;

    /// <summary>How far a tile colour's plate reaches past the text's box.</summary>
    public const double PlatePadding = 2;

    /// <summary>The corner radius of a tile colour's plate.</summary>
    public const double PlateCorner = 3;

    /// <summary>The width of the outline around text without a tile colour, outside the letters.</summary>
    public const double Outline = 1;

    /// <summary>The text colour without a colour of its own and without a tile colour.</summary>
    public const string DefaultColor = "#FFFFFF";

    /// <summary>The outline under light text.</summary>
    public const string DarkOutline = "#1A1A1A";

    public TextIcon? Icon
    {
        get => (TextIcon?)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public double TileSize
    {
        get => (double)GetValue(TileSizeProperty);
        set => SetValue(TileSizeProperty, value);
    }

    /// <summary>The largest size text drawn at its size is drawn at (a small preview caps it).</summary>
    public double MaxFontSize
    {
        get => (double)GetValue(MaxFontSizeProperty);
        set => SetValue(MaxFontSizeProperty, value);
    }

    /// <summary>The box the text is drawn in, in a tile <paramref name="side"/> wide: the whole tile, or the corner's.</summary>
    public static Rect BoxFor(string? corner, double side)
    {
        var small = side * CornerShare;
        var far = side - small;
        return corner switch
        {
            TextIcon.PlaceTopLeft => new Rect(0, 0, small, small),
            TextIcon.PlaceTopRight => new Rect(far, 0, small, small),
            TextIcon.PlaceBottomLeft => new Rect(0, far, small, small),
            TextIcon.PlaceBottomRight => new Rect(far, far, small, small),
            _ => new Rect(0, 0, side, side),
        };
    }

    /// <summary>The text's colour and, without a tile colour, its outline's: by the rules in the class summary.</summary>
    public static (string Text, string? Outline) ColorsFor(TextIcon icon)
    {
        ArgumentNullException.ThrowIfNull(icon);
        var tile = LauncherSettings.IsValidColor(icon.Background) ? icon.Background : null;
        var own = LauncherSettings.IsValidColor(icon.Color) ? icon.Color : null;
        if (tile is not null)
        {
            return (own ?? ThemeCatalog.TextOnTile(tile), null);
        }

        var text = own ?? DefaultColor;
        return (text, ThemeCatalog.Luminance(text) > 0.5 ? DarkOutline : "#FFFFFF");
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var side = Math.Max(0, TileSize);
        return new Size(double.IsInfinity(availableSize.Width) ? side : Math.Min(side, availableSize.Width), double.IsInfinity(availableSize.Height) ? side : Math.Min(side, availableSize.Height));
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        if (Icon is not { } icon || string.IsNullOrEmpty(icon.Text))
        {
            return;
        }

        var side = Math.Min(RenderSize.Width, RenderSize.Height);
        if (side <= 0)
        {
            return;
        }

        var corner = icon.Corner;
        var box = BoxFor(corner, side);
        var fit = corner is not null || icon.Fit || SymbolCatalog.IsSymbol(icon);
        var (textColor, outlineColor) = ColorsFor(icon);
        var room = outlineColor is null ? PlatePadding : Outline; // the plate or the outline stays inside the box
        if (corner is null && fit)
        {
            box.Inflate(-side * TextIconTile.FitMargin, -side * TextIconTile.FitMargin); // as fitted text on a tile of its own
        }
        else
        {
            box.Inflate(-room, -room);
        }

        if (box.Width <= 0 || box.Height <= 0)
        {
            return;
        }

        var typeface = new Typeface(TextIconFonts.Resolve(icon.FontFamily), FontStyles.Normal, icon.Bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal);
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        FormattedText Text(double size) => new(icon.Text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, size, Brushes.Black, pixelsPerDip);

        double size;
        if (fit)
        {
            var probe = Text(100).BuildGeometry(default).Bounds;
            if (probe.IsEmpty || probe.Width <= 0 || probe.Height <= 0)
            {
                return; // only spaces
            }

            size = 100 * Math.Min(box.Width / probe.Width, box.Height / probe.Height);
        }
        else
        {
            size = Math.Min(double.IsFinite(icon.FontSize) && icon.FontSize > 0 ? icon.FontSize : TextIcon.DefaultFontSize, MaxFontSize);
        }

        var geometry = Text(Math.Max(size, 0.5)).BuildGeometry(default);
        var ink = geometry.Bounds;
        if (ink.IsEmpty)
        {
            return;
        }

        // The letters themselves (not their line) in the middle of the box, then up or down by their share of the tile
        // (0.15.0 WP7a), the plate with them; the tile cuts what passes its edge.
        var offset = new Vector(
            box.X + ((box.Width - ink.Width) / 2) - ink.X,
            box.Y + ((box.Height - ink.Height) / 2) - ink.Y + TextIconTile.ShiftFor(icon, side));
        geometry.Transform = new TranslateTransform(offset.X, offset.Y);
        var placed = Rect.Offset(ink, offset);
        var fill = ThemeManager.ToBrush(textColor);
        drawingContext.PushClip(new RectangleGeometry(new Rect(0, 0, side, side)));
        if (outlineColor is null)
        {
            var plate = placed;
            plate.Inflate(PlatePadding, PlatePadding);
            drawingContext.DrawRoundedRectangle(ThemeManager.ToBrush(icon.Background!), null, plate, PlateCorner, PlateCorner);
            drawingContext.DrawGeometry(fill, null, geometry);
        }
        else
        {
            var pen = new Pen(ThemeManager.ToBrush(outlineColor), 2 * Outline) { LineJoin = PenLineJoin.Round };
            pen.Freeze();
            drawingContext.DrawGeometry(null, pen, geometry); // half of it outside the letters: one unit
            drawingContext.DrawGeometry(fill, null, geometry); // the letters over the inner half
        }

        drawingContext.Pop();
    }
}
