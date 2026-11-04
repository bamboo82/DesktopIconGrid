using System.Windows.Media;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Infrastructure;
using DesktopIconGrid.Services;

namespace DesktopIconGrid.ViewModels;

/// <summary>One choice in the bar-background list.</summary>
public sealed record BackdropOption(string Id, string Name);

public sealed class SettingsViewModel : ObservableObject
{
    public static readonly IReadOnlyList<string> ColorPresets =
    [
        LauncherSettings.DefaultBackgroundColor, "#2B2B2B", "#3C3F41", "#1E1E2E", "#2D4A6E",
        "#4A3B5C", "#5B6B3A", "#F3F3F3", "#E6E1D8", "#FFFFFF",
    ];

    public static readonly IReadOnlyList<BackdropOption> BackdropOptions =
    [
        new(LauncherSettings.BackdropSolid, "Solid colour"),
        new(LauncherSettings.BackdropMica, "Mica (Windows 11)"),
        new(LauncherSettings.BackdropAcrylic, "Acrylic (Windows 11)"),
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
    private int _transparency;
    private bool _closeToTray;
    private bool _startWithWindows;
    private bool _startHidden;

    public SettingsViewModel(LauncherSettings current)
    {
        ArgumentNullException.ThrowIfNull(current);
        _iconSize = current.IconSize;
        _cellPadding = current.CellPadding;
        _showLabels = current.ShowLabels;
        _doubleClickToLaunch = current.DoubleClickToLaunch;
        _alwaysOnTop = current.AlwaysOnTop;
        _backgroundColor = current.BackgroundColor;
        _toggleHotkey = current.ToggleHotkey ?? string.Empty;
        _transparency = LauncherSettings.MaxOpacity - current.Opacity;
        _closeToTray = current.CloseToTray;
        _fluentDialogs = current.FluentDialogs;
        _startWithWindows = current.StartWithWindows;
        _startHidden = current.StartHidden;

        Themes = [.. ThemeCatalog.Builtin, ThemeCatalog.CreateCustom(current.BackgroundColor)];
        _selectedTheme = Themes.FirstOrDefault(t => string.Equals(t.Id, current.Theme, StringComparison.OrdinalIgnoreCase)) ?? Themes[0];
        _selectedBackdrop = BackdropOptions.FirstOrDefault(b => string.Equals(b.Id, current.BarBackdrop, StringComparison.OrdinalIgnoreCase)) ?? BackdropOptions[0];

        SelectPresetCommand = new RelayCommand<string>(preset => BackgroundColor = preset);
    }

    public IReadOnlyList<int> IconSizes => LauncherSettings.IconSizes;

    public IReadOnlyList<string> Presets => ColorPresets;

    /// <summary>Built-in themes followed by "Custom".</summary>
    public IReadOnlyList<ThemePalette> Themes { get; }

    public IReadOnlyList<BackdropOption> Backdrops => BackdropOptions;

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
        ? "Mica and Acrylic let the desktop show through with a blur; the theme colour tints it and the transparency slider sets how much of the blur shows. Icons stay sharp."
        : "Mica and Acrylic need Windows 11 (version 22H2 or later).";

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

    public string TransparencyText => _transparency == 0 ? "opaque" : $"{_transparency} %";

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
                OnPropertyChanged(nameof(CanSave));
            }
        }
    }

    public bool HasHotkey => _toggleHotkey.Length > 0;

    public bool IsHotkeyValid => _toggleHotkey.Length == 0 || HotkeyGesture.TryParse(_toggleHotkey, out _);

    public bool CanSave => (!IsCustomTheme || IsColorValid) && IsHotkeyValid;

    public RelayCommand<string> SelectPresetCommand { get; }

    public LauncherSettings ToSettings()
    {
        var settings = new LauncherSettings
        {
            IconSize = _iconSize,
            CellPadding = _cellPadding,
            ShowLabels = _showLabels,
            DoubleClickToLaunch = _doubleClickToLaunch,
            AlwaysOnTop = _alwaysOnTop,
            Theme = _selectedTheme.Id,
            BackgroundColor = IsColorValid ? _backgroundColor : LauncherSettings.DefaultBackgroundColor,
            BarBackdrop = _selectedBackdrop.Id,
            FluentDialogs = _fluentDialogs,
            ToggleHotkey = IsHotkeyValid && HasHotkey ? _toggleHotkey : null,
            Opacity = LauncherSettings.MaxOpacity - _transparency,
            CloseToTray = _closeToTray,
            StartWithWindows = _startWithWindows,
            StartHidden = _startHidden,
        };
        settings.Normalize();
        return settings;
    }
}
