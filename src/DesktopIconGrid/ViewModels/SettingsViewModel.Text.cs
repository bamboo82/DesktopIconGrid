using System.Globalization;
using System.Windows.Markup;
using System.Windows.Media;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Resources;
using DesktopIconGrid.Services;

namespace DesktopIconGrid.ViewModels;

/// <summary>A font of the list: the family's name as it is stored (<see langword="null"/>: Windows' own), and as it is shown.</summary>
public sealed record UiFontOption(string? Id, string Name);

/// <summary>A text size of the list, in percent, and as it is shown.</summary>
public sealed record TextScaleOption(int Percent, string Name);

/// <summary>
/// Settings ▸ Appearance ▸ <em>Font</em> and <em>Text size</em> (0.13.0 WP4), around the Chinese font row: the font of the
/// app's text, Windows' own first, and the size of it, with a sample in both at once. They apply at Save, the bar at once
/// and a dialog when it is next opened.
/// </summary>
public sealed partial class SettingsViewModel
{
    private string? _uiFontBefore;
    private IReadOnlyList<UiFontOption>? _uiFontOptions;
    private UiFontOption? _selectedUiFont;
    private IReadOnlyList<TextScaleOption> _textScaleOptions = [];
    private TextScaleOption _selectedTextScale = new(100, "100 %");
    private Func<IEnumerable<FontFamily>> _families = UiFonts.InstalledFamilies;
    private string? _taipeiSource;

    /// <summary>The font and the size the dialog starts from (the constructor).</summary>
    private void InitText(LauncherSettings current)
    {
        _uiFontBefore = current.UiFont;
        var percent = current.TextScale ?? 100;
        _textScaleOptions = [.. LauncherSettings.TextScales.Append(percent).Distinct().Order().Select(value => new TextScaleOption(value, string.Format(CultureInfo.CurrentCulture, Strings.Settings_TextSizeValue, value)))];
        _selectedTextScale = _textScaleOptions.First(option => option.Percent == percent);
    }

    /// <summary>The families the list offers; tests give their own.</summary>
    internal void UseFamilies(Func<IEnumerable<FontFamily>> families)
    {
        _families = families ?? throw new ArgumentNullException(nameof(families));
        _uiFontOptions = null;
        _selectedUiFont = null;
        OnPropertyChanged(nameof(UiFontOptions));
        OnPropertyChanged(nameof(SelectedUiFont));
        OnPropertyChanged(nameof(SampleFontFamily));
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
                OnPropertyChanged(nameof(SampleFontFamily));
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
            }
        }
    }

    /// <summary>The sample beside the size: the chosen font, then the Chinese font chosen in its row, then Windows' own.</summary>
    public FontFamily SampleFontFamily => UiFonts.SampleFamily(SelectedUiFont.Id, SampleChineseSource);

    /// <summary>
    /// How much the sample is scaled: the chosen size against the size this dialog is drawn at, which is the size in use
    /// (it was scaled by it when it opened).
    /// </summary>
    public double SampleScale => SelectedTextScale.Percent / 100.0 / UiFonts.TextFactor;

    /// <summary>Taipei Sans TC Light, by its file, when it is chosen in the Chinese row and this PC has it.</summary>
    private string? SampleChineseSource =>
        _selectedChineseFont == TaipeiOption && _fontFile is { } file ? _taipeiSource ??= UiFonts.FromFile(file)?.Source : null;

    private List<UiFontOption> BuildUiFontOptions()
    {
        var options = new List<UiFontOption> { new(null, string.Format(CultureInfo.CurrentCulture, Strings.Settings_UiFontDefault, System.Windows.SystemFonts.MessageFontFamily.Source)) };
        var language = XmlLanguage.GetLanguage(CultureInfo.CurrentUICulture.IetfLanguageTag);
        var installed = _families()
            .Where(family => !string.IsNullOrWhiteSpace(family.Source) && Core.Fonts.UiFontChain.IsFamilyName(family.Source))
            .Select(family => new UiFontOption(family.Source, family.FamilyNames.TryGetValue(language, out var local) && !string.IsNullOrWhiteSpace(local) ? local : family.Source))
            .DistinctBy(option => option.Id, StringComparer.OrdinalIgnoreCase)
            .OrderBy(option => option.Name, StringComparer.Create(CultureInfo.CurrentCulture, ignoreCase: true))
            .ToList();
        if (_uiFontBefore is { } stored && !installed.Any(option => string.Equals(option.Id, stored, StringComparison.OrdinalIgnoreCase) || string.Equals(option.Name, stored, StringComparison.OrdinalIgnoreCase)))
        {
            options.Add(new UiFontOption(stored, string.Format(CultureInfo.CurrentCulture, Strings.Settings_UiFontNotHere, stored)));
        }
        else if (_uiFontBefore is { } named && installed.FirstOrDefault(option => string.Equals(option.Name, named, StringComparison.OrdinalIgnoreCase)) is { } byLocalName)
        {
            _uiFontBefore = byLocalName.Id; // stored by a local name: chosen by it all the same
        }

        options.AddRange(installed);
        return options;
    }
}
