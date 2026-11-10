using DesktopIconGrid.Core.Health;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using DesktopIconGrid.Core.Launching;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Core.Storage;
using DesktopIconGrid.Infrastructure;
using DesktopIconGrid.Resources;
using DesktopIconGrid.Services;

namespace DesktopIconGrid.ViewModels;

/// <summary>One choice of "When it is running".</summary>
public sealed record WhenRunningOption(bool Switches, string Name);

/// <summary>One choice of "Run": a normal, minimised or maximised window.</summary>
public sealed record WindowStateOption(string? Value, string Name);

/// <summary>Backs the Shortcut Properties / Group Properties dialog. Writes to the item only on <see cref="Apply"/>.</summary>
public sealed class ItemPropertiesViewModel : ObservableObject
{
    public static readonly IReadOnlyList<WhenRunningOption> WhenRunningChoices =
    [
        new(true, Strings.Props_WhenRunningSwitch),
        new(false, Strings.Props_WhenRunningStart),
    ];

    public static readonly IReadOnlyList<WindowStateOption> WindowStateChoices =
    [
        new(null, Strings.Props_RunNormal),
        new(ShortcutItem.WindowMinimized, Strings.Props_RunMinimized),
        new(ShortcutItem.WindowMaximized, Strings.Props_RunMaximized),
    ];

    private const int PreviewSize = 32;
    /// <summary>How long after the last edit the preview and the not-found check run.</summary>
    private static readonly TimeSpan PreviewDebounce = TimeSpan.FromMilliseconds(300);

    private readonly LauncherItem _target;
    private readonly IIconProvider _icons;
    private string _name;
    private string _location;
    private bool _onlyThisPc;
    private readonly string _sharedLocation;
    private string _parameters;
    private bool _captureLog;
    private readonly string? _originalWhenRunning;
    private bool _switchesWhenRunning;
    private bool _whenRunningChosen;
    private string _startIn;
    private WindowStateOption _windowState;
    private bool _runAsAdministrator;
    private BrowserChoice _browser;
    private bool _appWindow;
    private string? _iconPath;
    private TextIcon? _textIcon;
    private bool _nameIsAutomatic;
    private bool _updatingAutomaticName;
    private ImageSource? _preview;
    private string? _warning;
    private int _previewRequest;
    private Task _previewWork = Task.CompletedTask;

    public ItemPropertiesViewModel(LauncherItem target, bool isNew, IIconProvider icons, IReadOnlyList<BrowserChoice>? browsers = null)
    {
        _target = target;
        _icons = icons;
        IsGroup = target is GroupItem;
        IsNew = isNew;

        _name = target.Name;
        _location = (target as ShortcutItem)?.EffectiveLocation ?? string.Empty;
        _onlyThisPc = target is ShortcutItem { HasOwnLocation: true };
        _sharedLocation = (target as ShortcutItem)?.Location ?? string.Empty;
        _parameters = (target as ShortcutItem)?.Parameters ?? string.Empty;
        _captureLog = (target as ShortcutItem)?.CaptureLog ?? false;
        _healthCheck = (target as ShortcutItem)?.HealthCheck ?? string.Empty;
        _healthInterval = HealthCheck.NormalizeInterval((target as ShortcutItem)?.HealthInterval).ToString(CultureInfo.CurrentCulture);
        _healthNotify = (target as ShortcutItem)?.HealthNotify ?? false;
        _originalWhenRunning = (target as ShortcutItem)?.WhenRunning;
        _switchesWhenRunning = target is ShortcutItem shortcut && LaunchPlanner.SwitchesWhenRunning(shortcut);
        var options = target as ShortcutItem;
        _startIn = options?.StartIn ?? string.Empty;
        _windowState = WindowStateChoices.FirstOrDefault(c => string.Equals(c.Value, options?.WindowState, StringComparison.OrdinalIgnoreCase)) ?? WindowStateChoices[0];
        _runAsAdministrator = options?.RunAsAdministrator ?? false;
        _appWindow = options?.AppWindow ?? false;
        var choices = (browsers ?? [BrowserCatalog.DefaultBrowser]).ToList();
        if (choices.Count == 0 || !choices[0].IsDefault)
        {
            choices.Insert(0, BrowserCatalog.DefaultBrowser);
        }

        _browser = BrowserChoiceFor(options, choices);
        if (!choices.Contains(_browser))
        {
            choices.Add(_browser); // a profile from another PC, or one since deleted: kept as it is
        }

        BrowserOptions = choices;
        _iconPath = target.IconPath;
        _textIcon = target.TextIcon;
        _nameIsAutomatic = !IsGroup && isNew && (_name.Length == 0 || _name == ShortcutItem.SuggestName(_location));

        RefreshPreviewAndWarning(immediate: true);
    }

