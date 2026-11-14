using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using DesktopIconGrid.Core.Diagnostics;
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
}

/// <summary>
/// Receives files other PCs sent to this one through the sync folder (<see cref="FolderInbox"/>) or Google Drive
/// (<see cref="DriveInbox"/>): once a minute while the bar runs and this PC receives, what waits is moved to the
/// Downloads folder, marked as downloaded from elsewhere (so SmartScreen and Office's Protected View treat it so), and
/// announced. Nothing is opened.
/// </summary>
public sealed class InboxService : IDisposable, IReceiving
{
    private static readonly Guid DownloadsFolder = new("374DE290-123F-4565-9164-39C4925E467B");

    private readonly SyncService _sync;
    private readonly Action<string, string> _notify;
    private readonly Action<string, string> _inform;
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

    /// <param name="notify">Shows a notice of a problem: a file that waits for disk space.</param>
    /// <param name="inform">Shows a notice of no problem: a file received (logged as information, not as a warning).</param>
    public InboxService(SyncService sync, Action<string, string> notify, Action<string, string> inform, Dispatcher dispatcher)
    {
        _sync = sync;
        _notify = notify;
        _inform = inform;
        _dispatcher = dispatcher;
        _timer = new DispatcherTimer(TimeSpan.FromMinutes(1), DispatcherPriority.Background, (_, _) => Look(), dispatcher);
        _timer.Stop();
    }

    /// <summary>Whether the bar holds a Send to PC icon; set once the bar exists.</summary>
    public Func<bool> HasSendIcon { get; set; } = () => false;

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

            var (received, waiting, announced, failure) = await Task.Run(() => TakeAsync(inbox));
            _announced = announced || _announced == true;
            if (failure is null)
            {
                _backOff.Reset();
            }
            else
            {
                Failed(failure, started);
            }

            foreach (var (name, sender, size, free) in waiting)
            {
                _notify(
                    string.Format(Strings.Inbox_WaitingTitle, sender),
                    string.Format(
                        Strings.Inbox_WaitingForSpace,
                        name,
                        SpecialIcons.SystemMonitor.FormatBytes(size),
                        SpecialIcons.SystemMonitor.FormatBytes(free),
                        SpecialIcons.SystemMonitor.FormatBytes(FolderInbox.FreeSpaceFloor)));
            }

            foreach (var (name, sender, marked) in received)
            {
                var message = string.Format(Strings.Inbox_Received, name);
                _inform(string.Format(Strings.Inbox_ReceivedTitle, sender), marked ? message : $"{message}\n{Strings.Inbox_NotMarked}");
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

    private static async Task<(List<(string Name, string Sender, bool Marked)> Received, List<(string Name, string Sender, long Size, long Free)> Waiting, bool Announced, Exception? Failure)> TakeAsync(IInbox inbox)
    {
        var received = new List<(string, string, bool)>();
        var waiting = new List<(string, string, long, long)>();
        var announced = false;
        try
        {
            await inbox.AnnounceAsync(CancellationToken.None).ConfigureAwait(false);
            announced = true;
            var downloads = DownloadsPath();
            foreach (var file in await inbox.PendingAsync(WasDelivered, CancellationToken.None).ConfigureAwait(false))
            {
                if (FreeBytes(downloads) is { } free && !FolderInbox.HasRoom(free, file.Size))
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
                        AppLog.Default.Warning("Inbox", $"\"{file.Name}\" ({file.Size} bytes) from {file.SenderName} waits: {free} bytes free on the Downloads drive");
                        waiting.Add((file.Name, file.SenderName, file.Size, free));
                    }

                    continue;
                }

                try
                {
                    var (path, marked, cleared) = await inbox.ReceiveAsync(file, downloads, MarkAsDownloaded, CancellationToken.None).ConfigureAwait(false);
                    if (!cleared)
                    {
                        lock (Delivered)
                        {
                            Delivered.Add(file.Path);
                        }
                    }

                    AppLog.Default.Info("Inbox", $"Received \"{Path.GetFileName(path)}\" ({file.Size} bytes) from {file.SenderName}{(marked ? string.Empty : ", not marked as downloaded")}");
                    received.Add((Path.GetFileName(path), file.SenderName, marked));
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
            return (received, waiting, announced, ex);
        }

        return (received, waiting, announced, null);
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
