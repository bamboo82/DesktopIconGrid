using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using DesktopIconGrid.Core.Diagnostics;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Core.Sync;
using DesktopIconGrid.Resources;

namespace DesktopIconGrid.Services;

/// <summary>What a Send to PC icon's properties need of the receiving side.</summary>
public interface IReceiving
{
    /// <summary>This PC's inbox, or <see langword="null"/> without a sync folder or a Google sign-in.</summary>
    IInbox? Current();

    /// <summary>The menu's <em>Receive files from my other PCs</em>.</summary>
    bool ReceiveSetting { get; }

    /// <summary>The user opened the properties: a look that waits after failures is due again (review 27 S4).</summary>
    void Opened();

    /// <summary>A Send to PC icon's mark changed: says this PC's name when that turned receiving on, takes it back when off.</summary>
    void Changed();

    /// <summary>The receive folder as the dialog names it (0.14.0 WP5): Downloads unless another was chosen in Settings.</summary>
    string ReceiveFolderName => Strings.Inbox_Downloads;
}

/// <summary>
/// Receives files other PCs sent to this one through the sync folder (<see cref="FolderInbox"/>) or Google Drive
/// (<see cref="DriveInbox"/>): once a minute while the bar runs and this PC receives, what waits is moved to the
/// receive folder (Downloads unless another was chosen in Settings, 0.14.0 WP5), marked as downloaded from elsewhere
/// (so SmartScreen and Office's Protected View treat it so), and announced. Nothing is opened. It also lets the bar's
/// Send to PC icons follow the PCs they send to when those are renamed (0.14.0 A3).
/// </summary>
public sealed class InboxService : IDisposable, IReceiving
{
    private static readonly Guid DownloadsFolder = new("374DE290-123F-4565-9164-39C4925E467B");

    private readonly SyncService _sync;
    private readonly Action<Notice> _show;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _timer;
    private readonly InboxBackOff _backOff = new();
    private bool _busy;
    private bool _lookAgain;

    /// <summary>
    /// Whether this PC's name is in its inbox, as far as this run knows; <see langword="null"/> until the first look,
    /// which takes it back when this PC does not receive (left by an earlier run, or receiving was turned off meanwhile).
    /// </summary>
    private bool? _announced;

    /// <summary>Inbox files that were delivered but could be neither deleted nor set aside: never delivered twice by this run.</summary>
    private static readonly HashSet<string> Delivered = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Inbox files the user was told are waiting for disk space: told once, not every minute.</summary>
    private static readonly HashSet<string> ToldWaiting = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="show">Shows a notice: a file received (its click shows the file), one that waits for disk space.</param>
    public InboxService(SyncService sync, Action<Notice> show, Dispatcher dispatcher)
    {
        _sync = sync;
        _show = show;
        _dispatcher = dispatcher;
        _timer = new DispatcherTimer(TimeSpan.FromMinutes(1), DispatcherPriority.Background, (_, _) => Look(), dispatcher);
        _timer.Stop();
    }

    /// <summary>Whether the bar holds a Send to PC icon; set once the bar exists.</summary>
    public Func<bool> HasSendIcon { get; set; } = () => false;

    /// <summary>Whether a Send to PC icon of the bar has a PC to send to, whose name it may need to follow; set once the bar exists.</summary>
    public Func<bool> HasSendTargets { get; set; } = () => false;

    /// <summary>Gives the bar the other PCs as they are listed now, so its Send to PC icons follow a renamed one (0.14.0 A3).</summary>
    public Action<IReadOnlyList<InboxDevice>>? FollowNames { get; set; }

    /// <summary>The bar's data folder, which the receive folder must keep out of; tests give their own.</summary>
    internal Func<string> DataDirectory { get; set; } = () => DataFolders.Current.Directory;

    /// <summary>How often the other PCs are listed to follow their names: a look every minute would list Drive as often.</summary>
    internal static TimeSpan FollowEvery { get; set; } = TimeSpan.FromMinutes(5);