    public bool IsGroup { get; }

    public bool IsShortcut => !IsGroup;

    public bool IsNew { get; }

    public string Title => IsGroup ? Strings.Props_GroupTitle : Strings.Props_ShortcutTitle;

    public string LocationHint => Strings.Props_LocationHint;

    /// <summary>
    /// The location applies to this PC only (<see cref="ShortcutItem.Locations"/>), for a program installed elsewhere
    /// here; other PCs sharing the bar keep the common one. Offered for existing shortcuts once the device is known.
    /// </summary>
    public bool OnlyThisPc
    {
        get => _onlyThisPc;
        set
        {
            if (SetProperty(ref _onlyThisPc, value))
            {
                OnPropertyChanged(nameof(ShowsOtherPcs));
            }
        }
    }

    public bool CanBeOnlyThisPc => IsShortcut && !IsNew && ThisDevice.Id is not null;

    public bool ShowsOtherPcs => _onlyThisPc && _sharedLocation.Length > 0;

    public string OtherPcsText => string.Format(Strings.Props_OtherPcsKeep, _sharedLocation);

    public string ParametersHint => Strings.Props_ParametersHint;

    public string Name
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, value ?? string.Empty))
            {
                if (!_updatingAutomaticName)
                {
                    _nameIsAutomatic = _name.Length == 0;
                }

                OnPropertyChanged(nameof(CanSave));
            }
        }
    }

    public string Location
    {
        get => _location;
        set
        {
            if (!SetProperty(ref _location, value ?? string.Empty))
            {
                return;
            }

            FollowDefaultWhenRunning();

            OnPropertyChanged(nameof(IsWebLink));
            OnPropertyChanged(nameof(CanChooseAppWindow));

            if (_nameIsAutomatic)
            {
                _updatingAutomaticName = true;
                Name = ShortcutItem.SuggestName(_location);
                _updatingAutomaticName = false;
            }

            OnPropertyChanged(nameof(CanSave));
            OnPropertyChanged(nameof(ParametersWarning)); // who reads the placeholders
            RefreshPreviewAndWarning();
        }
    }

    public string Parameters
    {
        get => _parameters;
        set
        {
            if (SetProperty(ref _parameters, value ?? string.Empty))
            {
                FollowDefaultWhenRunning(); // parameters turn the default from "switch" to "start another"
                OnPropertyChanged(nameof(ParametersWarning));
            }
        }
    }

    /// <summary>A path as typed or pasted, without the quotes Explorer's Copy as path puts around it ("E:\Apps\x.exe").</summary>
    private static string Unquoted(string path) => path.Trim().Trim('"').Trim();

    /// <summary>
    /// Said under the parameters when a placeholder stands where a launch refuses it, judged as a launch judges it (the
    /// variables expanded, cmd.exe's pair around its command taken off, {files} as two {file}): inside quotes with other
    /// text, after a program that hands it on without its quotes to cmd.exe or WSL, or before cmd.exe's /c.
    /// </summary>
    public string? ParametersWarning
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_parameters) || IsGroup)
            {
                return null;
            }

            var location = Unquoted(_location);
            var template = LaunchPlaceholders.SplitFiles(CmdCommandLine.Unwrapped(location, Environment.ExpandEnvironmentVariables(_parameters)));
            if (LaunchPlaceholders.SpotsOf(template).Count == 0)
            {
                return null;
            }

            var startIn = Unquoted(_startIn);
            var misplaced = LaunchPlaceholders.FirstMisplaced(
                location, template, index => LaunchPlaceholders.ReaderAt(location, template, index, startIn, InstalledPrograms.RegisteredPath));
            return misplaced?.Problem switch
            {
                PlacementProblem.InsideQuotes => Strings.Props_PlaceholderInQuotes,
                PlacementProblem.HandedOnWithoutQuotes => string.Format(
                    Strings.Props_RelayDropsQuotes, LaunchPlaceholders.DropsQuotes(location, template), misplaced.Value.Reader == ArgumentReader.Cmd ? "cmd.exe" : "WSL"),
                PlacementProblem.CmdSwitches => Strings.Props_CmdSwitch,
                _ => null,
            };
        }
    }

    /// <summary>Until the user picks <see cref="SelectedWhenRunning"/>, it shows the default for the location and parameters.</summary>
    private void FollowDefaultWhenRunning()
    {
        var byDefault = LaunchPlanner.SwitchesByDefault(_location, _parameters);
        if (!_whenRunningChosen && _originalWhenRunning is null && _switchesWhenRunning != byDefault)
        {
            _switchesWhenRunning = byDefault;
            OnPropertyChanged(nameof(SelectedWhenRunning));
        }
    }

    // ----- health light (0.9.0) ----------------------------------------------------------------

    private string _healthCheck;
    private string _healthInterval;
    private bool _healthNotify;

    /// <summary><c>ping host</c>, <c>tcp host:port</c> or an http(s) address; empty for no light.</summary>
    public string HealthCheckText
    {
        get => _healthCheck;
        set
        {
            if (SetProperty(ref _healthCheck, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(HealthCheckError));
                OnPropertyChanged(nameof(HasHealthCheckError));
                OnPropertyChanged(nameof(HasHealthCheck));
                OnPropertyChanged(nameof(CanSave));
            }
        }
    }

    public bool HasHealthCheck => _healthCheck.Trim().Length > 0;

    public string HealthInterval
    {
        get => _healthInterval;
        set
        {
            if (SetProperty(ref _healthInterval, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(HealthCheckError));
                OnPropertyChanged(nameof(HasHealthCheckError));
                OnPropertyChanged(nameof(CanSave));
            }
        }
    }

    public bool HealthNotify
    {
        get => _healthNotify;
        set => SetProperty(ref _healthNotify, value);
    }

    /// <summary>Why the check cannot be used, or <see langword="null"/>.</summary>
    public string? HealthCheckError
    {
        get
        {
            if (!HasHealthCheck)
            {
                return null;
            }

            HealthCheck.Parse(_healthCheck, out var error);
            if (error is null && !int.TryParse(_healthInterval, NumberStyles.Integer, CultureInfo.CurrentCulture, out _))
            {
                error = Strings.Props_HealthEvery.TrimEnd(':', '：');
            }

            return error is null ? null : string.Format(Strings.Props_HealthInvalid, error);
        }
    }

    public bool HasHealthCheckError => HealthCheckError is not null;

    /// <summary>Capture the program's output to a log file when it is started from the bar.</summary>
    public bool CaptureLog
    {
        get => _captureLog;
        set => SetProperty(ref _captureLog, value);
    }

    /// <summary>An elevated program's output cannot be captured, so the option is off while Run as administrator is ticked.</summary>
    public bool CanCaptureLog => !_runAsAdministrator;

    public string CaptureLogHint => _runAsAdministrator ? Strings.Props_LogNotElevated : Strings.Props_LogHint;

    public IReadOnlyList<WhenRunningOption> WhenRunningOptions => WhenRunningChoices;

    /// <summary>
    /// What a click does while the program runs. Until the user picks one it follows the location (programs switch,
    /// documents, folders and links start again), and nothing is stored, so a later change of location still decides.
    /// </summary>
    public WhenRunningOption SelectedWhenRunning
    {
        get => WhenRunningChoices.First(o => o.Switches == _switchesWhenRunning);
        set
        {
            if (value is null)
            {
                return;
            }

            _whenRunningChosen = true;
            if (_switchesWhenRunning != value.Switches)
            {
                _switchesWhenRunning = value.Switches;
                OnPropertyChanged();
            }
        }
    }

    public string WhenRunningHint => Strings.Props_WhenRunningHint;

    /// <summary>The working folder; empty uses the program's own folder.</summary>
    public string StartIn
    {
        get => _startIn;
        set
        {
            if (SetProperty(ref _startIn, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(ParametersWarning)); // a bare name may be a batch file in this folder
            }
        }
    }

    public string StartInHint => Strings.Props_StartInHint;

    public IReadOnlyList<WindowStateOption> WindowStateOptions => WindowStateChoices;

    public WindowStateOption SelectedWindowState
    {
        get => _windowState;
        set
        {
            if (value is not null)
            {
                SetProperty(ref _windowState, value);
            }
        }
    }

    public bool RunAsAdministrator
    {
        get => _runAsAdministrator;
        set
        {
            if (SetProperty(ref _runAsAdministrator, value))
            {
                OnPropertyChanged(nameof(CanCaptureLog));
                OnPropertyChanged(nameof(CaptureLogHint));
            }
        }
    }

    /// <summary>The default browser, then every installed browser profile (and the stored one if it is not installed here).</summary>
    public IReadOnlyList<BrowserChoice> BrowserOptions { get; }

    public BrowserChoice SelectedBrowser
    {
        get => _browser;
        set
        {
            if (value is not null && SetProperty(ref _browser, value))
            {
                OnPropertyChanged(nameof(CanChooseAppWindow));
            }
        }
    }

    public bool AppWindow
    {
        get => _appWindow;
        set => SetProperty(ref _appWindow, value);
    }

    /// <summary>The browser options apply to web links only.</summary>
    public bool IsWebLink => LaunchPlanner.IsWebLink(_location);

    public bool CanChooseAppWindow => IsWebLink && !_browser.IsDefault;

    private static BrowserChoice BrowserChoiceFor(ShortcutItem? shortcut, IReadOnlyList<BrowserChoice> choices)
    {
        if (string.IsNullOrWhiteSpace(shortcut?.Browser))
        {
            return choices[0];
        }

        return choices.FirstOrDefault(c => string.Equals(c.Browser, shortcut.Browser, StringComparison.OrdinalIgnoreCase)
                                           && string.Equals(c.Profile ?? string.Empty, shortcut.BrowserProfile ?? string.Empty, StringComparison.OrdinalIgnoreCase))
               ?? new BrowserChoice(
                   shortcut.Browser,
                   shortcut.BrowserProfile,
                   string.Format(Strings.Props_BrowserProfile, BrowserCatalog.DisplayName(shortcut.Browser), shortcut.BrowserProfile ?? string.Empty));
    }

    public string? IconPath
    {
        get => _iconPath;
        set
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (SetProperty(ref _iconPath, normalized))
            {
                if (normalized is not null)
                {
                    _textIcon = null; // an image replaces a text icon
                }

                NotifyIconChanged();
            }
        }
    }

    /// <summary>Text drawn instead of an image. Setting it clears a custom image.</summary>
    public TextIcon? TextIcon
    {
        get => _textIcon;
        set
        {
            var normalized = value?.Normalized();
            if (!Equals(_textIcon, normalized))
            {
                _textIcon = normalized;
                if (normalized is not null)
                {
                    _iconPath = null;
                }

                NotifyIconChanged();
            }
        }
    }

    /// <summary>True when either an image or a text icon overrides the default.</summary>
    public bool HasCustomIcon => _iconPath is not null || _textIcon is not null;

    public bool HasTextIcon => _iconPath is null && _textIcon is not null;

    public bool ShowsImagePreview => !HasTextIcon;

    public string PreviewText => _textIcon?.Text ?? string.Empty;

    public FontFamily PreviewFontFamily => TextIconFonts.Resolve(_textIcon?.FontFamily);

    public double PreviewFontSize => Math.Min(_textIcon?.FontSize ?? TextIcon.DefaultFontSize, 28);

    public FontWeight PreviewFontWeight => _textIcon?.Bold == true ? FontWeights.Bold : FontWeights.Normal;

    public Brush PreviewTextBrush => _textIcon?.Color is { } color
        ? ThemeManager.ToBrush(color)
        : Application.Current?.TryFindResource(ThemeManager.ForegroundKey) as Brush ?? Brushes.White;

    public ImageSource? Preview
    {
        get => _preview;
        private set => SetProperty(ref _preview, value);
    }

    /// <summary>Non-blocking notice, e.g. a location that does not exist yet.</summary>
    public string? Warning
    {
        get => _warning;
        private set
        {
            if (SetProperty(ref _warning, value))
            {
                OnPropertyChanged(nameof(HasWarning));
            }
        }
    }

    public bool HasWarning => _warning is not null;

    public bool CanSave => _name.Trim().Length > 0 && (IsGroup || _location.Trim().Length > 0) && !HasHealthCheckError;

    public void UseDefaultIcon()
    {
        _iconPath = null;
        _textIcon = null;
        NotifyIconChanged();
    }

    private void NotifyIconChanged()
    {
        OnPropertyChanged(nameof(IconPath));
        OnPropertyChanged(nameof(TextIcon));
        OnPropertyChanged(nameof(HasCustomIcon));
        OnPropertyChanged(nameof(HasTextIcon));
        OnPropertyChanged(nameof(ShowsImagePreview));
        OnPropertyChanged(nameof(PreviewText));
        OnPropertyChanged(nameof(PreviewFontFamily));
        OnPropertyChanged(nameof(PreviewFontSize));
        OnPropertyChanged(nameof(PreviewFontWeight));
        OnPropertyChanged(nameof(PreviewTextBrush));
        RefreshPreviewAndWarning();
    }

    /// <summary>Copies the edited values into the item. Returns false when the form is not valid.</summary>
    public bool Apply()
    {
        if (!CanSave)
        {
            return false;
        }

        _target.Name = _name.Trim();
        _target.IconPath = _iconPath;
        _target.TextIcon = _textIcon;
        if (_target is ShortcutItem shortcut)
        {
            if (_onlyThisPc && CanBeOnlyThisPc && _sharedLocation.Length > 0)
            {
                shortcut.SetLocationFor(ThisDevice.Id!, DataLocation.ForStorage(Unquoted(_location))); // the common location stays for the other PCs
            }
            else
            {
                shortcut.Location = DataLocation.ForStorage(Unquoted(_location)); // typed or pasted: %APPDRIVE% in portable mode, as picked ones
                if (ThisDevice.Id is { } device)
                {
                    shortcut.SetLocationFor(device, null);
                }
            }

            shortcut.Parameters = string.IsNullOrWhiteSpace(_parameters) ? null : _parameters.Trim();
            shortcut.CaptureLog = _captureLog;
            shortcut.WhenRunning = !_whenRunningChosen
                ? _originalWhenRunning
                : _switchesWhenRunning == LaunchPlanner.SwitchesByDefault(shortcut.EffectiveLocation, shortcut.Parameters)
                    ? null // the location's own default: stored as nothing, so it keeps following the location
                    : _switchesWhenRunning ? ShortcutItem.WhenRunningSwitch : ShortcutItem.WhenRunningStart;
            shortcut.StartIn = string.IsNullOrWhiteSpace(_startIn) ? null : DataLocation.ForStorage(Unquoted(_startIn));
            shortcut.WindowState = _windowState.Value;
            shortcut.RunAsAdministrator = _runAsAdministrator;
            shortcut.Browser = _browser.Browser;
            shortcut.BrowserProfile = _browser.IsDefault ? null : _browser.Profile;
            shortcut.AppWindow = !_browser.IsDefault && _appWindow;
            var check = HealthCheck.Parse(_healthCheck, out _);
            shortcut.HealthCheck = check?.ToString();
            shortcut.HealthInterval = check is null || !int.TryParse(_healthInterval, NumberStyles.Integer, CultureInfo.CurrentCulture, out var every)
                ? null
                : HealthCheck.NormalizeInterval(every) is var seconds && seconds == HealthCheck.DefaultIntervalSeconds ? null : seconds;
            shortcut.HealthNotify = check is not null && _healthNotify;
        }

        return true;
    }

    /// <summary>
    /// Recomputes the preview image and the not-found notice on the icon worker a moment after the last edit, so
    /// typing a path (which probes the file system, possibly a sleeping network share) never blocks the dialog.
    /// </summary>
    private void RefreshPreviewAndWarning(bool immediate = false)
    {
        var request = ++_previewRequest;
        var refresh = RefreshAsync(request, immediate, _iconPath, _location, IsGroup, IsShortcut);
        _previewWork = _previewWork.IsCompleted ? refresh : Task.WhenAll(_previewWork, refresh);
    }

    /// <summary>The pause after an edit before the preview is worked out; a test ends each pause when it chooses.</summary>
    internal Func<Task> PausePreview { get; set; } = () => Task.Delay(PreviewDebounce);

    /// <summary>Completes when every preview refresh started so far has finished, superseded ones included.</summary>
    internal Task PreviewSettled => _previewWork;

    private async Task RefreshAsync(int request, bool immediate, string? iconPath, string location, bool isGroup, bool isShortcut)
    {
        if (!immediate)
        {
            await PausePreview();
            if (request != _previewRequest)
            {
                return; // superseded while waiting
            }
        }

        var icons = _icons;
        var slow = IconWorker.IsOnSlowStorage(location) || IconWorker.IsOnSlowStorage(iconPath);
        var result = await IconWorker.Shared.RunAsync(() => Resolve(icons, iconPath, location, isGroup, isShortcut), IconWorker.LookupTimeout, location, slow);
        if (request != _previewRequest)
        {
            return;
        }

        Preview = result?.Preview;
        Warning = result?.Warning;
    }

    private sealed record PreviewResult(ImageSource? Preview, string? Warning);

    private static PreviewResult Resolve(IIconProvider icons, string? iconPath, string location, bool isGroup, bool isShortcut)
    {
        ImageSource? image = null;
        if (iconPath is not null && icons.CanResolve(iconPath))
        {
            image = icons.GetIcon(iconPath, PreviewSize * 2);
        }

        if (image is null)
        {
            if (isGroup)
            {
                image = icons.GetFolderIcon(PreviewSize * 2);
            }
            else if (icons.CanResolve(location))
            {
                image = icons.GetIcon(location, PreviewSize * 2);
            }
        }

        string? warning = null;
        var expanded = Environment.ExpandEnvironmentVariables(location.Trim());
        if (isShortcut
            && expanded.Length > 0
            && Path.IsPathRooted(expanded)
            && !LaunchPlanner.IsUri(expanded)
            && !File.Exists(expanded)
            && !Directory.Exists(expanded))
        {
            warning = Strings.Props_LocationNotFound;
        }
        else if (iconPath is not null && !icons.CanResolve(iconPath))
        {
            warning = Strings.Props_IconNotFound;
        }

        return new PreviewResult(image, warning);
    }
}
