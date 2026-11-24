using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Services;

namespace DesktopIconGrid.Views;

/// <summary>
/// Text drawn with a <see cref="TextLook"/> (0.15.0 WP7b): a row line's caption that has a look, and the text icon window's
/// previews of a caption and a widget. What the look does not set is inherited as before (set the text's own size, weight,
/// colour and font with <see cref="TextElement"/> on this element):
/// <list type="bullet">
/// <item>the font goes first, the inherited one after it for what it lacks;</item>
/// <item>the colour is the chosen one; on a plate without one, white or near-black, whichever reads better;</item>
/// <item>the plate is drawn just behind the text, its box and <see cref="TextOverPicture.PlatePadding"/> more, corner
/// <see cref="TextOverPicture.PlateCorner"/> (a widget's preview draws it as the tile instead: <see cref="DrawsPlate"/>);</item>
/// <item>while the bar is see-through the text keeps its edge: the theme's for text in the theme's colour, in the tone that
/// stands out from a chosen colour, none on a plate;</item>
/// <item>up or down by the look's percent of <see cref="ShiftBase"/> when it is set; a row line moves its caption itself,
/// by the strip's height (<see cref="RowLinePanel.ShiftPercentProperty"/>).</item>
/// </list>
/// </summary>
public sealed class LookedText : Decorator
{
    /// <summary>
    /// A line's height for its font size (0.16.0 WP4): a widget's name moves up or down by its look's percent of the name's
    /// line, the bar's name size times this, as a row line's caption moves by its strip's.
    /// </summary>
    public const double LineFactor = 4.0 / 3;

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(LookedText), new FrameworkPropertyMetadata(string.Empty, Rebuild));

    public static readonly DependencyProperty LookProperty = DependencyProperty.Register(
        nameof(Look), typeof(TextLook), typeof(LookedText), new FrameworkPropertyMetadata(null, Rebuild));

    public static readonly DependencyProperty DrawsPlateProperty = DependencyProperty.Register(
        nameof(DrawsPlate), typeof(bool), typeof(LookedText), new FrameworkPropertyMetadata(true, Rebuild));

    public static readonly DependencyProperty ShiftBaseProperty = DependencyProperty.Register(
        nameof(ShiftBase), typeof(double), typeof(LookedText), new FrameworkPropertyMetadata(0.0, Rebuild));

    private readonly Border _plate = new();
    private readonly TextBlock _text = new() { TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };

    public LookedText()
    {
        _plate.Child = _text;
        Child = _plate;
        Build();
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public TextLook? Look
    {
        get => (TextLook?)GetValue(LookProperty);
        set => SetValue(LookProperty, value);
    }

    /// <summary>Whether the look's plate is drawn behind the text (a caption) or left to the caller (a widget's tile).</summary>
    public bool DrawsPlate
    {
        get => (bool)GetValue(DrawsPlateProperty);
        set => SetValue(DrawsPlateProperty, value);
    }

    /// <summary>The height the look's up or down is a percent of; 0 leaves it to the parent.</summary>
    public double ShiftBase
    {
        get => (double)GetValue(ShiftBaseProperty);
        set => SetValue(ShiftBaseProperty, value);
    }

    /// <summary>The text (tests).</summary>
    internal TextBlock TextElementForTests => _text;

    /// <summary>The plate behind the text (tests).</summary>
    internal Border PlateForTests => _plate;

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == TextElement.FontFamilyProperty && Look?.Font is not null)
        {
            Build(); // the chosen font's fallback is the inherited one, which just changed
        }
    }

    private static void Rebuild(DependencyObject owner, DependencyPropertyChangedEventArgs e) => ((LookedText)owner).Build();

    private void Build()
    {
        _text.Text = Text ?? string.Empty;
        var look = Look;

        if (look?.Font is { } font)
        {
            _text.FontFamily = LookFonts.Chain(font, TextElement.GetFontFamily(this));
        }
        else
        {
            _text.ClearValue(TextBlock.FontFamilyProperty);
        }

        if (look?.Size is { } size)
        {
            _text.FontSize = size;
        }
        else
        {
            _text.ClearValue(TextBlock.FontSizeProperty);
        }

        if (look?.Bold is { } bold)
        {
            _text.FontWeight = bold ? FontWeights.Bold : FontWeights.Normal;
        }
        else
        {
            _text.ClearValue(TextBlock.FontWeightProperty);
        }

        var plate = LauncherSettings.IsValidColor(look?.TileColor) ? look!.TileColor : null;
        var colour = LauncherSettings.IsValidColor(look?.Color) ? look!.Color : plate is null ? null : ThemeCatalog.TextOnTile(plate);
        if (colour is not null)
        {
            _text.Foreground = ThemeManager.ToBrush(colour);
        }
        else
        {
            _text.ClearValue(TextBlock.ForegroundProperty);
        }

        if (plate is not null && DrawsPlate)
        {
            _plate.Background = ThemeManager.ToBrush(plate);
            _plate.Padding = new Thickness(TextOverPicture.PlatePadding);
            _plate.CornerRadius = new CornerRadius(TextOverPicture.PlateCorner);
        }
        else
        {
            _plate.ClearValue(Border.BackgroundProperty);
            _plate.ClearValue(Border.PaddingProperty);
            _plate.ClearValue(Border.CornerRadiusProperty);
        }

        // The see-through edge stays on: the theme's for the theme's colour, the tone that stands out from a chosen one,
        // none on a plate (the text stands on the plate, not on the desktop).
        if (plate is not null)
        {
            _text.ClearValue(EffectProperty);
        }
        else if (colour is not null)
        {
            _text.SetResourceReference(EffectProperty, ThemeCatalog.Luminance(colour) > 0.5 ? MainWindow.LightTextEdgeKey : MainWindow.DarkTextEdgeKey);
        }
        else
        {
            _text.SetResourceReference(EffectProperty, MainWindow.TextEdgeKey);
        }

        var shift = look is { OffsetY: not 0 } && ShiftBase > 0 ? look.OffsetY * ShiftBase / 100 : 0;
        if (shift != 0)
        {
            RenderTransform = new TranslateTransform(0, shift);
        }
        else
        {
            ClearValue(RenderTransformProperty);
        }
    }
}
