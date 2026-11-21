using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows.Threading;
using DesktopIconGrid.Core.Diagnostics;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Core.Sync;
using DesktopIconGrid.Infrastructure;
using DesktopIconGrid.Resources;

namespace DesktopIconGrid.Services;

/// <summary>
/// Lives for the whole app session: uploads changes to the linked copy a few seconds after they happen,
/// offers newer copies at startup, and applies downloaded copies to the bar.
/// </summary>
public sealed class SyncCoordinator : ObservableObject
{
    private static readonly TimeSpan UploadDelay = TimeSpan.FromSeconds(5);

    /// <summary>How long a postponed upload waits before it tries again (the linked copy could not be read).</summary>
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(1);

    private readonly ISyncHost _host;
    private readonly Func<string, string, bool> _confirm;
    private readonly DispatcherTimer _timer;
    private readonly DispatcherTimer _retryTimer;
    private Task? _current;
    private bool _uploadRequestedWhileBusy;
    private bool _shuttingDown;
    private int _loading;
    private bool _uploadDueAfterLoad;
    private string _status = string.Empty;

    /// <param name="confirm">Asks the user a yes/no question (title, message) when a newer copy is found at startup.</param>
    public SyncCoordinator(SyncService service, ISyncHost host, Func<string, string, bool> confirm)
    {
        Service = service;
        _host = host;
        _confirm = confirm;
        _timer = new DispatcherTimer { Interval = UploadDelay };
        _timer.Tick += async (_, _) =>
        {
            _timer.Stop();
            await UploadNowAsync();
        };
        _retryTimer = new DispatcherTimer { Interval = RetryDelay };
        _retryTimer.Tick += async (_, _) =>
        {
            _retryTimer.Stop();
            if (!_shuttingDown && Service.IsLinked && Service.Settings.AutoUpload)
            {
                await UploadNowAsync();
            }
        };
        host.LayoutChanged += (_, _) => ScheduleUpload();
    }

    public SyncService Service { get; }

    /// <summary>One line describing the last background activity, shown in the sync dialog.</summary>
    public string Status
    {
        get => _status;
        private set => Report(value, LogLevel.Info);
    }

    /// <summary>
    /// Sets <see cref="Status"/>, logs it at <paramref name="level"/> and, for warnings and errors, raises <see cref="Notify"/>
    /// with a warning whose click opens the Sync window, where the problem is shown and can be dealt with (0.12.1, review
    /// 28 D2; before, it opened the log).
    /// </summary>
    private void Report(string status, LogLevel level)
    {
        if (!SetProperty(ref _status, status, nameof(Status)) || status.Length == 0)
        {
            return;
        }

        switch (level)
        {
            case LogLevel.Error:
                AppLog.Default.Error("Sync", status);
                Notify?.Invoke(Problem(status));
                break;
            case LogLevel.Warning:
                AppLog.Default.Warning("Sync", status);
                Notify?.Invoke(Problem(status));
                break;
            default:
                AppLog.Default.Info("Sync", status);
                break;
        }
    }

    /// <summary>Where failures and conflicts are announced without interrupting (the tray balloon); optional.</summary>
    public Action<Notice>? Notify { get; set; }

    /// <summary>A problem of sync: a warning that leads to the Sync window.</summary>
    internal static Notice Problem(string status) => new(Strings.Sync_Notification, status, NoticeKind.Warning, NoticeTarget.Sync);

    /// <summary>True while an upload is running.</summary>
    public bool IsUploading => _current is { IsCompleted: false };

    /// <summary>True while the debounce timer is armed (an upload will start in a few seconds).</summary>
    internal bool IsUploadScheduled => _timer.IsEnabled;

    /// <summary>True while a postponed upload waits to be tried again.</summary>
    internal bool IsRetryScheduled => _retryTimer.IsEnabled;

    public LauncherLayout ExportLayout() => _host.ExportLayout();

    public void ScheduleUpload()
    {
        if (_shuttingDown || !Service.IsLinked || !Service.Settings.AutoUpload)
        {
            return;
        }

        _timer.Stop();
        _timer.Start();
    }

    /// <summary>
    /// Before exit: waits for an upload that is already running and then runs the pending debounced upload, or the one
    /// postponed while a copy could not be read, if any, without re-arming a timer afterwards. Returns false when that did
    /// not finish within <paramref name="timeout"/>.
    /// </summary>
    public async Task<bool> FlushPendingAsync(TimeSpan timeout)
    {
        _shuttingDown = true;
        var pending = _timer.IsEnabled || _uploadRequestedWhileBusy || _retryTimer.IsEnabled; // a postponed upload gets one more try
        _retryTimer.Stop();
        _timer.Stop();
        if (!pending && !IsUploading)
        {
            return true;
        }

        var deadline = Task.Delay(timeout);
        var finished = true;
        if (_current is { IsCompleted: false } inFlight)
        {
            AppLog.Default.Info("Sync", "Waiting for the running upload before exit");
            finished = await Task.WhenAny(inFlight, deadline) == inFlight;
            pending |= _uploadRequestedWhileBusy; // changes made while it ran
        }

        if (finished && pending)
        {
            _uploadRequestedWhileBusy = false;
            var upload = UploadNowAsync();
            finished = await Task.WhenAny(upload, deadline) == upload;
        }

        if (!finished)
        {
            AppLog.Default.Warning("Sync", "Exiting before the pending upload finished");
        }

        return finished;
    }

