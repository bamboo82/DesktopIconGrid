using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Services;

namespace DesktopIconGrid.Views;

/// <summary>
/// Draws a text icon, the same everywhere one is shown (0.12.0 WP3): the bar, a group's popup and the pointer window
/// (<see cref="IconGridControl"/>), the Properties preview and the Text icon window's preview.
/// The look is the review session's design (0.12.0 plan, addendum A2):
/// <list type="bullet">
/// <item>With a tile colour: a filled square over the whole tile, corner 6 at 32 px growing with the tile, no border.</item>
/// <item>Without one, text that fits the tile (a symbol, a built-in glyph) has no frame at all; text drawn at its size
/// keeps the frame text icons always had, the theme's hover fill and border with a corner of 6.</item>
/// <item>Fitted text is 68 % of the tile, a margin of 16 % on every side, centred; other text is drawn at its size.</item>
/// <item>The text's colour is the icon's own, else on a tile colour white or near-black, whichever has the higher WCAG
/// contrast (<see cref="ThemeCatalog.TextOnTile"/>), else the theme's text colour.</item>
/// <item>A symbol the symbol fonts of this PC do not have is the placeholder icon, not an empty box.</item>
/// <item>Over the item's own picture (0.14.0 WP6, <see cref="TextIcon.OverImage"/>) there is no tile:
/// <see cref="TextOverPicture"/> draws the text alone, and the caller draws the picture under it.</item>
/// </list>
/// </summary>
public sealed class TextIconTile : Decorator
{
    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon), typeof(TextIcon), typeof(TextIconTile), new FrameworkPropertyMetadata(null, Rebuild));

    public static readonly DependencyProperty TileSizeProperty = DependencyProperty.Register(
        nameof(TileSize), typeof(double), typeof(TextIconTile), new FrameworkPropertyMetadata(32.0, Rebuild));

    public static readonly DependencyProperty MaxFontSizeProperty = DependencyProperty.Register(
        nameof(MaxFontSize), typeof(double), typeof(TextIconTile), new FrameworkPropertyMetadata(double.PositiveInfinity, Rebuild));

    /// <summary>The corner radius of a tile of 32 px; a tile colour's square scales it with the tile.</summary>
    public const double CornerAt32 = 6;

    /// <summary>The share of the tile left free on each side of fitted text.</summary>
    public const double FitMargin = 0.16;

    private readonly Border _tile = new() { ClipToBounds = true };
    private readonly Viewbox _fit = new() { Stretch = Stretch.Uniform };
    private readonly TextBlock _text = new()
    {
        TextAlignment = TextAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private readonly Image _placeholder = new() { Stretch = Stretch.Uniform };

    /// <summary>The text over the item's picture, without a tile of its own (0.14.0 WP6).</summary>
    private readonly TextOverPicture _overlay = new();

    public TextIconTile()
    {
        _placeholder.SetResourceReference(Image.SourceProperty, "PlaceholderIconImage");
        Build();
    }

    /// <summary>The icon to draw; nothing when <see langword="null"/>.</summary>
    public TextIcon? Icon
    {
        get => (TextIcon?)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    /// <summary>The icon size the tile is drawn at, which the corner of a tile colour and the margin of fitted text follow.</summary>
    public double TileSize
    {
        get => (double)GetValue(TileSizeProperty);
        set => SetValue(TileSizeProperty, value);
    }

    /// <summary>The largest font size text that does not fit is drawn at (a small preview caps it).</summary>
    public double MaxFontSize
    {
        get => (double)GetValue(MaxFontSizeProperty);
        set => SetValue(MaxFontSizeProperty, value);
    }

    /// <summary>The tile behind the text (tests).</summary>
    internal Border TileBorder => _tile;

    /// <summary>The text (tests).</summary>
    internal TextBlock TextElement => _text;

    /// <summary>The box fitted text is scaled in, or <see langword="null"/> when the text is drawn at its size (tests).</summary>
    internal Viewbox? FitBox => ReferenceEquals(_tile.Child, _fit) ? _fit : null;

    /// <summary>Whether the placeholder is drawn because the symbol is missing here (tests).</summary>
    internal bool ShowsPlaceholder => ReferenceEquals(Child, _placeholder);

    /// <summary>The text drawn over the item's picture, or <see langword="null"/> when the tile is drawn (tests).</summary>
    internal TextOverPicture? Overlay => ReferenceEquals(Child, _overlay) ? _overlay : null;

    /// <summary>Whether this PC's symbol fonts have a code point; tests stand in an older Windows here.</summary>
    internal static Func<int, bool> HasGlyph { get; set; } = SymbolGlyphs.Has;

    /// <summary>Whether <paramref name="icon"/> is a symbol of one character that neither symbol font of this PC has.</summary>
    public static bool IsMissingSymbol(TextIcon? icon) =>
        SymbolCatalog.IsSymbol(icon) && icon!.Text is { Length: 1 } text && !HasGlyph(text[0]);

    private static void Rebuild(DependencyObject owner, DependencyPropertyChangedEventArgs e) => ((TextIconTile)owner).Build();

    private void Build()
    {
        var icon = Icon;
        if (icon is null)
        {
            Child = null;
            return;
        }

        if (icon.OverImage)
        {
            // Over the item's own picture, drawn under this (0.14.0 WP6): no tile, only the text. A symbol this PC does not
            // have leaves the picture alone rather than putting the placeholder over it.
            _overlay.Icon = IsMissingSymbol(icon) ? null : icon;
            _overlay.TileSize = TileSize;
            _overlay.MaxFontSize = MaxFontSize;
            Child = _overlay;
            return;
        }

        if (IsMissingSymbol(icon))
        {
            Child = _placeholder;
            return;
        }

        DrawTile(icon);
        DrawText(icon);
        Child = _tile;
    }

    private void DrawTile(TextIcon icon)
    {
        if (icon.Background is { } background && LauncherSettings.IsValidColor(background))
        {
            _tile.Background = ThemeManager.ToBrush(background);
            _tile.ClearValue(Border.BorderBrushProperty);
            _tile.BorderThickness = new Thickness(0);
            _tile.CornerRadius = new CornerRadius(CornerAt32 * Math.Max(TileSize, 1) / 32);
            return;
        }

        if (icon.Fit)
        {
            // No frame: the symbol stands on the bar like the other icons.
            _tile.ClearValue(Border.BackgroundProperty);
            _tile.ClearValue(Border.BorderBrushProperty);
            _tile.BorderThickness = new Thickness(0);
            _tile.CornerRadius = new CornerRadius(0);
            return;
        }

        _tile.SetResourceReference(Border.BackgroundProperty, "Theme.Hover");
        _tile.SetResourceReference(Border.BorderBrushProperty, "Theme.HoverBorder");
        _tile.BorderThickness = new Thickness(1);
        _tile.CornerRadius = new CornerRadius(CornerAt32);
    }

    /// <summary>How high the text is drawn: fitted, about as high as the box it fills; else its own size.</summary>
    internal double DrawnSize(TextIcon icon) => icon.Fit
        ? Math.Max(TileSize, 0) * (1 - (2 * FitMargin))
        : Math.Min(double.IsFinite(icon.FontSize) && icon.FontSize > 0 ? icon.FontSize : TextIcon.DefaultFontSize, MaxFontSize);

    private void DrawText(TextIcon icon)
    {
        _text.Text = icon.Text;
        _text.FontFamily = TextIconFonts.Resolve(icon.FontFamily);
        _text.FontWeight = icon.Bold ? FontWeights.Bold : FontWeights.Normal;
        var themeColour = false;
        if (icon.Color is { } color && LauncherSettings.IsValidColor(color))
        {
            _text.Foreground = ThemeManager.ToBrush(color);
        }
        else if (icon.Background is { } tile && LauncherSettings.IsValidColor(tile))
        {
            _text.Foreground = ThemeManager.ToBrush(ThemeCatalog.TextOnTile(tile));
        }
        else
        {
            _text.SetResourceReference(TextBlock.ForegroundProperty, "Theme.Foreground");
            themeColour = true;
        }

        // Re-parent the text: it is the child of the fit box or of the tile, never of both.
        _fit.Child = null;
        _tile.Child = null;
        if (icon.Fit)
        {
            _text.FontSize = double.IsFinite(icon.FontSize) && icon.FontSize > 0 ? icon.FontSize : TextIcon.DefaultFontSize;
            _text.TextWrapping = TextWrapping.NoWrap;
            _text.Margin = new Thickness(0);
            _fit.Margin = new Thickness(Math.Max(TileSize, 0) * FitMargin);
            _fit.Child = _text;
            _tile.Child = _fit;
        }
        else
        {
            _text.FontSize = Math.Min(icon.FontSize, MaxFontSize);
            _text.TextWrapping = TextWrapping.Wrap;
            _text.Margin = new Thickness(1);
            _tile.Child = _text;
        }

        // Laid out as drawn from 20 units up, on whole pixels below (0.15.0 WP9): fitted text is drawn about as high as
        // the box it is fitted to, the rest at its own size.
        TextOptions.SetTextFormattingMode(_text, TextSharpness.FormattingFor(DrawnSize(icon)));

        // In the theme's colour, with no tile colour behind it, the text stands on the bar's background, which may be
        // see-through: it takes the bar's text edge (review 38 T2; the window has it only while it is see-through). Fitted
        // text has it on its box, at the bar's scale: on the text the box scales, it would shrink with the box.
        _fit.ClearValue(EffectProperty);
        _text.ClearValue(EffectProperty);
        if (themeColour)
        {
            (icon.Fit ? (FrameworkElement)_fit : _text).SetResourceReference(EffectProperty, MainWindow.TextEdgeKey);
        }
    }
}
