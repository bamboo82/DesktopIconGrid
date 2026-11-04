using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows.Threading;
using DesktopIconGrid.Core.Diagnostics;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Core.Sync;
using DesktopIconGrid.Infrastructure;

namespace DesktopIconGrid.Services;

/// <summary>
/// Lives for the whole app session: uploads changes to the linked copy a few seconds after they happen,
/// offers newer copies at startup, and applies downloaded copies to the bar.
/// </summary>
public sealed class SyncCoordinator : ObservableObject
{
    private static readonly TimeSpan UploadDelay = TimeSpan.FromSeconds(5);

    private readonly ISyncHost _host;
    private readonly Func<string, string, bool> _confirm;
    private readonly DispatcherTimer _timer;
    private Task? _current;
    private bool _uploadRequestedWhileBusy;
    private bool _shuttingDown;
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
        host.LayoutChanged += (_, _) => ScheduleUpload();
    }

    public SyncService Service { get; }

    /// <summary>One line describing the last background activity, shown in the sync dialog.</summary>
    public string Status
    {
        get => _status;
        private set
        {
            if (SetProperty(ref _status, value) && value.Length > 0)
            {
                if (value.StartsWith("Upload failed", StringComparison.Ordinal))
                {
                    AppLog.Default.Error("Sync", value);
                    Notify?.Invoke("Google Drive sync", value);
                }
                else if (value.StartsWith("Not uploaded", StringComparison.Ordinal) || value.StartsWith("A newer", StringComparison.Ordinal) || value.StartsWith("Startup check failed", StringComparison.Ordinal))
                {
                    AppLog.Default.Warning("Sync", value);
                    Notify?.Invoke("Google Drive sync", value);
                }
                else
                {
                    AppLog.Default.Info("Sync", value);
                }
            }
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
            var result = await Service.AutoUploadAsync(_host.ExportLayout(), CancellationToken.None);
            Status = result.Outcome switch
            {
                AutoUploadOutcome.Uploaded => $"Uploaded to \"{result.Remote?.Name}\" at {DateTime.Now.ToString("t", CultureInfo.CurrentCulture)}.",
                AutoUploadOutcome.Conflict =>
                    $"Not uploaded: \"{result.Remote?.Name}\" was saved by {result.Remote?.SavedBy ?? "another device"} on {Describe(result.Remote?.SavedAt)}. " +
                    "Open Sync with Google Drive to load it or overwrite it.",
                _ => Status,
            };
        }
        catch (Exception ex) when (IsSyncError(ex))
        {
            Status = $"Upload failed: {ex.Message}";
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
            var newer = await Service.CheckForNewerCopyAsync(CancellationToken.None);
            if (newer is null)
            {
                Status = $"\"{Service.Settings.LinkedProfile}\" is up to date.";
                return;
            }

            var load = _confirm(
                "Newer settings copy on Google Drive",
                $"\"{newer.Name}\" was saved by {newer.SavedBy ?? "another device"} on {Describe(newer.SavedAt)}.\n\n" +
                "Load it now? The settings and icons on this device will be replaced.");
            if (load)
            {
                await LoadAndApplyAsync(newer, CancellationToken.None);
            }
            else
            {
                Status = $"A newer \"{newer.Name}\" is on Google Drive; it was not loaded.";
            }
        }
        catch (Exception ex) when (IsSyncError(ex))
        {
            Status = $"Startup check failed: {ex.Message}";
        }
    }

    public async Task LoadAndApplyAsync(RemoteProfile remote, CancellationToken cancellationToken)
    {
        var profile = await Service.LoadProfileAsync(remote, cancellationToken);
        _host.ReplaceLayout(profile.Layout);
        Status = $"Loaded \"{remote.Name}\" (saved by {profile.SavedBy} on {Describe(profile.SavedAt)}).";
    }

    public static string Describe(DateTimeOffset? time) =>
        time is { } t ? t.LocalDateTime.ToString("g", CultureInfo.CurrentCulture) : "an unknown date";

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
