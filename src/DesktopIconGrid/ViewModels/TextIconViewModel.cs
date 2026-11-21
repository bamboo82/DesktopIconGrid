using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using DesktopIconGrid.Core.Models;
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

/// <summary>
/// Backs the Text icon window. The Text side is the characters, font, size and weight drawn instead of an image; the
/// Symbol side (0.12.0 WP3) is a symbol of <see cref="SymbolCatalog"/>, found by a search. Both have the text colour and
/// the tile colour. Switching sides keeps both sides' values while the window is open; OK saves the side shown.
/// </summary>
public sealed class TextIconViewModel : ObservableObject
{
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
    {
        Picture = picture;
        CanOverImage = canOverImage;
        _overImage = canOverImage && current is { OverImage: true };
        _place = current?.Corner ?? TextIcon.PlaceCenter;
        // An icon that is a symbol opens on Symbol with its symbol chosen; the Text side then starts as for a new icon.
        _isSymbol = SymbolCatalog.IsSymbol(current);
        var text = _isSymbol ? null : current;
        _text = text?.Text ?? Suggest(suggestedText);
        _selectedFont = TextIconFonts.Find(text?.FontFamily) ?? TextIconFonts.DefaultChoice();
        _fontSizeText = (text?.FontSize ?? TextIcon.DefaultFontSize).ToString(CultureInfo.CurrentCulture);
        _bold = text?.Bold ?? false;
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

    public IReadOnlyList<FontChoice> Fonts => TextIconFonts.Installed;

    public IReadOnlyList<double> FontSizes => TextIcon.SuggestedFontSizes;

    public int MaxTextLength => TextIcon.MaxTextLength;

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

    /// <summary>The hint under the fields: the Text side's, or the one for text over the picture, on both sides.</summary>
    public string Hint => _overImage ? Strings.TextIcon_HintOver : Strings.TextIcon_Hint;

    public bool ShowsHint => _overImage || !_isSymbol;

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
                           && (_isSymbol ? _symbol is not null || _keptSymbol is not null : _text.Trim().Length > 0 && IsFontSizeValid);

    /// <summary>The icon as OK would save it, for the preview; "Aa" while the text is empty, nothing before a symbol is chosen.</summary>
    public TextIcon? PreviewIcon => Build(_text.Trim().Length == 0 ? "Aa" : null);

    /// <summary>The icon of the side shown.</summary>
    public TextIcon? ToTextIcon() => Build(null);

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
