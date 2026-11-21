using System.Windows.Media;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Services;

namespace DesktopIconGrid.ViewModels;

/// <summary>
/// Settings ▸ Appearance: the custom theme's own hover colour (with its strength) and focus border colour (0.15.0
/// WP4b, the owner's request). Each is automatic, the colour derived from the background as before, until its box is
/// unticked; it then starts from the derived colour. The theme's preview shows an icon under the pointer and one with
/// the keyboard focus.
/// </summary>
public sealed partial class SettingsViewModel
{
    private bool _hoverAutomatic = true;
    private bool _focusAutomatic = true;
    private string _hoverColorText = string.Empty;
    private string _focusColorText = string.Empty;
    private int _hoverStrength = LauncherSettings.MinHoverStrength;

    private void ReadThemeColours(LauncherSettings current)
    {
        var derived = DerivedPalette();
        _hoverAutomatic = current.HoverColor is null;
        _focusAutomatic = current.FocusColor is null;
        _hoverColorText = current.HoverColor ?? ThemeCatalog.DerivedHoverColor(derived);
        _hoverStrength = current.HoverStrength ?? ThemeCatalog.DerivedHoverStrength(derived);
        _focusColorText = current.FocusColor ?? derived.Accent;
    }

    private void WriteThemeColours(LauncherSettings settings)
    {
        settings.HoverColor = !_hoverAutomatic && LauncherSettings.IsValidColor(_hoverColorText) ? _hoverColorText.ToUpperInvariant() : null;
        settings.HoverStrength = settings.HoverColor is null ? null : _hoverStrength;
        settings.FocusColor = !_focusAutomatic && LauncherSettings.IsValidColor(_focusColorText) ? _focusColorText.ToUpperInvariant() : null;
    }

    /// <summary>
    /// The custom theme as the background now gives it, without colours of its own; with a picture this PC has, its text
    /// chosen for the picture blended into the colour (0.15.0 WP5).
    /// </summary>
    private ThemePalette DerivedPalette() => ThemeCatalog.CreateCustom(IsColorValid ? _backgroundColor : null, PictureBasis());

    /// <summary>The theme as it would look with what is chosen now: the custom one with its own colours, or the one chosen.</summary>
    private ThemePalette PreviewPalette() => IsCustomTheme
        ? ThemeCatalog.WithHoverAndFocus(DerivedPalette(), _hoverAutomatic ? null : _hoverColorText, _hoverStrength, _focusAutomatic ? null : _focusColorText)
        : _selectedTheme;

    /// <summary>The hover colour derived from the background while ticked; unticked, the box's, starting from it.</summary>
    public bool HoverAutomatic
    {
        get => _hoverAutomatic;
        set
        {
            if (SetProperty(ref _hoverAutomatic, value))
            {
                FollowDerived(hover: true);
                OnThemeColoursChanged();
            }
        }
    }

    public string HoverColorText
    {
        get => _hoverColorText;
        set
        {
            if (SetProperty(ref _hoverColorText, value?.Trim() ?? string.Empty))
            {
                OnThemeColoursChanged();
            }
        }
    }

    public bool IsHoverColorValid => _hoverAutomatic || LauncherSettings.IsValidColor(_hoverColorText);

    /// <summary>How strongly the hover colour lies over an item, 10 to 100 %.</summary>
    public int HoverStrength
    {
        get => _hoverStrength;
        set
        {
            if (SetProperty(ref _hoverStrength, Math.Clamp(value, LauncherSettings.MinHoverStrength, LauncherSettings.MaxHoverStrength)))
            {
                OnPropertyChanged(nameof(HoverStrengthText));
                OnThemeColoursChanged();
            }
        }
    }

    public string HoverStrengthText => string.Create(System.Globalization.CultureInfo.CurrentCulture, $"{_hoverStrength} %");

    public int MinHoverStrength => LauncherSettings.MinHoverStrength;

    public int MaxHoverStrength => LauncherSettings.MaxHoverStrength;

