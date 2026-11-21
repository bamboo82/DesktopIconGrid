using System.Globalization;
using System.Windows.Markup;
using System.Windows.Media;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Resources;
using DesktopIconGrid.Services;

namespace DesktopIconGrid.ViewModels;

/// <summary>
/// A font of a list: the family's name as it is stored, and as it is shown. In the bar's list <see langword="null"/> is
/// Windows' own; in the dialogs' (0.14.0 WP2) <see langword="null"/> is the bar's and an empty string Windows' own.
/// </summary>
public sealed record UiFontOption(string? Id, string Name);

/// <summary>A text size of the bar's list, in percent, and as it is shown.</summary>
public sealed record TextScaleOption(int Percent, string Name);

/// <summary>A text size of the dialogs' list (0.14.0 WP2): <see langword="null"/> is the bar's.</summary>
public sealed record DialogScaleOption(int? Percent, string Name);

/// <summary>
/// Settings ▸ Appearance ▸ <em>Font</em> and <em>Text size</em> (0.13.0 WP4), after the Chinese font row, twice since
/// 0.14.0 (WP2): under <em>Bar text</em> the font and the size of the bar's names, Windows' own first; under <em>Dialog and
/// menu text</em> those of the other windows and the menus, the bar's first. Each pair has a sample in both at once. They
/// apply at Save, the bar at once and a dialog when it is next opened.
/// </summary>
public sealed partial class SettingsViewModel
{
    private string? _uiFontBefore;
    private string? _dialogFontBefore;
    private List<UiFontOption>? _installedFonts;
    private IReadOnlyList<UiFontOption>? _uiFontOptions;
    private UiFontOption? _selectedUiFont;
    private IReadOnlyList<UiFontOption>? _dialogFontOptions;
    private UiFontOption? _selectedDialogFont;
    private IReadOnlyList<TextScaleOption> _textScaleOptions = [];
    private TextScaleOption _selectedTextScale = new(100, "100 %");
    private IReadOnlyList<DialogScaleOption> _dialogScaleOptions = [];
    private DialogScaleOption _selectedDialogScale = new(null, string.Empty);
    private Func<IEnumerable<FontFamily>> _families = UiFonts.InstalledFamilies;
    private string? _taipeiSource;

    /// <summary>The fonts and the sizes the dialog starts from (the constructor).</summary>
    private void InitText(LauncherSettings current)
    {
        _uiFontBefore = current.UiFont;
        _dialogFontBefore = current.DialogFont;
        var percent = current.TextScale ?? 100;
        _textScaleOptions = [.. Sizes(percent).Select(value => new TextScaleOption(value, SizeName(value)))];
        _selectedTextScale = _textScaleOptions.First(option => option.Percent == percent);
        _dialogScaleOptions =
        [
            new DialogScaleOption(null, Strings.Settings_SameAsBar),
            .. Sizes(current.DialogTextScale).Select(value => new DialogScaleOption(value, SizeName(value))),
        ];
        _selectedDialogScale = _dialogScaleOptions.First(option => option.Percent == current.DialogTextScale);
    }

    /// <summary>90, 100, 110, 125 and 150 %, and a size between them that a layout holds.</summary>
    private static IEnumerable<int> Sizes(int? stored) =>
        (stored is { } kept ? LauncherSettings.TextScales.Append(kept) : LauncherSettings.TextScales).Distinct().Order();

    private static string SizeName(int percent) => string.Format(CultureInfo.CurrentCulture, Strings.Settings_TextSizeValue, percent);

    /// <summary>The families the lists offer; tests give their own.</summary>
    internal void UseFamilies(Func<IEnumerable<FontFamily>> families)
    {
        _families = families ?? throw new ArgumentNullException(nameof(families));
        _installedFonts = null;
        _uiFontOptions = null;
        _selectedUiFont = null;
        _dialogFontOptions = null;
        _selectedDialogFont = null;
        OnPropertyChanged(nameof(UiFontOptions));
        OnPropertyChanged(nameof(SelectedUiFont));
        OnPropertyChanged(nameof(DialogFontOptions));
        OnPropertyChanged(nameof(SelectedDialogFont));
        OnSampleFontsChanged();
    }

