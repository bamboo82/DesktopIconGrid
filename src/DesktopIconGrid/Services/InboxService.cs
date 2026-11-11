using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using DesktopIconGrid.Core.Diagnostics;
using DesktopIconGrid.Core.Sync;
using DesktopIconGrid.Resources;

namespace DesktopIconGrid.Services;

/// <summary>
/// Receives files other PCs sent to this one through the sync folder (<see cref="FolderInbox"/>): once a minute while
/// the bar runs, what waits is moved to the Downloads folder, marked as downloaded from elsewhere (so SmartScreen and
/// Office's Protected View treat it so), and announced. Nothing is opened. Only with a sync folder, not with Google Drive.
/// </summary>
public sealed class InboxService : IDisposable
{
    private static readonly Guid DownloadsFolder = new("374DE290-123F-4565-9164-39C4925E467B");

    private readonly SyncService _sync;
    private readonly Action<string, string> _notify;
    private readonly DispatcherTimer _timer;
    private bool _busy;

    /// <summary>Inbox files that were delivered but could be neither deleted nor set aside: never delivered twice by this run.</summary>
    private static readonly HashSet<string> Delivered = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Inbox files the user was told are waiting for disk space: told once, not every minute.</summary>
    private static readonly HashSet<string> ToldWaiting = new(StringComparer.OrdinalIgnoreCase);

    public InboxService(SyncService sync, Action<string, string> notify, Dispatcher dispatcher)
    {
        _sync = sync;
        _notify = notify;
        _timer = new DispatcherTimer(TimeSpan.FromMinutes(1), DispatcherPriority.Background, (_, _) => Look(), dispatcher);
        _timer.Stop();
    }

    /// <summary>Whether the bar holds a Send to PC icon; set once the bar exists.</summary>
    public Func<bool> HasSendIcon { get; set; } = () => false;

    /// <summary>Whether a sync folder is in use, so that receiving can be turned on at all.</summary>
    public bool CanReceive => _sync.UsesFolder && FolderInbox.IsDeviceId(_sync.Settings.DeviceId);

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
                if (value)
                {
                    Look();
                }
            }
        }
    }

    /// <summary>
    /// Whether this PC announces itself and takes files now (<see cref="FolderInbox.TakesPart"/>): off unless its user
    /// turned receiving on or put a Send to PC icon on the bar.
    /// </summary>
    public bool IsReceiving => FolderInbox.TakesPart(CanReceive, _sync.Settings.ReceiveFiles, HasSendIcon());

    /// <summary>This PC's inbox in the sync folder, or <see langword="null"/> when no sync folder is used.</summary>
    public FolderInbox? Current()
    {
        var settings = _sync.Settings;
        if (!_sync.UsesFolder || settings.SyncFolder is not { } folder || !FolderInbox.IsDeviceId(settings.DeviceId))
        {
            return null;
        }

        return new FolderInbox(folder, settings.DeviceId, settings.DeviceName);
    }

    public void Start()
    {
        _timer.Start();
        Look();
    }

    public void Dispose() => _timer.Stop();

    private async void Look()
    {
        if (_busy || !IsReceiving || Current() is not { } inbox)
        {
            return; // off unless the user asked for it: no folder is made in the sync folder and nothing is taken
        }

        _busy = true;
        try
        {
            var (received, waiting) = await Task.Run(() => Take(inbox));
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
                _notify(string.Format(Strings.Inbox_ReceivedTitle, sender), marked ? message : $"{message}\n{Strings.Inbox_NotMarked}");
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // async void: nothing may escape to the dispatcher.
            AppLog.Default.Warning("Inbox", $"Looking for sent files failed: {ex.Message}");
        }
        finally
        {
            _busy = false;
        }
    }

    private static (List<(string Name, string Sender, bool Marked)> Received, List<(string Name, string Sender, long Size, long Free)> Waiting) Take(FolderInbox inbox)
    {
        var received = new List<(string, string, bool)>();
        var waiting = new List<(string, string, long, long)>();
        try
        {
            inbox.Announce();
            var downloads = DownloadsPath();
            foreach (var file in inbox.Pending(WasDelivered))
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
                    var (path, marked, cleared) = FolderInbox.Receive(file, downloads, MarkAsDownloaded);
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
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Still being written by the sync program, or locked: the next look takes it.
                    AppLog.Default.Debug("Inbox", $"\"{file.Name}\" could not be taken yet: {ex.Message}");
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Default.Warning("Inbox", $"The inbox could not be used: {ex.Message}");
        }

        return (received, waiting);
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
