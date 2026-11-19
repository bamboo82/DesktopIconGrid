using System.Globalization;
using System.Windows.Input;
using DesktopIconGrid.Core.Diagnostics;
using DesktopIconGrid.Core.Fonts;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Infrastructure;
using DesktopIconGrid.Resources;
using DesktopIconGrid.Services;

namespace DesktopIconGrid.ViewModels;

/// <summary>One choice of the Chinese font list.</summary>
public sealed record ChineseFontOption(string Id, string Name);

/// <summary>Where the install of Taipei Sans TC Light is.</summary>
public enum FontInstallState
{
    Idle,
    Downloading,
    Done,
    Failed,
}

/// <summary>
/// The Settings row of the Chinese font (0.12.0 WP5, T3): Taipei Sans TC Light or the Windows font, and, on a PC
/// without the font, a button that installs it. The button asks first (<see cref="ConfirmInstall"/>), never runs by
/// itself, and its result applies at Save like every other setting here.
/// </summary>
public sealed partial class SettingsViewModel
{
    private static readonly ChineseFontOption TaipeiOption = new(LauncherSettings.ChineseFontTaipei, Strings.Settings_ChineseFontTaipei);
    private static readonly ChineseFontOption SystemOption = new(LauncherSettings.ChineseFontSystem, Strings.Settings_ChineseFontSystem);

    private IChineseFontInstall? _fontInstall;
    private bool _fontFound;
    private IReadOnlyList<ChineseFontOption> _chineseFontOptions = [SystemOption];
    private ChineseFontOption _selectedChineseFont = SystemOption;
    private bool _chineseFontChosen;
    private FontInstallState _installState;
    private long _received;
    private string? _installMessage;
    private CancellationTokenSource? _installCancel;
    private RelayCommand? _installFontCommand;

    /// <summary>
    /// Connects the row to this PC's font: whether it is here, and how it is installed. Without it (tests of other rows)
    /// the row offers the Windows font only and no button.
    /// </summary>
    public void UseChineseFont(IChineseFontInstall? fonts)
    {
        _fontInstall = fonts;
        _fontFound = fonts?.Find() is not null;
        _chineseFontChosen = false;
        _chineseFontOptions = _fontFound ? [TaipeiOption, SystemOption] : [SystemOption];
        _selectedChineseFont = ChineseFonts.UsesTaipei(_original.ChineseFont, CultureInfo.CurrentUICulture, _fontFound) ? TaipeiOption : SystemOption;
        OnPropertyChanged(nameof(ChineseFontOptions));
        OnPropertyChanged(nameof(SelectedChineseFont));
        NotifyInstall();
    }

    /// <summary>Asks before the download (the window: the app's confirmation window, Cancel by default).</summary>
    public Func<bool>? ConfirmInstall { get; set; }

    /// <summary>Taipei Sans TC Light when this PC has it, and the Windows font.</summary>
    public IReadOnlyList<ChineseFontOption> ChineseFontOptions => _chineseFontOptions;

    public ChineseFontOption SelectedChineseFont
    {
        get => _selectedChineseFont;
        set
        {
            if (value is not null && SetProperty(ref _selectedChineseFont, value))
            {
                _chineseFontChosen = true;
            }
        }
    }

    /// <summary>This PC has no Taipei Sans TC Light, and it can be installed from here: the button and the hint.</summary>
    public bool IsChineseFontMissing => !_fontFound && _fontInstall is not null;

    public FontInstallState InstallState => _installState;

    public bool IsInstalling => _installState == FontInstallState.Downloading;

    /// <summary>The hint under the list: shown while the font is missing and nothing else is said there.</summary>
    public bool ShowsMissingHint => IsChineseFontMissing && _installState == FontInstallState.Idle;

    /// <summary>"Downloading… 7.4 / 20.3 MB".</summary>
    public string InstallProgressText => string.Format(CultureInfo.CurrentCulture, Strings.FontInstall_Progress, Megabytes(_received), Megabytes(_fontInstall?.Size ?? 0));

    /// <summary>0 to 100.</summary>
    public double InstallProgress => _fontInstall is { Size: > 0 } fonts ? Math.Min(100, 100.0 * _received / fonts.Size) : 0;

    /// <summary>"Installed.", or why not.</summary>
    public string? InstallMessage => _installMessage;

    public bool HasInstallMessage => _installMessage is not null;

    /// <summary>A failure: drawn in the error colour, with the link to the author's page.</summary>
    public bool IsInstallFailed => _installState == FontInstallState.Failed;

