using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Core.SpecialIcons;
using DesktopIconGrid.Infrastructure;
using DesktopIconGrid.Resources;
using DesktopIconGrid.Services;

namespace DesktopIconGrid.ViewModels;

/// <summary>One cell of the symbol grid.</summary>
public sealed class SymbolChoice(SymbolEntry entry)
{
    public SymbolEntry Entry { get; } = entry;

    /// <summary>The symbol's character.</summary>
    public string Text => Entry.Text;

    /// <summary>The English name: the cell's tooltip and what a screen reader says.</summary>
    public string Name => Entry.Name;

    /// <summary>The heading it is listed under, in the UI language.</summary>
    public string GroupName { get; } = TextIconViewModel.GroupName(entry.Group);

    public override string ToString() => Name;
}

/// <summary>What the text icon window sets (0.15.0 WP7b): what is shown differs, nothing else.</summary>
public enum TextWindowMode
{
    /// <summary>A shortcut's or a group's text or symbol icon: everything, as before.</summary>
    Icon,

    /// <summary>A row line's caption: the text (or a symbol) and its look; no picture and no place.</summary>
    Caption,

    /// <summary>A widget's own text: the look only, with a sample of what the widget shows now.</summary>
    Widget,

    /// <summary>
    /// A widget's name (0.16.0 WP4): the look only, with the name in the bar's name size as its sample; the second part of the
    /// widget's window (<see cref="WidgetLookParts"/>).
    /// </summary>
    WidgetName,
}

/// <summary>
/// How a caption or a widget's text is drawn without a look of its own (0.15.0 WP7b): what the window starts from, and
/// what is not written when it is kept, so that OK without a change leaves the text as it was.
/// </summary>
/// <param name="Size">The size it is drawn at: a caption's in the bar's units, a widget's in its tile's 100.</param>
/// <param name="Weight">Its weight; the Bold box starts ticked from bold up.</param>
/// <param name="Family">Its font, which "(as before)" in the font list stands for.</param>
/// <param name="TileSize">The bar's icon size, which a widget's preview is drawn at.</param>
public sealed record TextLookDefaults(double Size, FontWeight Weight, FontFamily Family, int TileSize = LauncherSettings.DefaultIconSize)
{
    /// <summary>Drawn bold (or heavier): the Bold box starts ticked.</summary>
    public bool Bold => Weight.ToOpenTypeWeight() >= FontWeights.Bold.ToOpenTypeWeight();
}

/// <summary>
/// Backs the Text icon window. The Text side is the characters, font, size and weight drawn instead of an image; the
/// Symbol side (0.12.0 WP3) is a symbol of <see cref="SymbolCatalog"/>, found by a search. Both have the text colour and
/// the tile colour. Switching sides keeps both sides' values while the window is open; OK saves the side shown. The same
/// window sets a row line's caption and a widget's text (0.15.0 WP7b, <see cref="TextWindowMode"/>), whose look is a
/// <see cref="TextLook"/>.
/// </summary>
public sealed class TextIconViewModel : ObservableObject
{
    /// <summary>The longest caption the Caption mode's box takes; a text icon keeps <see cref="TextIcon.MaxTextLength"/>.</summary>
    public const int MaxCaptionLength = 100;

    private readonly TextLookDefaults? _defaults;
    private readonly FontChoice? _ownFont;
    private readonly string _widgetName = string.Empty;

    /// <summary>The Text side keeps the fit its icon came with (only the built-in glyphs have it); it has no control for it.</summary>
    private readonly bool _fit;
    private string _text;
    private FontChoice? _selectedFont;
    private string _fontSizeText;
    private bool _bold;
    private int _offsetY;
    private string _color;
    private string _tileColor;
    private bool _isSymbol;
    private string _search = string.Empty;
    private SymbolChoice? _symbol;
    private bool _overImage;
    private string _place;

    /// <summary>
    /// A symbol icon whose character the catalogue does not have (one a later version adds, come through sync): kept as
    /// it is until another symbol is chosen (release check S3).
    /// </summary>
    private readonly TextIcon? _keptSymbol;

