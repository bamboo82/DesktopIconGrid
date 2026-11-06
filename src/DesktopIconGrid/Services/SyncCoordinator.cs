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

    /// <summary>Sets <see cref="Status"/>, logs it at <paramref name="level"/> and, for warnings and errors, raises <see cref="Notify"/>.</summary>
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
                Notify?.Invoke(Strings.Sync_Notification, status);
                break;
            case LogLevel.Warning:
                AppLog.Default.Warning("Sync", status);
                Notify?.Invoke(Strings.Sync_Notification, status);
                break;
            default:
                AppLog.Default.Info("Sync", status);
                break;
        }
    }

    /// <summary>Where failures and conflicts are announced without interrupting (the tray balloon); optional.</summary>
    public Action<string, string>? Notify { get; set; }

    /// <summary>True while an upload is running.</summary>
    public bool IsUploading => _current is { IsCompleted: false };

    /// <summary>True while the debounce timer is armed (an upload will start in a few seconds).</summary>
    internal bool IsUploadScheduled => _timer.IsEnabled;

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
    /// Before exit: waits for an upload that is already running and then runs the pending debounced upload, if any,
    /// without re-arming the timer afterwards. Returns false when that did not finish within <paramref name="timeout"/>.
    /// </summary>
    public async Task<bool> FlushPendingAsync(TimeSpan timeout)
    {
        _shuttingDown = true;
        _retryTimer.Stop(); // a copy that could not be read is not waited for; the next start checks again
        var pending = _timer.IsEnabled || _uploadRequestedWhileBusy;
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
                    break;
                case AutoUploadOutcome.Conflict:
                    Report(
                        string.Format(Strings.Sync_NotUploaded, result.Remote?.Name, result.Remote?.SavedBy ?? Strings.Common_AnotherDevice, Describe(result.Remote?.SavedAt))
                        + WithConflictCopies(" ", result.ConflictCopies),
                        LogLevel.Warning);
                    break;
                case AutoUploadOutcome.Postponed:
                    Report(string.Format(Strings.Sync_UploadPostponed, result.Remote?.Name), LogLevel.Warning); // said once while it lasts
                    _retryTimer.Stop();
                    _retryTimer.Start();
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
