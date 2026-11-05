using System.Windows.Media;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Infrastructure;
using DesktopIconGrid.Resources;
using DesktopIconGrid.Services;

namespace DesktopIconGrid.ViewModels;

/// <summary>One choice in the bar-background list.</summary>
public sealed record BackdropOption(string Id, string Name);

/// <summary>One choice in the dock-edge list.</summary>
public sealed record DockEdgeOption(string Id, string Name);

/// <summary>One choice in the language list; the name is written in its own language.</summary>
public sealed record LanguageOption(string Id, string Name);

public sealed class SettingsViewModel : ObservableObject
{
    public static readonly IReadOnlyList<string> ColorPresets =
    [
        LauncherSettings.DefaultBackgroundColor, "#2B2B2B", "#3C3F41", "#1E1E2E", "#2D4A6E",
        "#4A3B5C", "#5B6B3A", "#F3F3F3", "#E6E1D8", "#FFFFFF",
    ];

    public static readonly IReadOnlyList<BackdropOption> BackdropOptions =
    [
        new(LauncherSettings.BackdropSolid, Strings.Settings_BackdropSolid),
        new(LauncherSettings.BackdropMica, Strings.Settings_BackdropMica),
        new(LauncherSettings.BackdropAcrylic, Strings.Settings_BackdropAcrylic),
    ];

    public static readonly IReadOnlyList<DockEdgeOption> DockEdgeOptions =
    [
        new(LauncherSettings.DockNone, Strings.Settings_DockNone),
        new(LauncherSettings.DockTop, Strings.Settings_DockTop),
        new(LauncherSettings.DockBottom, Strings.Settings_DockBottom),
        new(LauncherSettings.DockLeft, Strings.Settings_DockLeft),
        new(LauncherSettings.DockRight, Strings.Settings_DockRight),
    ];

    private int _iconSize;
    private int _cellPadding;
    private bool _showLabels;
    private bool _doubleClickToLaunch;
    private bool _alwaysOnTop;
    private string _backgroundColor;
    private ThemePalette _selectedTheme;
    private BackdropOption _selectedBackdrop;
    private bool _fluentDialogs;
    private string _toggleHotkey;
    private string _pointerHotkey;
    private int _transparency;
    private bool _closeToTray;
    private bool _startWithWindows;
    private bool _startHidden;
    private DockEdgeOption _selectedDockEdge;
    private bool _autoHide;
    private LanguageOption _selectedLanguage;

    /// <summary>The settings as they were; <see cref="ToSettings"/> starts from them, so whatever the dialog does not show is kept.</summary>
    private readonly LauncherSettings _original;

    public SettingsViewModel(LauncherSettings current)
    {
        ArgumentNullException.ThrowIfNull(current);
        _original = current.Clone();
        _iconSize = current.IconSize;
        _cellPadding = current.CellPadding;
        _showLabels = current.ShowLabels;
        _doubleClickToLaunch = current.DoubleClickToLaunch;
        _alwaysOnTop = current.AlwaysOnTop;
        _backgroundColor = current.BackgroundColor;
        _toggleHotkey = current.ToggleHotkey ?? string.Empty;
        _pointerHotkey = current.ShowAtPointerHotkey ?? string.Empty;
        _transparency = LauncherSettings.MaxOpacity - current.Opacity;
        _closeToTray = current.CloseToTray;
        _fluentDialogs = current.FluentDialogs;
        _startWithWindows = current.StartWithWindows;
        _startHidden = current.StartHidden;

        Themes = [.. ThemeCatalog.Builtin, ThemeCatalog.CreateCustom(current.BackgroundColor)];
        _selectedTheme = Themes.FirstOrDefault(t => string.Equals(t.Id, current.Theme, StringComparison.OrdinalIgnoreCase)) ?? Themes[0];
        _selectedBackdrop = BackdropOptions.FirstOrDefault(b => string.Equals(b.Id, current.BarBackdrop, StringComparison.OrdinalIgnoreCase)) ?? BackdropOptions[0];
        _selectedDockEdge = DockEdgeOptions.FirstOrDefault(d => string.Equals(d.Id, current.DockEdge, StringComparison.OrdinalIgnoreCase)) ?? DockEdgeOptions[0];
        _autoHide = current.AutoHide;
        Languages = LanguageOptions();
        _selectedLanguage = Languages.FirstOrDefault(l => string.Equals(l.Id, current.Language, StringComparison.OrdinalIgnoreCase)) ?? Languages[0];

        SelectPresetCommand = new RelayCommand<string>(preset => BackgroundColor = preset);
    }

    /// <summary>"Same as Windows" (in the current UI language) followed by every shipped language, each in its own language.</summary>
    public static IReadOnlyList<LanguageOption> LanguageOptions() =>
    [
        new(LauncherSettings.LanguageSystem, Strings.Settings_LanguageSystem),
        new(LauncherSettings.LanguageEnglish, "English"),
        new(LauncherSettings.LanguageTraditionalChinese, "繁體中文"),
        new(LauncherSettings.LanguageSimplifiedChinese, "简体中文"),
    ];