    /// <summary>Uploads now; when an upload is already running, returns that one and remembers to run again afterwards.</summary>
    public Task UploadNowAsync()
    {
        if (_current is { IsCompleted: false } inFlight)
        {
            _uploadRequestedWhileBusy = true;
            return inFlight;
        }

        _current = RunUploadAsync();
        return _current;
    }

    private async Task RunUploadAsync()
    {
        try
        {
            if (_loading > 0)
            {
                // A copy is being loaded: the bar it replaces is uploaded nowhere (not even to the copy being left, so
                // loading the linked copy again undoes a change). Should the load fail, the upload is due again.
                _uploadDueAfterLoad = true;
                return;
            }

            // The generation is read with the bar, on this thread: a load that finishes while the upload waits for its
            // turn moves it, and the upload then writes nothing (the bar it carries is the one the load replaced).
            var takenAt = Service.LinkGeneration;
            var result = await Service.AutoUploadAsync(_host.ExportLayout(), takenAt, CancellationToken.None);
            switch (result.Outcome)
            {
                case AutoUploadOutcome.Uploaded:
                    Status = string.Format(Strings.Sync_UploadedAt, result.Remote?.Name, DateTime.Now.ToString("t", CultureInfo.CurrentCulture));
                    await SyncPictureAsync(); // the layout is up whatever this does (0.15.0 WP5b)
                    _ = EnsurePictureAsync();
                    break;
                case AutoUploadOutcome.Conflict:
                    Report(
                        string.Format(Strings.Sync_NotUploaded, result.Remote?.Name, result.Remote?.SavedBy ?? Strings.Common_AnotherDevice, Describe(result.Remote?.SavedAt))
                        + WithConflictCopies(" ", result.ConflictCopies),
                        LogLevel.Warning);
                    break;
                case AutoUploadOutcome.Postponed:
                    Report(string.Format(Strings.Sync_UploadPostponed, result.Remote?.Name ?? Service.Settings.LinkedProfile), LogLevel.Warning); // said once while it lasts
                    if (!_shuttingDown)
                    {
                        _retryTimer.Stop();
                        _retryTimer.Start();
                    }

                    break;
            }
        }
        catch (Exception ex) when (IsSyncError(ex))
        {
            Report(string.Format(Strings.Sync_UploadFailed, ex.Message), LogLevel.Error);
        }
        finally
        {
            if (_uploadRequestedWhileBusy && !_shuttingDown)
            {
                _uploadRequestedWhileBusy = false;
                ScheduleUpload();
            }
        }
    }

    /// <summary>At startup: when another device saved the linked copy more recently, offer to load it.</summary>
    public async Task RunStartupCheckAsync()
    {
        if (!Service.IsLinked || !Service.Settings.CheckAtStartup)
        {
            return;
        }

        try
        {
            var check = await Service.CheckLinkedCopyAsync(CancellationToken.None);
            if (check.Newer is not { } newer)
            {
                Status = string.Format(Strings.Sync_UpToDate, Service.Settings.LinkedProfile) + WithConflictCopies(" ", check.ConflictCopies);
                return;
            }

            var load = _confirm(
                Strings.Sync_NewerTitle,
                string.Format(Strings.Sync_NewerQuestion, newer.Name, newer.SavedBy ?? Strings.Common_AnotherDevice, Describe(newer.SavedAt))
                + WithConflictCopies("\n\n", check.ConflictCopies));
            if (load)
            {
                await LoadAndApplyAsync(newer, CancellationToken.None);
            }
            else
            {
                Report(string.Format(Strings.Sync_NewerNotLoaded, newer.Name), LogLevel.Warning);
            }
        }
        catch (Exception ex) when (IsSyncError(ex))
        {
            Report(string.Format(Strings.Sync_StartupCheckFailed, ex.Message), LogLevel.Warning);
        }

        _ = EnsurePictureAsync(); // a picture the bar names and this PC lacks: tried again at each sync check (0.15.0 WP5b)
    }