    private DateTime _followedAt = DateTime.MinValue;
    private bool _following;

    /// <summary>
    /// The folder chosen in Settings as it is used (0.14.0 WP5), or Downloads when none is: what the menu's tool tip,
    /// Settings and the Send to PC dialog name. Nothing is checked or made here.
    /// </summary>
    public string ChosenFolder => ReceiveFolder.Expand(_sync.Settings.ReceiveFolder) ?? DownloadsPath();

    /// <summary>
    /// Where the files of a look go: the folder chosen in Settings while it keeps the rules (it is made when missing), else
    /// Downloads; with the chosen folder and why, when it could not be used (a stick pulled out). Checked at each look that
    /// has files, so a folder that comes back is used again.
    /// </summary>
    internal (string Folder, string? Chosen, ReceiveFolderProblem Problem) ReceiveTarget()
    {
        var downloads = DownloadsPath();
        if (ReceiveFolder.Expand(_sync.Settings.ReceiveFolder) is not { } chosen)
        {
            return (downloads, null, ReceiveFolderProblem.None);
        }

        var problem = ReceiveFolder.Check(chosen, DataDirectory(), _sync.UsesFolder ? _sync.Settings.SyncFolder : null);
        return problem == ReceiveFolderProblem.None ? (chosen, chosen, problem) : (downloads, chosen, problem);
    }

    /// <summary>
    /// A folder as the notices name it: the Downloads folder by the word for it in the UI's language, as before 0.14.0;
    /// another by its last name, a drive by its root. The full name is in the menu's tool tip and Settings.
    /// </summary>
    internal static string FolderShownName(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || Core.Storage.DataLocation.SameFolder(folder, DownloadsPath()))
        {
            return Strings.Inbox_Downloads;
        }

