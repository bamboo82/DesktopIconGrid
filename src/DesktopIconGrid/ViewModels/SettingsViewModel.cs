using System.Windows.Media;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Infrastructure;

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

    public SettingsViewModel(LauncherSettings current)
    {
        ArgumentNullException.ThrowIfNull(current);
        _iconSize = current.IconSize;
        _cellPadding = current.CellPadding;
        _showLabels = current.ShowLabels;
        _doubleClickToLaunch = current.DoubleClickToLaunch;
        _alwaysOnTop = current.AlwaysOnTop;
        _backgroundColor = current.BackgroundColor;
        SelectPresetCommand = new RelayCommand<string>(preset => BackgroundColor = preset);
    }

    public IReadOnlyList<int> IconSizes => LauncherSettings.IconSizes;

    public IReadOnlyList<string> Presets => ColorPresets;

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

    public Brush PreviewBrush
    {
        get
        {
            var color = LauncherViewModel.TryParseColor(_backgroundColor) ?? Colors.Transparent;
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
    }

    public bool CanSave => IsColorValid;

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
            BackgroundColor = _backgroundColor,
        };
        settings.Normalize();
        return settings;
    }
}
