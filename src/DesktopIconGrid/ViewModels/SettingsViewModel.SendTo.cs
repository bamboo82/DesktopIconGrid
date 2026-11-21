using DesktopIconGrid.Core.Storage;
using DesktopIconGrid.Core.Sync;
using DesktopIconGrid.Resources;

namespace DesktopIconGrid.ViewModels;

/// <summary>How the bar's copies travel between PCs, as the Send to PC tab says it.</summary>
public enum SyncSetup
{
    /// <summary>Neither a sync folder nor a Google sign-in: files can be neither sent nor received.</summary>
    None,

    /// <summary>A sync folder (OneDrive, Dropbox, a share).</summary>
    Folder,

    /// <summary>Google Drive.</summary>
    Drive,
}

/// <summary>
/// What the Send to PC tab shows of this PC (0.14.0 WP5, A3), from <c>sync.json</c> and the inbox: never from the bar's
/// layout, so nothing of it travels with the bar.
/// </summary>
/// <param name="DeviceName">This PC's name, the one the Sync window shows too.</param>
/// <param name="ReceiveFiles">The menu's <em>Receive files from my other PCs</em>.</param>
/// <param name="ReceiveFolder">The receive folder as stored, <see langword="null"/> for Downloads.</param>
/// <param name="DownloadsPath">Windows' Downloads folder on this PC.</param>
/// <param name="DataDirectory">The bar's data folder, which the receive folder must keep out of.</param>
/// <param name="SyncFolder">The sync folder when one is used, which the receive folder must keep out of.</param>
public sealed record ThisPcSettings(
    string DeviceName,
    bool ReceiveFiles,
    string? ReceiveFolder,
    string DownloadsPath,
    string DataDirectory,
    string? SyncFolder,
    SyncSetup Setup);

/// <summary>What Save keeps of the Send to PC tab, for <c>sync.json</c>.</summary>
/// <param name="ReceiveFolder">As stored: <see langword="null"/> for Downloads; in a portable copy a path on the program's drive with <c>%APPDRIVE%</c>.</param>
public sealed record ThisPcChoice(string DeviceName, bool ReceiveFiles, string? ReceiveFolder);

/// <summary>Settings ▸ Send to PC (0.14.0 WP5, A3): this PC's name, whether it receives, where, and how far sync is set up.</summary>
public sealed partial class SettingsViewModel
{
    private ThisPcSettings? _thisPc;
    private string _pcName = string.Empty;
    private bool _receiveFiles;
    private string _receiveFolderText = string.Empty;
    private string? _receiveFolderRefusal;

    /// <summary>Opens the Sync window over this dialog and gives this PC's settings as they are after it; set by the dialog service.</summary>
    internal Func<ThisPcSettings?>? OpenSync { get; set; }

    /// <summary>The tab's values from <paramref name="thisPc"/>; without them (tests) it shows this PC's defaults and Save keeps nothing of it.</summary>
    public void UseThisPc(ThisPcSettings? thisPc)
    {
        _thisPc = thisPc;
        if (thisPc is null)
        {
            return;
        }

        _pcName = thisPc.DeviceName;
        _receiveFiles = thisPc.ReceiveFiles;
        _receiveFolderText = ReceiveFolder.Expand(thisPc.ReceiveFolder) ?? thisPc.DownloadsPath;
        _receiveFolderRefusal = null;
        OnPropertyChanged(nameof(PcName));
        OnPropertyChanged(nameof(ReceiveFiles));
        OnSyncSetupChanged();
        OnReceiveFolderChanged();
    }

    /// <summary>This PC's name (A3): the one name the other PCs, sync and the notices use. Empty is Windows' name for the PC at Save.</summary>
    public string PcName
    {
        get => _pcName;
        set => SetProperty(ref _pcName, value ?? string.Empty);
    }

    /// <summary>The longest name a PC may have, the same as in the Sync window.</summary>
    public static int MaxPcNameLength => SyncSettings.MaxDeviceNameLength;

    public bool ReceiveFiles
    {
        get => _receiveFiles;
        set => SetProperty(ref _receiveFiles, value);
    }

    /// <summary>The folder in use as the box shows and edits it: Downloads' real name when none was chosen.</summary>
    public string ReceiveFolderText
    {
        get => _receiveFolderText;
        set
        {
            if (SetProperty(ref _receiveFolderText, value ?? string.Empty))
            {
                _receiveFolderRefusal = null; // said at Save for the folder that was there then
                OnReceiveFolderChanged();
            }
        }
    }

    /// <summary>The box names Downloads (or nothing): stored as no setting, so Downloads it stays wherever Windows keeps it.</summary>
    public bool ReceiveFolderIsDownloads =>
        string.IsNullOrWhiteSpace(_receiveFolderText) || DataLocation.SameFolder(ReceiveFolder.Expand(_receiveFolderText), _thisPc?.DownloadsPath);

    /// <summary>Back to Downloads: the box shows Downloads, and Save stores no setting.</summary>
    public void ResetReceiveFolder() => ReceiveFolderText = _thisPc?.DownloadsPath ?? string.Empty;