        var name = Path.GetFileName(folder.TrimEnd('\\', '/'));
        return string.IsNullOrEmpty(name) ? folder : name;
    }

    /// <summary>Whether a sync folder is in use, so that receiving can be turned on at all.</summary>
    public bool CanReceive => (_sync.UsesFolder || UsesDrive) && FolderInbox.IsDeviceId(_sync.Settings.DeviceId);

    /// <summary>Signed in to Google Drive (and not on a sync folder): files travel through the app's folder there.</summary>
    private bool UsesDrive => !_sync.UsesFolder && _sync.IsGoogleConnected && _sync.Files is not null;

    /// <summary>The setting: take files even without a Send to PC icon. Saved in the local sync settings.</summary>
    public bool ReceiveSetting
    {
        get => _sync.Settings.ReceiveFiles;
        set
        {
            if (_sync.Settings.ReceiveFiles != value)
            {
                _sync.Settings.ReceiveFiles = value;
                _sync.SaveSettings();
                AppLog.Default.Info("Inbox", value ? "Receiving files from other PCs turned on" : "Receiving files from other PCs turned off");
                Look(); // says this PC's name, or takes it back when no marked icon keeps receiving on
            }
        }
    }

    /// <summary>
    /// Keeps what Settings' Send to PC tab changed (0.14.0 WP5, A3) in <c>sync.json</c>: this PC's name (empty is Windows'
    /// name for it), whether it receives, and the receive folder; then a look says the name again where this PC is listed,
    /// or takes it back.
    /// </summary>
    public void ApplyThisPc(ViewModels.ThisPcChoice choice)
    {
        ArgumentNullException.ThrowIfNull(choice);
        var settings = _sync.Settings;
        var name = string.IsNullOrWhiteSpace(choice.DeviceName) ? SyncSettings.DefaultDeviceName() : choice.DeviceName.Trim();
        if (string.Equals(name, settings.DeviceName, StringComparison.Ordinal)
            && string.Equals(choice.ReceiveFolder, settings.ReceiveFolder, StringComparison.Ordinal)
            && choice.ReceiveFiles == settings.ReceiveFiles)
        {
            return;
        }

        if (!string.Equals(name, settings.DeviceName, StringComparison.Ordinal))
        {
            AppLog.Default.Info("Inbox", $"This PC is called \"{name}\" now (it was \"{settings.DeviceName}\")");
        }

        if (!string.Equals(choice.ReceiveFolder, settings.ReceiveFolder, StringComparison.Ordinal))
        {
            AppLog.Default.Info("Inbox", choice.ReceiveFolder is null ? "Files from the other PCs go to Downloads again" : $"Files from the other PCs go to {choice.ReceiveFolder}");
        }

        if (choice.ReceiveFiles != settings.ReceiveFiles)
        {
            AppLog.Default.Info("Inbox", choice.ReceiveFiles ? "Receiving files from other PCs turned on" : "Receiving files from other PCs turned off");
        }

        settings.DeviceName = name;
        settings.ReceiveFolder = choice.ReceiveFolder;
        settings.ReceiveFiles = choice.ReceiveFiles;
        _sync.SaveSettings();
        Look();
    }

    /// <summary>
    /// Whether this PC announces itself and takes files now (<see cref="FolderInbox.TakesPart"/>): off unless its user
    /// turned receiving on or put a Send to PC icon on the bar.
    /// </summary>
    public bool IsReceiving => FolderInbox.TakesPart(CanReceive, _sync.Settings.ReceiveFiles, HasSendIcon());

    /// <summary>This PC's inbox in the sync folder, or <see langword="null"/> when no sync folder is used.</summary>
    public IInbox? Current()
    {
        var settings = _sync.Settings;
        if (!FolderInbox.IsDeviceId(settings.DeviceId))
        {
            return null;
        }

        if (_sync.UsesFolder && settings.SyncFolder is { } folder)
        {
            return new FolderInbox(folder, settings.DeviceId, settings.DeviceName);
        }

        if (UsesDrive)
        {
            // Kept while the account stays the same: it remembers the ids of its folders on Drive.
            if (_drive is null || _driveFor != (settings.AccountEmail, settings.DeviceId, settings.DeviceName))
            {
                _drive = new DriveInbox(_sync.Files!, settings.DeviceId, settings.DeviceName);
                _driveFor = (settings.AccountEmail, settings.DeviceId, settings.DeviceName);
            }

            return _drive;
        }

        return null;
    }

    private DriveInbox? _drive;
    private (string? Account, string DeviceId, string DeviceName) _driveFor;

    public void Start()
    {
        _timer.Start();
        Look();
    }

    public void Dispose() => _timer.Stop();

    void IReceiving.Opened() => _backOff.Reset();

    string IReceiving.ReceiveFolderName => FolderShownName(ChosenFolder);

    /// <summary>After the dialog's caller has put a new icon on the bar, so that it counts.</summary>
    void IReceiving.Changed() => _dispatcher.InvokeAsync(Look, DispatcherPriority.Background);

    /// <summary>
    /// Once a minute, and when receiving may have been turned on or off. While this PC receives: says its name and takes
    /// what waits. While it does not: takes its name back once (review 27 S1), so the other PCs no longer offer it;
    /// nothing else is made or taken. Through Google Drive a failed look waits 1, 2, 5, then 15 minutes (S4).
    /// </summary>
    private async void Look()
    {
        if (_busy)
        {
            _lookAgain = true; // a change while a look runs: looked at again when it ends
            return;
        }

        if (Current() is not { } inbox || (inbox is DriveInbox && !_backOff.IsDue(DateTime.UtcNow)))
        {
            return;
        }

        var receiving = IsReceiving;
        if (!receiving && _announced == false)
        {
            FollowNamesSometimes(inbox); // a PC that only sends follows the names of the PCs it sends to as well
            return; // off unless the user asked for it: no folder is made in the sync folder and nothing is taken
        }

        _busy = true;
        var started = DateTime.UtcNow;
        try
        {
            if (!receiving)
            {
                if (await Task.Run(() => inbox.WithdrawAsync(CancellationToken.None)))
                {
                    AppLog.Default.Info("Inbox", "This PC does not receive files now: its name is taken out of its inbox, so the other PCs no longer offer it");
                }

                _announced = false;
                _backOff.Reset();
                return;
            }

            var take = await Task.Run(() => TakeAsync(inbox, ReceiveTarget));
            _announced = take.Announced || _announced == true;
            if (take.Failure is null)
            {
                _backOff.Reset();
                FollowNamesSometimes(inbox);
            }
            else
            {
                Failed(take.Failure, started);
            }

            foreach (var notice in Notices(take.Received, take.Waiting, take.Folder, take.FellBackFrom))
            {
                _show(notice);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // async void: nothing may escape to the dispatcher. Taking the name back failed too: tried at the next look.
            Failed(ex, started);
        }
        finally
        {
            _busy = false;
            if (_lookAgain)
            {
                _lookAgain = false;
                Look();
            }
        }
    }

    /// <summary>
    /// The notices of one look, per sender: one for the files received, then one for the files that wait for disk
    /// space. Twenty files in one look were twenty balloons, each replacing the last (review of WP1, F1). The warning
    /// comes last, so it is the balloon that stays (release check U4).
    /// </summary>
    /// <param name="folder">Where the look saved its files, which the warning about disk space names; Downloads when not given.</param>
    /// <param name="fellBackFrom">The folder chosen in Settings when it could not be used and the files went to Downloads: one more warning, last, names both.</param>
    internal static IReadOnlyList<Notice> Notices(
        IEnumerable<(string Path, string Sender, bool Marked)> received,
        IEnumerable<(string Name, string Sender, long Size, long Free)> waiting,
        string? folder = null,
        string? fellBackFrom = null)
    {
        var notices = new List<Notice>();
        var saved = received.ToList();
        foreach (var fromOne in saved.GroupBy(file => file.Sender, StringComparer.Ordinal))
        {
            var files = fromOne.ToList();
            notices.Add(files.Count == 1 ? ReceivedNotice(files[0].Path, files[0].Sender, files[0].Marked) : ReceivedNotice(files));
        }

        foreach (var fromOne in waiting.GroupBy(file => file.Sender, StringComparer.Ordinal))
        {
            var first = fromOne.First();
            var notice = WaitingNotice(first.Name, first.Sender, first.Size, first.Free, folder);
            var more = fromOne.Count() - 1;
            notices.Add(more == 0 ? notice : notice with { Message = $"{notice.Message}\n{string.Format(Strings.Inbox_AndMore, more)}" });
        }

        if (fellBackFrom is not null && saved.Count > 0)
        {
            notices.Add(FellBackNotice(fellBackFrom, [.. saved.Select(file => file.Path)]));
        }

        return notices;
    }

    /// <summary>
    /// The folder chosen in Settings could not be used, so the look's files went to Downloads (0.14.0 WP5): a warning that
    /// names the folder and the first file (and how many more); a click shows that file. The setting is kept.
    /// </summary>
    internal static Notice FellBackNotice(string chosen, IReadOnlyList<string> savedPaths)
    {
        var message = string.Format(Strings.Inbox_FolderFellBack, chosen, Path.GetFileName(savedPaths[0]));
        return new Notice(
            Strings.Inbox_FolderFellBackTitle,
            savedPaths.Count == 1 ? message : $"{message}\n{string.Format(Strings.Inbox_AndMore, savedPaths.Count - 1)}",
            NoticeKind.Warning,
            NoticeTarget.Folder,
            savedPaths[0]);
    }

    /// <summary>
    /// Two or more files from one sender in one look: how many, the line on the mark when any lacks it, then the names
    /// one per line (the balloon cuts the list at a line end). A click shows the first in its folder.
    /// </summary>
    internal static Notice ReceivedNotice(IReadOnlyList<(string Path, string Sender, bool Marked)> files)
    {
        var lines = new List<string> { string.Format(Strings.Inbox_ReceivedMany, files.Count) };
        if (files.Any(file => !file.Marked))
        {
            lines.Add(Strings.Inbox_NotMarked);
        }

        lines.AddRange(files.Select(file => Path.GetFileName(file.Path)));
        return new Notice(
            string.Format(Strings.Inbox_ReceivedTitle, files[0].Sender),
            string.Join("\n", lines),
            NoticeKind.Information,
            NoticeTarget.Folder,
            files[0].Path);
    }

    /// <summary>A file received: information, and a click shows it in its folder, under the name it was saved as (WP1 N3).</summary>
    internal static Notice ReceivedNotice(string savedPath, string sender, bool marked)
    {
        var message = string.Format(Strings.Inbox_Received, Path.GetFileName(savedPath), FolderShownName(Path.GetDirectoryName(savedPath)));
        return new Notice(
            string.Format(Strings.Inbox_ReceivedTitle, sender),
            marked ? message : $"{message}\n{Strings.Inbox_NotMarked}",
            NoticeKind.Information,
            NoticeTarget.Folder,
            savedPath);
    }

    /// <summary>A file that waits for disk space: a problem of receiving, so a click opens the Sync window (WP1 N3).</summary>
    internal static Notice WaitingNotice(string name, string sender, long size, long free, string? folder = null) => new(
        string.Format(Strings.Inbox_WaitingTitle, sender),
        string.Format(
            Strings.Inbox_WaitingForSpace,
            name,
            SpecialIcons.SystemMonitor.FormatBytes(size),
            SpecialIcons.SystemMonitor.FormatBytes(free),
            SpecialIcons.SystemMonitor.FormatBytes(FolderInbox.FreeSpaceFloor),
            FolderShownName(folder)),
        NoticeKind.Warning,
        NoticeTarget.Sync);

    /// <summary>The first failure since a look worked is a warning in the log, the repeats are debug lines; Drive waits longer each time.</summary>
    private void Failed(Exception failure, DateTime started)
    {
        var message = $"The inbox could not be used: {failure.Message}";
        if (_backOff.Failed(started))
        {
            AppLog.Default.Warning("Inbox", message);
        }
        else
        {
            AppLog.Default.Debug("Inbox", $"{message} (failed {_backOff.Failures} times in a row)");
        }
    }

    /// <summary>What one look took, what waits for room, where it saved them, and why it stopped, when it did.</summary>
    private sealed record TakeResult(
        List<(string Path, string Sender, bool Marked)> Received,
        List<(string Name, string Sender, long Size, long Free)> Waiting,
        bool Announced,
        Exception? Failure,
        string? Folder = null,
        string? FellBackFrom = null);

    /// <param name="target">Where the files go (<see cref="ReceiveTarget"/>), asked only when files wait.</param>
    private static async Task<TakeResult> TakeAsync(IInbox inbox, Func<(string Folder, string? Chosen, ReceiveFolderProblem Problem)> target)
    {
        var received = new List<(string, string, bool)>();
        var waiting = new List<(string, string, long, long)>();
        var announced = false;
        string? folder = null;
        string? fellBackFrom = null;
        try
        {
            await inbox.AnnounceAsync(CancellationToken.None).ConfigureAwait(false);
            announced = true;
            var pending = await inbox.PendingAsync(WasDelivered, CancellationToken.None).ConfigureAwait(false);
            if (pending.Count > 0)
            {
                var (into, chosen, problem) = target();
                folder = into;
                if (problem != ReceiveFolderProblem.None)
                {
                    fellBackFrom = chosen;
                    AppLog.Default.Warning("Inbox", $"The receive folder \"{chosen}\" cannot be used ({problem}): what arrives now goes to Downloads, {into}; the setting is kept");
                }
            }

            foreach (var file in pending)
            {
                var into = folder!;
                if (FreeBytes(into) is { } free && !FolderInbox.HasRoom(free, file.Size))
                {
                    // It stays in the inbox and is taken at a later look, when there is room. Said once per file, with
                    // the numbers the user needs; a smaller file after it may still fit.
                    bool firstTime;
                    lock (Delivered)
                    {
                        firstTime = ToldWaiting.Add(file.Path);
                    }

                    if (firstTime)
                    {
                        AppLog.Default.Warning("Inbox", $"\"{file.Name}\" ({file.Size} bytes) from {file.SenderName} waits: {free} bytes free on the drive of {into}");
                        waiting.Add((file.Name, file.SenderName, file.Size, free));
                    }

                    continue;
                }

                try
                {
                    var (path, marked, cleared) = await inbox.ReceiveAsync(file, into, MarkAsDownloaded, CancellationToken.None).ConfigureAwait(false);
                    if (!cleared)
                    {
                        lock (Delivered)
                        {
                            Delivered.Add(file.Path);
                        }
                    }

                    AppLog.Default.Info("Inbox", $"Received \"{Path.GetFileName(path)}\" ({file.Size} bytes) from {file.SenderName} into {into}{(marked ? string.Empty : ", not marked as downloaded")}");
                    received.Add((path, file.SenderName, marked)); // where it was saved: the notice's click shows it
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DriveException or System.Net.Http.HttpRequestException)
                {
                    // Still being written by the sync program, locked, or Drive did not answer: the next look takes it.
                    AppLog.Default.Debug("Inbox", $"\"{file.Name}\" could not be taken yet: {ex.Message}");
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DriveException or System.Net.Http.HttpRequestException or InvalidOperationException)
        {
            // The folder is not there, Drive did not answer, or the sign-in has lapsed: tried again at a later look.
            return new TakeResult(received, waiting, announced, ex, folder, fellBackFrom);
        }

        return new TakeResult(received, waiting, announced, null, folder, fellBackFrom);
    }

    /// <summary>
    /// Lists the other PCs, at most every <see cref="FollowEvery"/>, and gives the list to the bar so its Send to PC icons
    /// follow a PC renamed on its own Settings tab (0.14.0 A3). Only while an icon has a PC to send to; a list that
    /// cannot be made changes nothing.
    /// </summary>
    private async void FollowNamesSometimes(IInbox inbox)
    {
        if (_following || FollowNames is not { } follow || !HasSendTargets() || DateTime.UtcNow - _followedAt < FollowEvery)
        {
            return;
        }

        _following = true;
        _followedAt = DateTime.UtcNow;
        try
        {
            var listed = await Task.Run(() => inbox.DevicesAsync(CancellationToken.None));
            await _dispatcher.InvokeAsync(() => follow(listed));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            AppLog.Default.Debug("Inbox", $"The other PCs could not be listed to follow their names: {ex.Message}"); // tried at a later look
        }
        finally
        {
            _following = false;
        }
    }

    private static long? FreeBytes(string folder)
    {
        try
        {
            return Path.GetPathRoot(Path.GetFullPath(folder)) is { Length: > 0 } root ? new DriveInfo(root).AvailableFreeSpace : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null; // a share: not known, not limited
        }
    }

    private static bool WasDelivered(string inboxPath)
    {
        lock (Delivered)
        {
            return Delivered.Contains(inboxPath);
        }
    }

    /// <summary>The Zone.Identifier stream of a file from the Internet zone; false when the volume keeps none (FAT, some shares).</summary>
    internal static bool MarkAsDownloaded(string path)
    {
        try
        {
            File.WriteAllText(path + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\n");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            AppLog.Default.Warning("Inbox", $"\"{Path.GetFileName(path)}\" could not be marked as downloaded: {ex.Message}");
            return false;
        }
    }

    internal static string DownloadsPath()
    {
        if (OperatingSystem.IsWindows() && SHGetKnownFolderPath(DownloadsFolder, 0, IntPtr.Zero, out var pointer) == 0)
        {
            try
            {
                if (Marshal.PtrToStringUni(pointer) is { Length: > 0 } path)
                {
                    return path;
                }
            }
            finally
            {
                Marshal.FreeCoTaskMem(pointer);
            }
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    }

    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, IntPtr token, out IntPtr path);
}