    public IReadOnlyList<LanguageOption> Languages { get; }

    public LanguageOption SelectedLanguage
    {
        get => _selectedLanguage;
        set => SetProperty(ref _selectedLanguage, value ?? Languages[0]);
    }

    public IReadOnlyList<int> IconSizes => LauncherSettings.IconSizes;

    public IReadOnlyList<string> Presets => ColorPresets;

    /// <summary>Built-in themes followed by "Custom".</summary>
    public IReadOnlyList<ThemePalette> Themes { get; }

    public IReadOnlyList<BackdropOption> Backdrops => BackdropOptions;

    public IReadOnlyList<DockEdgeOption> DockEdges => DockEdgeOptions;

    public int MinCellPadding => LauncherSettings.MinCellPadding;

    public int MaxCellPadding => LauncherSettings.MaxCellPadding;

    public int IconSize
    {
        get => _iconSize;
        set => SetProperty(ref _iconSize, value);
    }

    public int CellPadding
    {
        get => _cellPadding;
        set
        {
            if (SetProperty(ref _cellPadding, value))
            {
                OnPropertyChanged(nameof(CellPaddingText));
            }
        }
    }

    public string CellPaddingText => $"{_cellPadding} px";

    public bool ShowLabels
    {
        get => _showLabels;
        set => SetProperty(ref _showLabels, value);
    }

    public bool DoubleClickToLaunch
    {
        get => _doubleClickToLaunch;
        set
        {
            if (SetProperty(ref _doubleClickToLaunch, value))
            {
                OnPropertyChanged(nameof(SingleClickToLaunch));
            }
        }
    }

    public bool SingleClickToLaunch
    {
        get => !_doubleClickToLaunch;
        set => DoubleClickToLaunch = !value;
    }

    public bool AlwaysOnTop
    {
        get => _alwaysOnTop;
        set => SetProperty(ref _alwaysOnTop, value);
    }

    public ThemePalette SelectedTheme
    {
        get => _selectedTheme;
        set
        {
            if (value is not null && SetProperty(ref _selectedTheme, value))
            {
                OnPropertyChanged(nameof(IsCustomTheme));
                OnPropertyChanged(nameof(PreviewBrush));
            }
        }
    }

    public bool IsCustomTheme => _selectedTheme.IsCustom;

    public string BackgroundColor
    {
        get => _backgroundColor;
        set
        {
            if (SetProperty(ref _backgroundColor, value?.Trim() ?? string.Empty))
            {
                OnPropertyChanged(nameof(IsColorValid));
                OnPropertyChanged(nameof(PreviewBrush));
                OnPropertyChanged(nameof(CanSave));
            }
        }
    }

    public bool IsColorValid => LauncherSettings.IsValidColor(_backgroundColor);

    /// <summary>The bar background the current choice produces.</summary>
    public Brush PreviewBrush => IsCustomTheme
        ? (IsColorValid ? ThemeManager.ToBrush(_backgroundColor) : Brushes.Transparent)
        : ThemeManager.ToBrush(_selectedTheme.Background);

    /// <summary>Solid colour, Mica or Acrylic.</summary>
    public BackdropOption SelectedBackdrop
    {
        get => _selectedBackdrop;
        set
        {
            if (value is not null && SetProperty(ref _selectedBackdrop, value))
            {
                OnPropertyChanged(nameof(IsSolidBackdrop));
                if (!IsSolidBackdrop && Transparency == 0)
                {
                    Transparency = 35; // a backdrop behind an opaque bar is invisible; give it something to show through
                }
            }
        }
    }

    public bool IsSolidBackdrop => string.Equals(_selectedBackdrop.Id, LauncherSettings.BackdropSolid, StringComparison.Ordinal);

    /// <summary>Mica and Acrylic need Windows 11 22H2; elsewhere the list stays on the solid colour.</summary>
    public bool IsBackdropSupported => WindowBackdrop.IsSupported;

    public string BackdropHint => IsBackdropSupported
        ? Strings.Settings_BackdropHintSupported
        : Strings.Settings_BackdropHintUnsupported;

    /// <summary>Dialogs use the Windows 11 look and follow the system light/dark mode.</summary>
    public bool FluentDialogs
    {
        get => _fluentDialogs;
        set => SetProperty(ref _fluentDialogs, value);
    }

    public bool CloseToTray
    {
        get => _closeToTray;
        set => SetProperty(ref _closeToTray, value);
    }

    /// <summary>Start at sign-in through the current user's Run key.</summary>
    public bool StartWithWindows
    {
        get => _startWithWindows;
        set => SetProperty(ref _startWithWindows, value);
    }

    /// <summary>Only meaningful with <see cref="StartWithWindows"/>: begin in the tray, without showing the bar.</summary>
    public bool StartHidden
    {
        get => _startHidden;
        set => SetProperty(ref _startHidden, value);
    }