    /// <summary>
    /// Loads a copy and puts it on the bar. Until the bar is replaced no upload takes it (the load has already moved
    /// <see cref="SyncService.LinkGeneration"/> by then), and afterwards nothing that was waiting to upload the old bar
    /// does: changes from now on ask for an upload of their own.
    /// </summary>
    public async Task LoadAndApplyAsync(RemoteProfile remote, CancellationToken cancellationToken)
    {
        _loading++;
        var replaced = false;
        try
        {
            var profile = await Service.LoadProfileAsync(remote, cancellationToken);
            _host.ReplaceLayout(profile.Layout);
            replaced = true;
            Status = string.Format(Strings.Sync_LoadedFrom, remote.Name, profile.SavedBy, Describe(profile.SavedAt));
            _ = EnsurePictureAsync(); // the colour shows at once; the picture when it is here (0.15.0 WP5b)
        }
        finally
        {
            _loading--;
            if (replaced)
            {
                _timer.Stop();
                _retryTimer.Stop();
                _uploadRequestedWhileBusy = false;
                _uploadDueAfterLoad = false;
            }
            else if (_uploadDueAfterLoad && _loading == 0)
            {
                _uploadDueAfterLoad = false;
                ScheduleUpload(); // the bar is still the one that was waiting to go up
            }
        }
    }

    /// <summary>Where this PC keeps its background pictures; tests stand in a folder of their own.</summary>
    internal string PicturesFolder { get; set; } = AppPaths.BackgroundsDirectory;

    /// <summary>
    /// Whether bytes from the sync place decode as the picture their name says: WPF imaging, on the UI thread as the rest of
    /// the app's; tests stand in their own.
    /// </summary>
    internal Func<byte[], bool, bool> Decodes { get; set; } = DecodeOnUiThread;

    private static bool DecodeOnUiThread(byte[] bytes, bool png)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            return BackgroundPictures.DecodesAs(bytes, png);
        }

        try
        {
            return dispatcher.Invoke(() => BackgroundPictures.DecodesAs(bytes, png));
        }
        catch (Exception ex) when (ex is TaskCanceledException or InvalidOperationException)
        {
            return false; // the program is ending: nothing is kept
        }
    }

    /// <summary>
    /// After a layout went up (0.15.0 WP5b): its background picture beside the copies unless it is there, and the one it
    /// replaced deleted from the sync place unless a copy still names it. A failure is one line in the log and leaves the
    /// layout's upload as it was; the next upload tries again.
    /// </summary>
    public async Task SyncPictureAsync()
    {
        try
        {
            await Service.SyncPictureAfterUploadAsync(_host.ExportLayout().Settings.BackgroundImage?.File, PicturesFolder, CancellationToken.None);
        }
        catch (Exception ex) when (IsSyncError(ex) || ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Default.Warning("Sync", $"The background picture was not synced: {ex.Message} (tried again at the next upload)");
        }
    }

    /// <summary>
    /// When the bar names a background picture this PC lacks (0.15.0 WP5b): fetches it from the sync place in the background,
    /// the colour showing meanwhile, and the bar takes it when it is here. Not there or refused: one line in the log, and it is
    /// tried again at the next sync check (an upload, a load, the start-up check), never in a loop.
    /// </summary>
    public async Task EnsurePictureAsync()
    {
        if (!Service.IsConnected || _host.ExportLayout().Settings.BackgroundImage?.File is not { } name)
        {
            return;
        }

        if (File.Exists(Path.Combine(PicturesFolder, name)))
        {
            return;
        }

        if (await PictureTransfers.EnsureAsync(Service, name, PicturesFolder, Decodes, CancellationToken.None))
        {
            _host.PictureArrived(name);
        }
    }

    /// <summary>
    /// The sentence that points out the copies a sync program kept of the linked copy (OneDrive's <c>Work-LAPTOP.json</c>,
    /// Dropbox's conflicted copy), after <paramref name="separator"/>; empty when there are none.
    /// </summary>
    private string WithConflictCopies(string separator, IReadOnlyList<RemoteProfile> conflictCopies) => conflictCopies.Count == 0
        ? string.Empty
        : separator + string.Format(
            Strings.Sync_ConflictCopies,
            string.Join(", ", conflictCopies.Select(copy => $"\"{copy.Name}\"")),
            Service.Settings.LinkedProfile);

    public static string Describe(DateTimeOffset? time) =>
        time is { } t ? t.LocalDateTime.ToString("g", CultureInfo.CurrentCulture) : Strings.Common_UnknownDate;

    /// <summary>
    /// Exceptions the sync feature reports to the user instead of crashing: network, Drive, sign-in, file and
    /// data-format failures and cancellation. Programming errors (invalid operations, bad arguments) are not
    /// in the list on purpose, so they surface instead of hiding behind a status line.
    /// </summary>
    public static bool IsSyncError(Exception ex) => ex is DriveException
        or GoogleAuthException
        or HttpRequestException
        or JsonException
        or FormatException
        or IOException
        or UnauthorizedAccessException
        or OperationCanceledException;
}