    /// <summary>The hover colour in use, solid: the box's while it holds one, else the derived one.</summary>
    public Brush HoverChipBrush => ThemeManager.ToBrush(!_hoverAutomatic && LauncherSettings.IsValidColor(_hoverColorText)
        ? _hoverColorText
        : ThemeCatalog.DerivedHoverColor(DerivedPalette()));

    /// <summary>The focus border colour derived from the background (the accent) while ticked; unticked, the box's.</summary>
    public bool FocusAutomatic
    {
        get => _focusAutomatic;
        set
        {
            if (SetProperty(ref _focusAutomatic, value))
            {
                FollowDerived(hover: false);
                OnThemeColoursChanged();
            }
        }
    }

    public string FocusColorText
    {
        get => _focusColorText;
        set
        {
            if (SetProperty(ref _focusColorText, value?.Trim() ?? string.Empty))
            {
                OnThemeColoursChanged();
            }
        }
    }

    public bool IsFocusColorValid => _focusAutomatic || LauncherSettings.IsValidColor(_focusColorText);

    public Brush FocusChipBrush => ThemeManager.ToBrush(!_focusAutomatic && LauncherSettings.IsValidColor(_focusColorText)
        ? _focusColorText
        : DerivedPalette().Accent);

    /// <summary>
    /// A chosen focus colour under 3 : 1 against the background: keyboard focus may be hard to see. A warning only; Save
    /// still works. The automatic colour is not judged: there is nothing to choose then.
    /// </summary>
    public bool IsFocusHardToSee => IsCustomTheme && !_focusAutomatic && LauncherSettings.IsValidColor(_focusColorText)
        && ThemeCatalog.IsFocusHardToSee(_focusColorText, PictureBasis() ?? DerivedPalette().Background);

    public Brush PreviewBorderBrush => ThemeManager.ToBrush(PreviewPalette().Border);

    public Brush PreviewForegroundBrush => ThemeManager.ToBrush(PreviewPalette().Foreground);

    /// <summary>The preview's icon under the pointer: its overlay and its outline.</summary>
    public Brush PreviewPointerOverBrush => ThemeManager.ToBrush(PreviewPalette().PointerOver);

    public Brush PreviewPointerOverBorderBrush => ThemeManager.ToBrush(PreviewPalette().PointerOverBorder);

    /// <summary>The preview's icon with the keyboard focus: its border.</summary>
    public Brush PreviewFocusBrush => ThemeManager.ToBrush(PreviewPalette().Focus);

    /// <summary>While a colour is automatic its box shows the derived one, which follows the background.</summary>
    private void FollowDerived(bool hover)
    {
        var derived = DerivedPalette();
        if (hover)
        {
            // Ticked: the box shows the derived colour again. Unticked: it starts from it, with the derived strength.
            _hoverColorText = ThemeCatalog.DerivedHoverColor(derived);
            _hoverStrength = ThemeCatalog.DerivedHoverStrength(derived);
            OnPropertyChanged(nameof(HoverColorText));
            OnPropertyChanged(nameof(HoverStrength));
            OnPropertyChanged(nameof(HoverStrengthText));
        }
        else
        {
            _focusColorText = derived.Accent;
            OnPropertyChanged(nameof(FocusColorText));
        }
    }

    private void OnThemeColoursChanged()
    {
        if (_hoverAutomatic && !string.Equals(_hoverColorText, ThemeCatalog.DerivedHoverColor(DerivedPalette()), StringComparison.OrdinalIgnoreCase))
        {
            FollowDerived(hover: true);
        }

        if (_focusAutomatic && !string.Equals(_focusColorText, DerivedPalette().Accent, StringComparison.OrdinalIgnoreCase))
        {
            FollowDerived(hover: false);
        }

        foreach (var name in new[]
        {
            nameof(IsHoverColorValid), nameof(HoverChipBrush), nameof(IsFocusColorValid), nameof(FocusChipBrush), nameof(IsFocusHardToSee),
            nameof(PreviewBorderBrush), nameof(PreviewForegroundBrush), nameof(PreviewPointerOverBrush), nameof(PreviewPointerOverBorderBrush), nameof(PreviewFocusBrush),
        })
        {
            OnPropertyChanged(name);
        }
    }
}