    /// <summary>Both samples' fonts: the Chinese one, or the bar's that the dialogs may take, changed.</summary>
    private void OnSampleFontsChanged()
    {
        OnPropertyChanged(nameof(SampleFontFamily));
        OnPropertyChanged(nameof(DialogSampleFontFamily));
    }

    /// <summary>
    /// Windows' own first; a family the bar's setting names and this PC lacks next, said so (it syncs: another PC may
    /// have it, and Save keeps it); then the installed families by the name they have in the UI's language, each drawn in
    /// the dialog's own font. Made when it is first asked for: a PC has hundreds.
    /// </summary>
    public IReadOnlyList<UiFontOption> UiFontOptions => _uiFontOptions ??= BuildUiFontOptions();

    public UiFontOption SelectedUiFont
    {
        get => _selectedUiFont ??=
            UiFontOptions.FirstOrDefault(option => option.Id is not null && string.Equals(option.Id, _uiFontBefore, StringComparison.OrdinalIgnoreCase))
            ?? UiFontOptions[0];
        set
        {
            if (SetProperty(ref _selectedUiFont, value ?? UiFontOptions[0]))
            {
                OnSampleFontsChanged();
            }
        }
    }

    /// <summary>90, 100, 110, 125 and 150 %, and a size between them that a layout holds.</summary>
    public IReadOnlyList<TextScaleOption> TextScaleOptions => _textScaleOptions;

    public TextScaleOption SelectedTextScale
    {
        get => _selectedTextScale;
        set
        {
            if (SetProperty(ref _selectedTextScale, value ?? _textScaleOptions.First(option => option.Percent == 100)))
            {
                OnPropertyChanged(nameof(SampleScale));
                OnPropertyChanged(nameof(DialogSampleScale));
            }
        }
    }

    /// <summary>
    /// The bar's own first, said so; Windows' own next, which keeps the dialogs in it whatever the bar has; a family the
    /// setting names and this PC lacks, said so; then the families of the bar's list.
    /// </summary>
    public IReadOnlyList<UiFontOption> DialogFontOptions => _dialogFontOptions ??= BuildDialogFontOptions();

    public UiFontOption SelectedDialogFont
    {
        get => _selectedDialogFont ??=
            DialogFontOptions.FirstOrDefault(option => _dialogFontBefore is not null && string.Equals(option.Id, _dialogFontBefore, StringComparison.OrdinalIgnoreCase))
            ?? DialogFontOptions[0];
        set
        {
            if (SetProperty(ref _selectedDialogFont, value ?? DialogFontOptions[0]))
            {
                OnPropertyChanged(nameof(DialogSampleFontFamily));
            }
        }
    }

    /// <summary>The bar's size first, then 90 to 150 %, and a size between them that a layout holds.</summary>
    public IReadOnlyList<DialogScaleOption> DialogScaleOptions => _dialogScaleOptions;

    public DialogScaleOption SelectedDialogScale
    {
        get => _selectedDialogScale;
        set
        {
            if (SetProperty(ref _selectedDialogScale, value ?? _dialogScaleOptions[0]))
            {
                OnPropertyChanged(nameof(DialogSampleScale));
            }
        }
    }

    /// <summary>The bar's sample: the chosen font, then the Chinese font chosen in its row, then Windows' own.</summary>
    public FontFamily SampleFontFamily => UiFonts.SampleFamily(SelectedUiFont.Id, SampleChineseSource);

    /// <summary>
    /// How much the bar's sample is scaled: the chosen size against the size this dialog is drawn at, which is the
    /// dialogs' size in use (it was scaled by it when it opened).
    /// </summary>
    public double SampleScale => SelectedTextScale.Percent / 100.0 / UiFonts.TextFactor;

