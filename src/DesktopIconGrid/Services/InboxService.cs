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

    public InboxService(SyncService sync, Action<string, string> notify, Dispatcher dispatcher)
    {
        _sync = sync;
        _notify = notify;
        _timer = new DispatcherTimer(TimeSpan.FromMinutes(1), DispatcherPriority.Background, (_, _) => Look(), dispatcher);
        _timer.Stop();
    }

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
        if (_busy || Current() is not { } inbox)
        {
            return;
        }

        _busy = true;
        try
        {
            var received = await Task.Run(() => Take(inbox));
            foreach (var (name, sender) in received)
            {
                _notify(string.Format(Strings.Inbox_ReceivedTitle, sender), string.Format(Strings.Inbox_Received, name));
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

    private static List<(string Name, string Sender)> Take(FolderInbox inbox)
    {
        var received = new List<(string, string)>();
        try
        {
            inbox.Announce();
            var downloads = DownloadsPath();
            foreach (var file in inbox.Pending())
            {
                try
                {
                    var path = FolderInbox.Receive(file, downloads);
                    MarkAsDownloaded(path);
                    AppLog.Default.Info("Inbox", $"Received \"{Path.GetFileName(path)}\" ({file.Size} bytes) from {file.SenderName}");
                    received.Add((Path.GetFileName(path), file.SenderName));
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

        return received;
    }

    /// <summary>The Zone.Identifier stream of a file from the Internet zone.</summary>
    internal static void MarkAsDownloaded(string path)
    {
        try
        {
            File.WriteAllText(path + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\n");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            AppLog.Default.Warning("Inbox", $"\"{Path.GetFileName(path)}\" could not be marked as downloaded: {ex.Message}");
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
