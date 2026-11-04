using System.Windows.Media;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Infrastructure;
using DesktopIconGrid.Services;

namespace DesktopIconGrid.ViewModels;

public sealed class SettingsViewModel : ObservableObject
{
    public static readonly IReadOnlyList<string> ColorPresets =
    [
        LauncherSettings.DefaultBackgroundColor, "#2B2B2B", "#3C3F41", "#1E1E2E", "#2D4A6E",
        "#4A3B5C", "#5B6B3A", "#F3F3F3", "#E6E1D8", "#FFFFFF",
    ];

    private int _iconSize;
    private int _cellPadding;
    private bool _showLabels;
    private bool _doubleClickToLaunch;
    private bool _alwaysOnTop;
    private string _backgroundColor;
    private ThemePalette _selectedTheme;
    private string _toggleHotkey;

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

        Themes = [.. ThemeCatalog.Builtin, ThemeCatalog.CreateCustom(current.BackgroundColor)];
        _selectedTheme = Themes.FirstOrDefault(t => string.Equals(t.Id, current.Theme, StringComparison.OrdinalIgnoreCase)) ?? Themes[0];

        SelectPresetCommand = new RelayCommand<string>(preset => BackgroundColor = preset);
    }

    public IReadOnlyList<int> IconSizes => LauncherSettings.IconSizes;

    public IReadOnlyList<string> Presets => ColorPresets;

    /// <summary>Built-in themes followed by "Custom".</summary>
    public IReadOnlyList<ThemePalette> Themes { get; }

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
            ToggleHotkey = IsHotkeyValid && HasHotkey ? _toggleHotkey : null,
        };
        settings.Normalize();
        return settings;
    }
}