    /// <summary>Free window, or docked to a screen edge as a desktop toolbar.</summary>
    public DockEdgeOption SelectedDockEdge
    {
        get => _selectedDockEdge;
        set
        {
            if (value is not null && SetProperty(ref _selectedDockEdge, value))
            {
                OnPropertyChanged(nameof(IsDocked));
            }
        }
    }

    public bool IsDocked => !string.Equals(_selectedDockEdge.Id, LauncherSettings.DockNone, StringComparison.Ordinal);

    /// <summary>With a dock edge: collapse when the pointer leaves, return when it touches the edge.</summary>
    public bool AutoHide
    {
        get => _autoHide;
        set => SetProperty(ref _autoHide, value);
    }

    /// <summary>0 = opaque, up to <see cref="MaxTransparency"/>.</summary>
    public int Transparency
    {
        get => _transparency;
        set
        {
            if (SetProperty(ref _transparency, Math.Clamp(value, 0, MaxTransparency)))
            {
                OnPropertyChanged(nameof(TransparencyText));
            }
        }
    }

    public int MaxTransparency => LauncherSettings.MaxOpacity - LauncherSettings.MinOpacity;

    public string TransparencyText => _transparency == 0 ? Strings.Settings_Opaque : $"{_transparency} %";

    /// <summary>Text form of the show/hide hotkey, e.g. "Ctrl+Alt+Space"; empty for none.</summary>
    public string ToggleHotkey
    {
        get => _toggleHotkey;
        set
        {
            if (SetProperty(ref _toggleHotkey, value?.Trim() ?? string.Empty))
            {
                OnPropertyChanged(nameof(IsHotkeyValid));
                OnPropertyChanged(nameof(HasHotkey));
                OnHotkeysChanged();
            }
        }
    }

    public bool HasHotkey => _toggleHotkey.Length > 0;

    public bool IsHotkeyValid => _toggleHotkey.Length == 0 || HotkeyGesture.TryParse(_toggleHotkey, out _);

    /// <summary>Text form of the Show-at-pointer hotkey; empty for none.</summary>
    public string ShowAtPointerHotkey
    {
        get => _pointerHotkey;
        set
        {
            if (SetProperty(ref _pointerHotkey, value?.Trim() ?? string.Empty))
            {
                OnPropertyChanged(nameof(HasPointerHotkey));
                OnHotkeysChanged();
            }
        }
    }

    public bool HasPointerHotkey => _pointerHotkey.Length > 0;

    /// <summary>Both hotkeys are set to the same combination (however it is spelled): Windows would refuse the second.</summary>
    public bool IsPointerHotkeySame =>
        HotkeyGesture.TryParse(_pointerHotkey, out var pointer) && HotkeyGesture.TryParse(_toggleHotkey, out var toggle) && pointer == toggle;

    public bool IsPointerHotkeyValid => (_pointerHotkey.Length == 0 || HotkeyGesture.TryParse(_pointerHotkey, out _)) && !IsPointerHotkeySame;

    public bool CanSave => (!IsCustomTheme || IsColorValid) && IsHotkeyValid && IsPointerHotkeyValid;

    private void OnHotkeysChanged()
    {
        OnPropertyChanged(nameof(IsPointerHotkeySame));
        OnPropertyChanged(nameof(IsPointerHotkeyValid));
        OnPropertyChanged(nameof(CanSave));
    }

    public RelayCommand<string> SelectPresetCommand { get; }

    /// <summary>
    /// The settings with the dialog's values applied to a copy of the original, so settings the dialog does not
    /// show (and properties written by a newer version) are kept.
    /// </summary>
    public LauncherSettings ToSettings()
    {
        var settings = _original.Clone();
        settings.IconSize = _iconSize;
        settings.CellPadding = _cellPadding;
        settings.ShowLabels = _showLabels;
        settings.DoubleClickToLaunch = _doubleClickToLaunch;
        settings.AlwaysOnTop = _alwaysOnTop;
        settings.Theme = _selectedTheme.Id;
        settings.BackgroundColor = IsColorValid ? _backgroundColor : LauncherSettings.DefaultBackgroundColor;
        settings.BarBackdrop = _selectedBackdrop.Id;
        settings.FluentDialogs = _fluentDialogs;
        settings.ToggleHotkey = IsHotkeyValid && HasHotkey ? _toggleHotkey : null;
        settings.ShowAtPointerHotkey = IsPointerHotkeyValid && HasPointerHotkey ? _pointerHotkey : null;
        settings.Opacity = LauncherSettings.MaxOpacity - _transparency;
        settings.CloseToTray = _closeToTray;
        settings.StartWithWindows = _startWithWindows;
        settings.StartHidden = _startHidden;
        settings.DockEdge = _selectedDockEdge.Id;
        settings.AutoHide = _autoHide;
        settings.Language = _selectedLanguage.Id;
        settings.Normalize();
        return settings;
    }
}