    /// <summary>Why the folder cannot be saved, next to the box: a name that breaks the rules at once, and what Save found on the disk.</summary>
    public string? ReceiveFolderError => _receiveFolderRefusal ?? NameProblem();

    public bool HasReceiveFolderError => ReceiveFolderError is not null;

    /// <summary>The folder as Save checks and uses it, or <see langword="null"/> for Downloads.</summary>
    private string? ChosenReceiveFolder => ReceiveFolderIsDownloads ? null : ReceiveFolder.Expand(_receiveFolderText);

    private string? NameProblem()
    {
        if (_thisPc is null || ChosenReceiveFolder is not { } folder)
        {
            return null;
        }

        var problem = ReceiveFolder.CheckName(folder, _thisPc.DataDirectory, _thisPc.SyncFolder);
        return problem == ReceiveFolderProblem.None ? null : RefusalText(problem, _receiveFolderText.Trim());
    }

    /// <summary>A refusal in the user's language, naming the folder as it was typed.</summary>
    internal static string RefusalText(ReceiveFolderProblem problem, string folder) => string.Format(
        problem switch
        {
            ReceiveFolderProblem.NotAbsolute => Strings.Settings_ReceiveNotAbsolute,
            ReceiveFolderProblem.TooLong => Strings.Settings_ReceiveTooLong,
            ReceiveFolderProblem.InDataFolder => Strings.Settings_ReceiveInDataFolder,
            ReceiveFolderProblem.InSyncFolder => Strings.Settings_ReceiveInSyncFolder,
            ReceiveFolderProblem.NotAFolder => Strings.Settings_ReceiveNotAFolder,
            _ => Strings.Settings_ReceiveCannotUse,
        },
        folder);

    /// <summary>
    /// At Save: the chosen folder against all the rules, on the disk too (it is made when missing). False, with the reason
    /// next to the box, when it cannot take the files; Downloads always can.
    /// </summary>
    public bool TryCheckReceiveFolder(Func<string, string, string?, ReceiveFolderProblem>? check = null)
    {
        if (_thisPc is null || ChosenReceiveFolder is not { } folder)
        {
            return true;
        }

        var problem = (check ?? ReceiveFolder.Check)(folder, _thisPc.DataDirectory, _thisPc.SyncFolder);
        _receiveFolderRefusal = problem == ReceiveFolderProblem.None ? null : RefusalText(problem, _receiveFolderText.Trim());
        OnReceiveFolderChanged();
        return _receiveFolderRefusal is null;
    }

    /// <summary>The Send to PC tab holds a folder that cannot be saved.</summary>
    private bool HasSendToProblem => HasReceiveFolderError;

    public string SendToTab => TabHeader(Strings.Settings_TabSendTo, HasSendToProblem);

    public string SyncStateText => (_thisPc?.Setup ?? SyncSetup.None) switch
    {
        SyncSetup.Folder => Strings.Settings_SyncStateFolder,
        SyncSetup.Drive => Strings.Settings_SyncStateDrive,
        _ => Strings.Settings_SyncStateNone,
    };

    /// <summary>Sync is not set up: a hint says that sending and receiving need it.</summary>
    public bool NeedsSync => (_thisPc?.Setup ?? SyncSetup.None) == SyncSetup.None;

    /// <summary>After the Sync window: how sync is set up now, and the name if it was changed there and not here.</summary>
    public void RefreshFromSync(ThisPcSettings now)
    {
        ArgumentNullException.ThrowIfNull(now);
        var nameUntouched = _thisPc is not null && string.Equals(_pcName, _thisPc.DeviceName, StringComparison.Ordinal);
        _thisPc = _thisPc is null ? now : _thisPc with { Setup = now.Setup, SyncFolder = now.SyncFolder, DeviceName = now.DeviceName };
        if (nameUntouched)
        {
            PcName = now.DeviceName;
        }

        OnSyncSetupChanged();
        OnReceiveFolderChanged(); // the sync folder the folder must keep out of may be another now
    }

    /// <summary>What Save keeps of the tab, or <see langword="null"/> without this PC's settings.</summary>
    public ThisPcChoice? ToThisPc() => _thisPc is null
        ? null
        : new ThisPcChoice(
            _pcName.Trim(),
            _receiveFiles,
            ChosenReceiveFolder is null ? null : DataLocation.ForStorage(_receiveFolderText.Trim().Trim('"').Trim()));

    private void OnSyncSetupChanged()
    {
        OnPropertyChanged(nameof(SyncStateText));
        OnPropertyChanged(nameof(NeedsSync));
    }

    private void OnReceiveFolderChanged()
    {
        OnPropertyChanged(nameof(ReceiveFolderIsDownloads));
        OnPropertyChanged(nameof(ReceiveFolderError));
        OnPropertyChanged(nameof(HasReceiveFolderError));
        OnPropertyChanged(nameof(SendToTab));
        OnPropertyChanged(nameof(ProblemText));
        OnPropertyChanged(nameof(CanSave));
    }
}