    public ICommand InstallFontCommand => _installFontCommand ??= new RelayCommand(() => _ = InstallFontAsync(), () => IsChineseFontMissing && !IsInstalling);

    public ICommand CancelInstallCommand => new RelayCommand(CancelInstall);

    /// <summary>The author's page, for installing it by hand.</summary>
    public Uri ManualInstallPage => TaipeiSans.ManualPage;

    /// <summary>Stops a download under way (its Cancel button, and the Settings window's).</summary>
    public void CancelInstall() => _installCancel?.Cancel();

    /// <summary>Asks, downloads and installs; the row shows each step.</summary>
    public async Task InstallFontAsync()
    {
        if (_fontInstall is not { } fonts || !IsChineseFontMissing || IsInstalling || ConfirmInstall?.Invoke() != true)
        {
            return;
        }

        using var cancel = new CancellationTokenSource();
        _installCancel = cancel;
        _received = 0;
        _installMessage = null;
        SetInstallState(FontInstallState.Downloading);
        FontInstallResult result;
        try
        {
            result = await fonts.InstallAsync(new Progress<long>(received =>
            {
                _received = received;
                OnPropertyChanged(nameof(InstallProgressText));
                OnPropertyChanged(nameof(InstallProgress));
            }), cancel.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            result = new FontInstallResult(FontInstallOutcome.Cancelled);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // The installer says its own cases as results; anything else is a failure the row says, not a row left on
            // "Downloading… 0.0 / 20.3 MB" with a Cancel that does nothing (0.12.1, review 28 F1).
            AppLog.Default.Warning("Fonts", $"The font could not be installed: {ex.Message}");
            result = new FontInstallResult(FontInstallOutcome.InstallFailed, Message: ex.Message);
        }
        finally
        {
            _installCancel = null;
        }

        (_installMessage, var state) = result.Outcome switch
        {
            FontInstallOutcome.Installed => (Strings.FontInstall_Done, FontInstallState.Done),
            FontInstallOutcome.Cancelled => (null, FontInstallState.Idle),
            FontInstallOutcome.DownloadFailed => (string.Format(CultureInfo.CurrentCulture, Strings.FontInstall_DownloadFailed, result.Message), FontInstallState.Failed),
            FontInstallOutcome.Mismatch => (Strings.FontInstall_Mismatch, FontInstallState.Failed),
            FontInstallOutcome.OtherFileThere => (string.Format(CultureInfo.CurrentCulture, Strings.FontInstall_OtherFileThere, TaipeiSans.FileName), FontInstallState.Failed),
            FontInstallOutcome.NameTaken => (string.Format(CultureInfo.CurrentCulture, Strings.FontInstall_NameTaken, result.Path), FontInstallState.Failed),
            _ => (string.Format(CultureInfo.CurrentCulture, Strings.FontInstall_InstallFailed, result.Message), FontInstallState.Failed),
        };

        if (result.Outcome == FontInstallOutcome.Installed)
        {
            // The found form, with the font chosen. The bar has already taken what the stored setting gives now that the
            // font is here (ChineseFontService.Installed); a choice made here applies at Save.
            _fontFound = true;
            _chineseFontOptions = [TaipeiOption, SystemOption];
            _selectedChineseFont = TaipeiOption;
            _chineseFontChosen = true;
            OnPropertyChanged(nameof(ChineseFontOptions));
            OnPropertyChanged(nameof(SelectedChineseFont));
        }

        SetInstallState(state);
    }

    /// <summary>What Save writes: the choice made here, else the setting as it was (the default stays the default).</summary>
    private string? ChineseFontToSave => _chineseFontChosen ? _selectedChineseFont.Id : _original.ChineseFont;

    private void SetInstallState(FontInstallState state)
    {
        _installState = state;
        NotifyInstall();
    }

    private void NotifyInstall()
    {
        OnPropertyChanged(nameof(InstallState));
        OnPropertyChanged(nameof(IsInstalling));
        OnPropertyChanged(nameof(IsChineseFontMissing));
        OnPropertyChanged(nameof(ShowsMissingHint));
        OnPropertyChanged(nameof(InstallMessage));
        OnPropertyChanged(nameof(HasInstallMessage));
        OnPropertyChanged(nameof(IsInstallFailed));
        OnPropertyChanged(nameof(InstallProgressText));
        OnPropertyChanged(nameof(InstallProgress));
        CommandManager.InvalidateRequerySuggested();
    }

    private static string Megabytes(long bytes) => (bytes / 1_000_000.0).ToString("0.0", CultureInfo.CurrentCulture);
}