    /// <summary>The dialogs' sample: their font as chosen (the bar's while they take it), then the Chinese font, then Windows' own.</summary>
    public FontFamily DialogSampleFontFamily => UiFonts.SampleFamily(ChosenDialogFamily, SampleChineseSource);

    /// <summary>How much the dialogs' sample is scaled, as the bar's is: their size, or the bar's while they take it.</summary>
    public double DialogSampleScale => (SelectedDialogScale.Percent ?? SelectedTextScale.Percent) / 100.0 / UiFonts.TextFactor;

    /// <summary>The family the dialogs would ask for with what is chosen now: <see langword="null"/> for Windows' own.</summary>
    private string? ChosenDialogFamily => SelectedDialogFont.Id switch
    {
        null => SelectedUiFont.Id,
        "" => null,
        var family => family,
    };

    /// <summary>Taipei Sans TC Light, by its file, when it is chosen in the Chinese row and this PC has it.</summary>
    private string? SampleChineseSource =>
        _selectedChineseFont == TaipeiOption && _fontFile is { } file ? _taipeiSource ??= UiFonts.FromFile(file)?.Source : null;

    private List<UiFontOption> BuildUiFontOptions()
    {
        var options = new List<UiFontOption> { new(null, WindowsDefaultName) };
        _uiFontBefore = AddStored(options, _uiFontBefore);
        options.AddRange(InstalledFonts);
        return options;
    }

    private List<UiFontOption> BuildDialogFontOptions()
    {
        var options = new List<UiFontOption> { new(null, Strings.Settings_SameAsBar), new(string.Empty, WindowsDefaultName) };
        if (_dialogFontBefore is { Length: > 0 })
        {
            _dialogFontBefore = AddStored(options, _dialogFontBefore);
        }

        options.AddRange(InstalledFonts);
        return options;
    }

    private static string WindowsDefaultName => string.Format(CultureInfo.CurrentCulture, Strings.Settings_UiFontDefault, System.Windows.SystemFonts.MessageFontFamily.Source);

    /// <summary>
    /// A stored family this PC lacks goes in the list, said so (it syncs: another PC may have it, and Save keeps it); one
    /// stored by a local name is chosen by it all the same. Gives the name to choose by.
    /// </summary>
    private string? AddStored(List<UiFontOption> options, string? stored)
    {
        if (stored is null)
        {
            return null;
        }

        if (InstalledFonts.Any(option => string.Equals(option.Id, stored, StringComparison.OrdinalIgnoreCase)))
        {
            return stored;
        }

        if (InstalledFonts.FirstOrDefault(option => string.Equals(option.Name, stored, StringComparison.OrdinalIgnoreCase)) is { } byLocalName)
        {
            return byLocalName.Id;
        }

        options.Add(new UiFontOption(stored, string.Format(CultureInfo.CurrentCulture, Strings.Settings_UiFontNotHere, stored)));
        return stored;
    }

    /// <summary>The installed families by the name they have in the UI's language, once each; read once for both lists.</summary>
    private List<UiFontOption> InstalledFonts => _installedFonts ??= ReadInstalledFonts();

    private List<UiFontOption> ReadInstalledFonts()
    {
        var language = XmlLanguage.GetLanguage(CultureInfo.CurrentUICulture.IetfLanguageTag);
        return _families()
            .Where(family => !string.IsNullOrWhiteSpace(family.Source) && Core.Fonts.UiFontChain.IsFamilyName(family.Source))
            .Select(family => new UiFontOption(family.Source, family.FamilyNames.TryGetValue(language, out var local) && !string.IsNullOrWhiteSpace(local) ? local : family.Source))
            .DistinctBy(option => option.Id, StringComparer.OrdinalIgnoreCase)
            .OrderBy(option => option.Name, StringComparer.Create(CultureInfo.CurrentCulture, ignoreCase: true))
            .ToList();
    }
}
