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
        _voiceHotkey = current.VoiceHotkey ?? string.Empty;
        VoiceLanguages = VoiceLanguageOptions(InstalledVoiceLanguages(), current.VoiceLanguage);
        _selectedVoiceLanguage = VoiceLanguages.FirstOrDefault(l => string.Equals(l.Id, current.VoiceLanguage, StringComparison.OrdinalIgnoreCase)) ?? VoiceLanguages[0];
        WindowKeyRows = Enum.GetValues<WindowKeyAction>().Select(action => new WindowKeyRow(action, WindowKeys.For(current, action))).ToList();
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
                OnProblemsChanged(); // leaving the custom theme settles an unreadable colour
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
                OnProblemsChanged();
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

    public bool IsHotkeyValid => _toggleHotkey.Length == 0 || (HotkeyGesture.TryParse(_toggleHotkey, out _) && WindowKeyUsing(_toggleHotkey) is null);

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

    // ----- voice launch ------------------------------------------------------------------------------

    private string _voiceHotkey;
    private LanguageOption _selectedVoiceLanguage;

    /// <summary>The recognisers of this PC; tests set their own list.</summary>
    internal static Func<IReadOnlyList<string>> InstalledVoiceLanguages { get; set; } = () => new VoiceEar().InstalledLanguages();

    /// <summary>Text form of the voice hotkey; empty for none.</summary>
    public string VoiceHotkey
    {
        get => _voiceHotkey;
        set
        {
            if (SetProperty(ref _voiceHotkey, value?.Trim() ?? string.Empty))
            {
                OnPropertyChanged(nameof(HasVoiceHotkey));
                OnHotkeysChanged();
            }
        }
    }

    public bool HasVoiceHotkey => _voiceHotkey.Length > 0;

    /// <summary>The voice hotkey is one of the other two (however it is spelled): Windows would refuse it.</summary>
    public bool IsVoiceHotkeySame =>
        HotkeyGesture.TryParse(_voiceHotkey, out var voice)
        && ((HotkeyGesture.TryParse(_toggleHotkey, out var toggle) && voice == toggle) || (HotkeyGesture.TryParse(_pointerHotkey, out var pointer) && voice == pointer));

    public bool IsVoiceHotkeyValid =>
        (_voiceHotkey.Length == 0 || (HotkeyGesture.TryParse(_voiceHotkey, out _) && WindowKeyUsing(_voiceHotkey) is null)) && !IsVoiceHotkeySame;

    public string? VoiceHotkeyClash => WindowKeyUsing(_voiceHotkey) is { } action ? string.Format(Strings.Settings_HotkeyUsedInBar, _voiceHotkey, action) : null;

    public bool HasVoiceHotkeyClash => VoiceHotkeyClash is not null;

    /// <summary>"Same as the bar", then the recognisers this PC has, then the saved one when this PC has none for it (so saving here keeps it for the PC that has).</summary>
    public IReadOnlyList<LanguageOption> VoiceLanguages { get; }

    public LanguageOption SelectedVoiceLanguage
    {
        get => _selectedVoiceLanguage;
        set => SetProperty(ref _selectedVoiceLanguage, value ?? VoiceLanguages[0]);
    }

    /// <summary>Whether this PC has a recogniser at all; without one the hint says so.</summary>
    public bool HasNoVoiceRecogniser => VoiceLanguages.Count == 1 || (VoiceLanguages.Count == 2 && VoiceLanguages[1].Name.EndsWith('?'));

    internal static IReadOnlyList<LanguageOption> VoiceLanguageOptions(IReadOnlyList<string> installed, string? saved)
    {
        var options = new List<LanguageOption> { new(string.Empty, Strings.Settings_VoiceLanguageAuto) };
        options.AddRange(installed.Select(name => new LanguageOption(name, NameOfCulture(name))));
        if (!string.IsNullOrWhiteSpace(saved) && !installed.Contains(saved.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            options.Add(new LanguageOption(saved.Trim(), saved.Trim() + " ?"));
        }

        return options;
    }

    private static string NameOfCulture(string name)
    {
        try
        {
            return $"{System.Globalization.CultureInfo.GetCultureInfo(name).NativeName} ({name})";
        }
        catch (System.Globalization.CultureNotFoundException)
        {
            return name;
        }
    }

    /// <summary>Both hotkeys are set to the same combination (however it is spelled): Windows would refuse the second.</summary>
    public bool IsPointerHotkeySame =>
        HotkeyGesture.TryParse(_pointerHotkey, out var pointer) && HotkeyGesture.TryParse(_toggleHotkey, out var toggle) && pointer == toggle;

    public bool IsPointerHotkeyValid =>
        (_pointerHotkey.Length == 0 || (HotkeyGesture.TryParse(_pointerHotkey, out _) && WindowKeyUsing(_pointerHotkey) is null)) && !IsPointerHotkeySame;

    /// <summary>Why the show/hide hotkey cannot be kept (a window key has it, e.g. from a file of an older version), or <see langword="null"/>.</summary>
    public string? ToggleHotkeyClash => WindowKeyUsing(_toggleHotkey) is { } action ? string.Format(Strings.Settings_HotkeyUsedInBar, _toggleHotkey, action) : null;

    public string? PointerHotkeyClash => WindowKeyUsing(_pointerHotkey) is { } action ? string.Format(Strings.Settings_HotkeyUsedInBar, _pointerHotkey, action) : null;

    public bool HasToggleHotkeyClash => ToggleHotkeyClash is not null;

    public bool HasPointerHotkeyClash => PointerHotkeyClash is not null;

    /// <summary>The label of the bar's window key that has <paramref name="hotkey"/>, or <see langword="null"/>: a system-wide hotkey would take it.</summary>
    public string? WindowKeyUsing(string hotkey) =>
        string.IsNullOrWhiteSpace(hotkey) ? null : WindowKeyRows.FirstOrDefault(row => HotkeyGesture.SameCombination(row.Text, hotkey))?.Label;

    public bool CanSave => !HasAppearanceProblem && !HasKeysProblem && !HasVoiceProblem;

    // ----- tabs (review 27 B1) -----------------------------------------------------------------------

    /// <summary>The Appearance tab holds a value that cannot be saved: a custom background that is no colour.</summary>
    private bool HasAppearanceProblem => IsCustomTheme && !IsColorValid;

    /// <summary>The Keys tab: a hotkey a window key has, or the show-at-pointer hotkey equal to the show/hide one.</summary>
    private bool HasKeysProblem => !IsHotkeyValid || !IsPointerHotkeyValid;

    /// <summary>The Voice tab: its hotkey is a window key's or one of the other two hotkeys.</summary>
    private bool HasVoiceProblem => !IsVoiceHotkeyValid;

    public string AppearanceTab => TabHeader(Strings.Settings_TabAppearance, HasAppearanceProblem);

    public string WindowTab => Strings.Settings_TabWindow;

    public string KeysTab => TabHeader(Strings.Settings_TabKeys, HasKeysProblem);

    public string VoiceTab => TabHeader(Strings.Settings_TabVoice, HasVoiceProblem);

    public string GeneralTab => Strings.Settings_TabGeneral;

    /// <summary>The line beside Save: which tab, the first in the tab order, has something to fix; empty when none has.</summary>
    public string ProblemText =>
        HasAppearanceProblem ? string.Format(Strings.Settings_FixOnTab, Strings.Settings_TabAppearance)
        : HasKeysProblem ? string.Format(Strings.Settings_FixOnTab, Strings.Settings_TabKeys)
        : HasVoiceProblem ? string.Format(Strings.Settings_FixOnTab, Strings.Settings_TabVoice)
        : string.Empty;

    /// <summary>A tab's name, with a warning sign before it while one of its values cannot be saved.</summary>
    private static string TabHeader(string name, bool hasProblem) => hasProblem ? "\u26A0 " + name : name;

    private void OnProblemsChanged()
    {
        OnPropertyChanged(nameof(AppearanceTab));
        OnPropertyChanged(nameof(KeysTab));
        OnPropertyChanged(nameof(VoiceTab));
        OnPropertyChanged(nameof(ProblemText));
        OnPropertyChanged(nameof(CanSave));
    }

    private void OnHotkeysChanged()
    {
        OnPropertyChanged(nameof(ToggleHotkeyClash));
        OnPropertyChanged(nameof(PointerHotkeyClash));
        OnPropertyChanged(nameof(HasToggleHotkeyClash));
        OnPropertyChanged(nameof(HasPointerHotkeyClash));
        OnPropertyChanged(nameof(IsHotkeyValid));
        OnPropertyChanged(nameof(IsPointerHotkeySame));
        OnPropertyChanged(nameof(IsPointerHotkeyValid));
        OnPropertyChanged(nameof(VoiceHotkeyClash));
        OnPropertyChanged(nameof(HasVoiceHotkeyClash));
        OnPropertyChanged(nameof(IsVoiceHotkeySame));
        OnPropertyChanged(nameof(IsVoiceHotkeyValid));
        OnProblemsChanged();
    }

    public RelayCommand<string> SelectPresetCommand { get; }

    /// <summary>
    /// The settings with the dialog's values applied to a copy of the original, so settings the dialog does not
    /// show (and properties written by a newer version) are kept.
    /// </summary>
    /// <summary>The bar window's keys, one row per action (<see cref="WindowKeys"/>).</summary>
    public IReadOnlyList<WindowKeyRow> WindowKeyRows { get; }

    /// <summary>
    /// Gives <paramref name="row"/> the combination <paramref name="text"/> (empty: none), unless another key of the window
    /// or a system-wide hotkey already has it, which is said instead. Returns that refusal, or <see langword="null"/>.
    /// </summary>
    public string? SetWindowKey(WindowKeyRow row, string text)
    {
        ArgumentNullException.ThrowIfNull(row);
        text = text.Trim();
        if (text.Length > 0)
        {
            var taken = WindowKeyRows.FirstOrDefault(other => other != row && SameKey(other.Text, text))?.Label
                        ?? (SameKey(_toggleHotkey, text) ? Strings.WindowKey_ToggleHotkey : SameKey(_pointerHotkey, text) ? Strings.WindowKey_PointerHotkey : SameKey(_voiceHotkey, text) ? Strings.WindowKey_VoiceHotkey : null);
            if (taken is not null)
            {
                return string.Format(Strings.Settings_WindowKeyInUse, text, taken);
            }
        }

        row.Text = text;
        OnHotkeysChanged(); // changing the window key may settle a clash with a hotkey
        return null;
    }

    private static bool SameKey(string a, string b) => a.Length > 0 && HotkeyGesture.SameCombination(a, b);

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
        settings.VoiceHotkey = IsVoiceHotkeyValid && HasVoiceHotkey ? _voiceHotkey : null;
        settings.VoiceLanguage = _selectedVoiceLanguage.Id.Length == 0 ? null : _selectedVoiceLanguage.Id;
        settings.WindowKeys = WindowKeys.Overrides(WindowKeyRows.ToDictionary(row => row.Action, row => row.Text));
        if (_original.WindowKeys?.Where(pair => !Enum.TryParse<WindowKeyAction>(pair.Key, out _)).ToList() is { Count: > 0 } unknown)
        {
            // Overrides of actions a later version has: kept, or saving here would delete them on every PC through sync.
            settings.WindowKeys ??= new Dictionary<string, string>();
            foreach (var (name, text) in unknown)
            {
                settings.WindowKeys[name] = text;
            }
        }
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

/// <summary>One key of the bar's window in Settings: the action, and the combination it has (empty: none).</summary>
public sealed class WindowKeyRow(WindowKeyAction action, string text) : ObservableObject
{
    private string _text = text;

    public WindowKeyAction Action { get; } = action;

    public string Label => Action switch
    {
        WindowKeyAction.NewShortcut => Strings.WindowKey_NewShortcut,
        WindowKeyAction.NewGroup => Strings.WindowKey_NewGroup,
        WindowKeyAction.OpenSettings => Strings.WindowKey_OpenSettings,
        WindowKeyAction.Find => Strings.WindowKey_Find,
        WindowKeyAction.UndoRemove => Strings.WindowKey_UndoRemove,
        WindowKeyAction.Remove => Strings.WindowKey_Remove,
        _ => Strings.WindowKey_Properties,
    };

    public string DefaultText => WindowKeys.Defaults[Action];

    public string Text
    {
        get => _text;
        set
        {
            if (SetProperty(ref _text, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(IsDefault));
            }
        }
    }

    public bool IsDefault => string.Equals(_text, DefaultText, StringComparison.OrdinalIgnoreCase);
}