    /// <param name="picture">The item's picture, which the text can be put over (0.14.0 WP6); the preview draws it under the text.</param>
    /// <param name="canOverImage">The item has a picture or a default icon to put the text over: the box is offered.</param>
    public TextIconViewModel(TextIcon? current, string suggestedText, ImageSource? picture = null, bool canOverImage = false)
        : this(TextWindowMode.Icon, current, suggestedText, picture, canOverImage, defaults: null, widgetName: null)
    {
    }

    private TextIconViewModel(
        TextWindowMode mode, TextIcon? current, string suggestedText, ImageSource? picture, bool canOverImage, TextLookDefaults? defaults, string? widgetName)
    {
        Mode = mode;
        _defaults = defaults;
        _widgetName = widgetName ?? string.Empty;
        // A caption or a widget starts from its own font, "(as before)", first in the list (0.15.0 WP7b). A font that came
        // with the look from a PC that has it, and this one does not, stays a choice so that OK here keeps it, when its name
        // passes the check every synced text shown in a window passes.
        _ownFont = defaults is null ? null : new FontChoice(string.Empty, Strings.TextIcon_OwnFont, defaults.Family);
        var missing = defaults is not null && current?.FontFamily is { } wanted && TextIconFonts.Find(wanted) is null
                      && !string.Equals(wanted, SymbolCatalog.FontFamily, StringComparison.OrdinalIgnoreCase)
                      && wanted.Length <= 100 && CommandTileSettings.IsShowable(wanted)
            ? new FontChoice(wanted.Trim(), string.Format(CultureInfo.CurrentCulture, Strings.TextIcon_MissingFont, wanted.Trim()), defaults.Family)
            : null;
        Fonts = _ownFont is null ? TextIconFonts.Installed
            : missing is null ? [_ownFont, .. TextIconFonts.Installed]
            : [_ownFont, missing, .. TextIconFonts.Installed];
        Picture = picture;
        CanOverImage = canOverImage && mode == TextWindowMode.Icon;
        _overImage = CanOverImage && current is { OverImage: true };
        _place = current?.Corner ?? TextIcon.PlaceCenter;
        // An icon that is a symbol opens on Symbol with its symbol chosen; the Text side then starts as for a new icon.
        _isSymbol = mode is not (TextWindowMode.Widget or TextWindowMode.WidgetName) && SymbolCatalog.IsSymbol(current) && current!.Text.Length > 0;
        var text = _isSymbol ? null : current;
        _text = text?.Text ?? Suggest(suggestedText);
        _selectedFont = TextIconFonts.Find(text?.FontFamily) ?? (text is null ? null : Fonts.FirstOrDefault(f => f.Source.Length > 0 && f.Source == text.FontFamily?.Trim()))
                        ?? _ownFont ?? TextIconFonts.DefaultChoice();
        _fontSizeText = (text?.FontSize ?? defaults?.Size ?? TextIcon.DefaultFontSize).ToString(CultureInfo.CurrentCulture);
        _bold = text?.Bold ?? defaults?.Bold ?? false;
        _offsetY = current?.OffsetY ?? 0; // on both sides, like the colours
        _fit = text?.Fit ?? false;
        _color = current?.Color ?? string.Empty;
        _tileColor = current?.Background ?? string.Empty;

        Symbols = SymbolCatalog.All.Select(entry => new SymbolChoice(entry)).ToList();
        var view = new ListCollectionView(Symbols.ToList());
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(SymbolChoice.GroupName)));
        view.Filter = item => SymbolCatalog.Matches(((SymbolChoice)item).Entry, _search);
        SymbolsView = view;
        _symbol = _isSymbol ? Symbols.FirstOrDefault(choice => choice.Text == current!.Text) : null;
        _keptSymbol = _isSymbol && _symbol is null ? current : null;

        SelectTileColorCommand = new RelayCommand<string>(color => TileColor = color ?? string.Empty);
        ClearTileColorCommand = new RelayCommand(() => TileColor = string.Empty);
    }

    /// <summary>The size the preview is drawn at: the bar's icon size (0.12.0 A2).</summary>
    public int PreviewSize { get; init; } = LauncherSettings.DefaultIconSize;

    /// <summary>
    /// The window for a row line's caption (0.15.0 WP7b): its text, empty for none, or a symbol (drawn in the symbol font),
    /// and its look. <paramref name="defaults"/> is the caption as it is drawn without a look.
    /// </summary>
    public static TextIconViewModel ForCaption(string? caption, TextLook? look, TextLookDefaults defaults)
    {
        ArgumentNullException.ThrowIfNull(defaults);
        var text = Showable(caption?.Trim() ?? string.Empty, MaxCaptionLength);
        var symbol = text.Length > 0 && string.Equals(look?.Font, SymbolCatalog.FontFamily, StringComparison.OrdinalIgnoreCase);
        return new TextIconViewModel(TextWindowMode.Caption, StartingIcon(text, look, defaults, symbol), string.Empty, null, false, defaults, null);
    }

    /// <summary>
    /// The window for a widget's own text (0.15.0 WP7b): the look only, with <paramref name="sample"/>, what the widget shows
    /// now, where the text box is. <paramref name="name"/> goes in the title.
    /// </summary>
    public static TextIconViewModel ForWidget(string name, string sample, TextLook? look, TextLookDefaults defaults)
    {
        ArgumentNullException.ThrowIfNull(defaults);
        return new TextIconViewModel(
            TextWindowMode.Widget, StartingIcon(Showable(sample ?? string.Empty, MaxCaptionLength), look, defaults, symbol: false), string.Empty, null, false, defaults,
            Showable(name ?? string.Empty, MaxCaptionLength));
    }

    /// <summary>
    /// The window for a widget's name (0.16.0 WP4): the look only, with the name drawn as the bar draws it under the tile;
    /// <paramref name="defaults"/> is the bar's name without a look, its size in the bar's units.
    /// </summary>
    public static TextIconViewModel ForWidgetName(string name, TextLook? look, TextLookDefaults defaults)
    {
        ArgumentNullException.ThrowIfNull(defaults);
        var shown = Showable(name ?? string.Empty, MaxCaptionLength);
        return new TextIconViewModel(TextWindowMode.WidgetName, StartingIcon(shown, look, defaults, symbol: false), string.Empty, null, false, defaults, shown);
    }

    /// <summary>
    /// <paramref name="text"/> as a window may show it (0.15.0 WP7b): a caption that came through sync, a widget's output and
    /// name pass the check every synced text shown in a window passes (<see cref="CommandTileSettings.IsShowable"/>). What
    /// does not loses the characters that change how the rest reads without being seen (controls, format characters, line
    /// and paragraph separators), other spaces become plain ones and a long run of them is cut to the longest allowed, so
    /// the box shows what OK saves. At most <paramref name="max"/> characters.
    /// </summary>
    public static string Showable(string text, int max)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!CommandTileSettings.IsShowable(text))
        {
            var kept = new System.Text.StringBuilder(text.Length);
            foreach (var c in text)
            {
                var category = char.GetUnicodeCategory(c);
                if (category is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator)
                {
                    continue;
                }

                var plain = category == UnicodeCategory.SpaceSeparator ? ' ' : c;
                if (plain == ' ' && kept.Length >= CommandTileSettings.MaxSpaceRun
                    && kept.ToString(kept.Length - CommandTileSettings.MaxSpaceRun, CommandTileSettings.MaxSpaceRun).All(k => k == ' '))
                {
                    continue;
                }

                kept.Append(plain);
            }

            text = kept.ToString();
        }

        return text.Length > max ? text[..max] : text;
    }

    /// <summary>The look's values as the fields a text icon has, which the window starts from.</summary>
    private static TextIcon StartingIcon(string text, TextLook? look, TextLookDefaults defaults, bool symbol) =>
        new(text, look?.Font, look?.Size ?? defaults.Size, look?.Bold ?? defaults.Bold, look?.Color, Fit: symbol, look?.TileColor)
        {
            OffsetY = look?.OffsetY ?? 0,
        };

    /// <summary>What the window sets.</summary>
    public TextWindowMode Mode { get; }

    public bool IsIconMode => Mode == TextWindowMode.Icon;

    public bool IsCaptionMode => Mode == TextWindowMode.Caption;

    public bool IsWidgetMode => Mode == TextWindowMode.Widget;

    /// <summary>A widget's name (0.16.0 WP4): the preview draws it as the bar's name line.</summary>
    public bool IsWidgetNameMode => Mode == TextWindowMode.WidgetName;

    /// <summary>A widget's value or name: the look only, the text read-only, no sides (0.15.0 WP7b, 0.16.0 WP4).</summary>
    private bool IsWidgetPart => Mode is TextWindowMode.Widget or TextWindowMode.WidgetName;

    /// <summary>The title says what is being set: 文字或符號圖示, 標題文字, or "&lt;name&gt; 的文字".</summary>
    public string Title => Mode switch
    {
        TextWindowMode.Caption => Strings.TextIcon_TitleCaption,
        TextWindowMode.Widget or TextWindowMode.WidgetName => string.Format(CultureInfo.CurrentCulture, Strings.TextIcon_TitleWidget, _widgetName),
        _ => Strings.TextIcon_Title,
    };

    /// <summary>The Text and Symbol switch: a widget has neither, it shows its own text.</summary>
    public bool ShowsSides => !IsWidgetPart;

    /// <summary>The text box is the widget's sample, which cannot be changed here.</summary>
    public bool IsTextReadOnly => IsWidgetPart;

    /// <summary>The label of the text row: 文字, 標題 or 目前顯示.</summary>
    public string TextLabel => Mode switch
    {
        TextWindowMode.Caption => Strings.TextIcon_Caption,
        TextWindowMode.Widget => Strings.TextIcon_Sample,
        TextWindowMode.WidgetName => Strings.TextLook_NameLabel, // with its colon, as the rows under it (review 43 V2)
        _ => Strings.TextIcon_Text,
    };

    /// <summary>The label of the tile colour's row: a caption's is a plate behind its text.</summary>
    public string TileColorLabel => Mode is TextWindowMode.Caption or TextWindowMode.WidgetName ? Strings.TextIcon_PlateColour : Strings.TextIcon_TileColour;

    public IReadOnlyList<FontChoice> Fonts { get; }

    public IReadOnlyList<double> FontSizes => TextIcon.SuggestedFontSizes;

    public int MaxTextLength => Mode == TextWindowMode.Caption ? MaxCaptionLength : TextIcon.MaxTextLength;

    // ----- the two sides --------------------------------------------------------------------------

    /// <summary>The Symbol side is shown.</summary>
    public bool IsSymbol
    {
        get => _isSymbol;
        set
        {
            if (SetProperty(ref _isSymbol, value))
            {
                OnPropertyChanged(nameof(IsText));
                OnPropertyChanged(nameof(ShowsHint));
                NotifyIcon();
            }
        }
    }

    /// <summary>The Text side is shown.</summary>
    public bool IsText
    {
        get => !_isSymbol;
        set => IsSymbol = !value;
    }

    // ----- over the picture (0.14.0 WP6) ------------------------------------------------------------

    /// <summary>The item's picture, under the text in the preview while <see cref="OverImage"/>.</summary>
    public ImageSource? Picture { get; }

    /// <summary>The item has a picture or a default icon: <em>Keep the icon picture</em> is offered.</summary>
    public bool CanOverImage { get; }

    /// <summary>Keep the item's picture and draw the text or symbol over it, on both sides.</summary>
    public bool OverImage
    {
        get => _overImage;
        set
        {
            if (SetProperty(ref _overImage, value && CanOverImage))
            {
                OnPropertyChanged(nameof(ShowsPicture));
                OnPropertyChanged(nameof(Hint));
                OnPropertyChanged(nameof(ShowsHint));
                NotifyIcon();
            }
        }
    }

    /// <summary>The preview draws the item's picture under the text.</summary>
    public bool ShowsPicture => _overImage;

    public bool IsPlaceCenter
    {
        get => _place == TextIcon.PlaceCenter;
        set => SetPlace(TextIcon.PlaceCenter, value);
    }

    public bool IsPlaceTopLeft
    {
        get => _place == TextIcon.PlaceTopLeft;
        set => SetPlace(TextIcon.PlaceTopLeft, value);
    }

    public bool IsPlaceTopRight
    {
        get => _place == TextIcon.PlaceTopRight;
        set => SetPlace(TextIcon.PlaceTopRight, value);
    }

    public bool IsPlaceBottomLeft
    {
        get => _place == TextIcon.PlaceBottomLeft;
        set => SetPlace(TextIcon.PlaceBottomLeft, value);
    }

    public bool IsPlaceBottomRight
    {
        get => _place == TextIcon.PlaceBottomRight;
        set => SetPlace(TextIcon.PlaceBottomRight, value);
    }

    private void SetPlace(string place, bool chosen)
    {
        if (!chosen || _place == place)
        {
            return;
        }

        _place = place;
        OnPropertyChanged(nameof(IsPlaceCenter));
        OnPropertyChanged(nameof(IsPlaceTopLeft));
        OnPropertyChanged(nameof(IsPlaceTopRight));
        OnPropertyChanged(nameof(IsPlaceBottomLeft));
        OnPropertyChanged(nameof(IsPlaceBottomRight));
        NotifyIcon();
    }

    /// <summary>The hint under the fields: the Text side's, or the one for text over the picture, on both sides; a caption's.</summary>
    public string Hint => Mode == TextWindowMode.Caption ? Strings.TextIcon_CaptionHint : _overImage ? Strings.TextIcon_HintOver : Strings.TextIcon_Hint;

    public bool ShowsHint => Mode switch
    {
        TextWindowMode.Caption => true,
        TextWindowMode.Widget or TextWindowMode.WidgetName => false,
        _ => _overImage || !_isSymbol,
    };

    // ----- Text ------------------------------------------------------------------------------------

    public string Text
    {
        get => _text;
        set
        {
            if (SetProperty(ref _text, value ?? string.Empty))
            {
                NotifyIcon();
            }
        }
    }

    public FontChoice? SelectedFont
    {
        get => _selectedFont;
        set
        {
            if (SetProperty(ref _selectedFont, value))
            {
                NotifyIcon();
            }
        }
    }

    public string FontSizeText
    {
        get => _fontSizeText;
        set
        {
            if (SetProperty(ref _fontSizeText, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(IsFontSizeValid));
                NotifyIcon();
            }
        }
    }

    public bool Bold
    {
        get => _bold;
        set
        {
            if (SetProperty(ref _bold, value))
            {
                NotifyIcon();
            }
        }
    }

    public bool IsFontSizeValid => TryParseFontSize(out _);

    /// <summary>垂直位移 (0.15.0 WP7a): how far the text or symbol is moved down, in percent of the tile, −20 to +20.</summary>
    public int OffsetY
    {
        get => _offsetY;
        set
        {
            if (SetProperty(ref _offsetY, Math.Clamp(value, TextIcon.MinOffsetY, TextIcon.MaxOffsetY)))
            {
                OnPropertyChanged(nameof(OffsetText));
                NotifyIcon();
            }
        }
    }

    /// <summary>The offset as the row shows it: a sign and a percent (negative is up).</summary>
    public string OffsetText => string.Create(CultureInfo.CurrentCulture, $"{_offsetY:+0;−0;0} %");

    public int MinOffsetY => TextIcon.MinOffsetY;

    public int MaxOffsetY => TextIcon.MaxOffsetY;

    /// <summary>歸零: back to the middle.</summary>
    public void ResetOffset() => OffsetY = 0;

    // ----- Symbol ----------------------------------------------------------------------------------

    /// <summary>Every symbol of the catalogue, in its order.</summary>
    public IReadOnlyList<SymbolChoice> Symbols { get; }

    /// <summary>The symbols the search finds, under their group headings; a group with none is not shown.</summary>
    public ICollectionView SymbolsView { get; }

    /// <summary>The font the grid draws the symbols in.</summary>
    public FontFamily SymbolFontFamily { get; } = TextIconFonts.Resolve(SymbolCatalog.FontFamily);

    /// <summary>Filters the grid at once: every word must begin a word of a symbol's English name or keywords.</summary>
    public string SearchText
    {
        get => _search;
        set
        {
            if (SetProperty(ref _search, value ?? string.Empty))
            {
                SymbolsView.Refresh();
                OnPropertyChanged(nameof(HasNoMatch));
                OnPropertyChanged(nameof(SelectedSymbol)); // the grid shows the choice again once the search lets it through
            }
        }
    }

    /// <summary>The search hides every symbol: "No symbol matches." is shown.</summary>
    public bool HasNoMatch => SymbolsView.IsEmpty;

    /// <summary>
    /// The chosen symbol. The grid sets <see langword="null"/> only when the search hides the choice; that does not
    /// undo it, so the preview and OK keep it.
    /// </summary>
    public SymbolChoice? SelectedSymbol
    {
        get => _symbol;
        set
        {
            if (value is not null && SetProperty(ref _symbol, value))
            {
                NotifyIcon();
            }
        }
    }

    // ----- both sides ------------------------------------------------------------------------------

    /// <summary><c>#RRGGBB</c>, or empty for the theme's text colour (on a tile colour, white or black).</summary>
    public string Color
    {
        get => _color;
        set
        {
            if (SetProperty(ref _color, value?.Trim() ?? string.Empty))
            {
                OnPropertyChanged(nameof(IsColorValid));
                NotifyIcon();
            }
        }
    }

    public bool IsColorValid => _color.Length == 0 || LauncherSettings.IsValidColor(_color);

    /// <summary><c>#RRGGBB</c> of the tile, or empty for the theme's look.</summary>
    public string TileColor
    {
        get => _tileColor;
        set
        {
            if (SetProperty(ref _tileColor, value?.Trim() ?? string.Empty))
            {
                OnPropertyChanged(nameof(IsTileColorValid));
                NotifyIcon();
            }
        }
    }

    public bool IsTileColorValid => _tileColor.Length == 0 || LauncherSettings.IsValidColor(_tileColor);

    /// <summary>The swatches of Settings > Appearance.</summary>
    public IReadOnlyList<string> TileColors => SettingsViewModel.ColorPresets;

    public ICommand SelectTileColorCommand { get; }

    /// <summary>None: the theme's tile.</summary>
    public ICommand ClearTileColorCommand { get; }

    public bool CanSave => IsColorValid && IsTileColorValid
                           && (_isSymbol ? _symbol is not null || _keptSymbol is not null
                               : (_text.Trim().Length > 0 || Mode != TextWindowMode.Icon) && IsFontSizeValid); // an empty caption is none

    /// <summary>The icon as OK would save it, for the preview; "Aa" while the text is empty, nothing before a symbol is chosen.</summary>
    public TextIcon? PreviewIcon => Build(_text.Trim().Length == 0 ? "Aa" : null);

    /// <summary>The icon of the side shown.</summary>
    public TextIcon? ToTextIcon() => Build(null);

    /// <summary>
    /// The caption as OK saves it (0.15.0 WP7b): its text, empty for none, and its look; a symbol is its character in the
    /// symbol font, at the caption's size.
    /// </summary>
    public (string Text, TextLook? Look) ToCaption()
    {
        if (_isSymbol)
        {
            return (_symbol?.Text ?? _keptSymbol?.Text ?? string.Empty, LookOf(SymbolCatalog.FontFamily, withTextFields: false));
        }

        return (_text.Trim(), LookOf(_selectedFont is { Source.Length: > 0 } font ? font.Source : null, withTextFields: true));
    }

    /// <summary>The widget's look as OK saves it (0.15.0 WP7b); <see langword="null"/> when nothing differs from its own.</summary>
    public TextLook? ToLook() => LookOf(_selectedFont is { Source.Length: > 0 } font ? font.Source : null, withTextFields: true);

    /// <summary>The text the preview draws in Caption and Widget mode: the caption or the sample (a symbol's character).</summary>
    public string PreviewText => _isSymbol ? _symbol?.Text ?? _keptSymbol?.Text ?? string.Empty : _text.Trim();

    /// <summary>The look the preview draws in Caption and Widget mode.</summary>
    public TextLook? PreviewLook => Mode == TextWindowMode.Icon ? null : _isSymbol ? ToCaption().Look : ToLook();

    /// <summary>The caption's or the widget's text without a look (what the preview inherits): its size.</summary>
    public double DefaultSize => _defaults?.Size ?? TextIcon.DefaultFontSize;

    /// <summary>Its weight.</summary>
    public FontWeight DefaultWeight => _defaults?.Weight ?? FontWeights.Normal;

    /// <summary>Its font, the bar's.</summary>
    public FontFamily DefaultFamily => _defaults?.Family ?? SystemFonts.MessageFontFamily;

    /// <summary>A widget name's line, whose height its up or down is a percent of (0.16.0 WP4), as on the bar.</summary>
    public double NameShiftBase => DefaultSize * Views.LookedText.LineFactor;

    /// <summary>A widget's preview draws the plate as the tile's fill.</summary>
    public Brush? PreviewPlateBrush => _tileColor.Length > 0 && LauncherSettings.IsValidColor(_tileColor) ? ThemeManager.ToBrush(_tileColor) : null;

    /// <summary>
    /// The look of the fields shown: a size and a weight only when they differ from the text's own (<see cref="_defaults"/>),
    /// so that OK without a change leaves no look at all.
    /// </summary>
    private TextLook? LookOf(string? font, bool withTextFields)
    {
        var parsed = TryParseFontSize(out var size);
        return new TextLook
        {
            Font = font,
            Size = withTextFields && parsed && size != _defaults?.Size ? size : null,
            Bold = withTextFields && _bold != (_defaults?.Bold ?? false) ? _bold : null,
            Color = _color.Length == 0 ? null : _color,
            TileColor = _tileColor.Length == 0 ? null : _tileColor,
            OffsetY = _offsetY,
        }.Normalized();
    }

    /// <summary>The heading of <paramref name="group"/> in the UI language.</summary>
    public static string GroupName(SymbolGroup group) => group switch
    {
        SymbolGroup.Common => Strings.Symbol_GroupCommon,
        SymbolGroup.FilesAndFolders => Strings.Symbol_GroupFilesAndFolders,
        SymbolGroup.Communication => Strings.Symbol_GroupCommunication,
        SymbolGroup.Media => Strings.Symbol_GroupMedia,
        SymbolGroup.Devices => Strings.Symbol_GroupDevices,
        SymbolGroup.Network => Strings.Symbol_GroupNetwork,
        SymbolGroup.ToolsAndSettings => Strings.Symbol_GroupToolsAndSettings,
        SymbolGroup.PeopleAndPlaces => Strings.Symbol_GroupPeopleAndPlaces,
        SymbolGroup.ArrowsAndMarks => Strings.Symbol_GroupArrowsAndMarks,
        _ => group.ToString(),
    };

    private TextIcon? Build(string? emptyText)
    {
        var color = _color.Length == 0 ? null : _color;
        var tile = _tileColor.Length == 0 ? null : _tileColor;
        if (_isSymbol)
        {
            return _symbol is not null ? OverPicture(SymbolCatalog.Icon(_symbol.Entry, color, tile)).Normalized()
                : _keptSymbol is not null ? OverPicture(_keptSymbol with { Color = color, Background = tile }).Normalized() // its own character, as it came
                : null;
        }

        TryParseFontSize(out var size);
        return OverPicture(new TextIcon(emptyText ?? _text, _selectedFont?.Source, size, _bold, color, _fit, tile)).Normalized();
    }

    /// <summary>The icon with the choice of drawing it over the picture, and where (0.14.0 WP6).</summary>
    private TextIcon OverPicture(TextIcon icon) => icon with
    {
        OverImage = _overImage,
        Place = _overImage && _place != TextIcon.PlaceCenter ? _place : null,
        OffsetY = _offsetY, // up or down, on both sides (0.15.0 WP7a)
    };

    private void NotifyIcon()
    {
        OnPropertyChanged(nameof(PreviewIcon));
        OnPropertyChanged(nameof(PreviewText));
        OnPropertyChanged(nameof(PreviewLook));
        OnPropertyChanged(nameof(PreviewPlateBrush));
        OnPropertyChanged(nameof(CanSave));
    }

    private bool TryParseFontSize(out double size)
    {
        if (double.TryParse(_fontSizeText, NumberStyles.Float, CultureInfo.CurrentCulture, out size)
            || double.TryParse(_fontSizeText, NumberStyles.Float, CultureInfo.InvariantCulture, out size))
        {
            return size >= TextIcon.MinFontSize && size <= TextIcon.MaxFontSize;
        }

        size = TextIcon.DefaultFontSize;
        return false;
    }

    private static string Suggest(string name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        // Short names fit as they are; long Latin names shrink to their initials.
        if (trimmed.Length <= 4 || trimmed.Any(c => c > 0x2E7F))
        {
            return trimmed.Length <= TextIcon.MaxTextLength ? trimmed : trimmed[..TextIcon.MaxTextLength];
        }

        var initials = string.Concat(trimmed.Split([' ', '-', '_'], StringSplitOptions.RemoveEmptyEntries).Select(w => char.ToUpperInvariant(w[0])));
        return initials.Length is >= 2 and <= 4 ? initials : trimmed[..Math.Min(4, trimmed.Length)];
    }
}
