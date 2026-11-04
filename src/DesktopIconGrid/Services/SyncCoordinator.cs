using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Windows.Threading;
using DesktopIconGrid.Core.Diagnostics;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Core.Sync;
using DesktopIconGrid.Infrastructure;
using DesktopIconGrid.ViewModels;

namespace DesktopIconGrid.Services;

/// <summary>
/// Lives for the whole app session: uploads changes to the linked copy a few seconds after they happen,
/// offers newer copies at startup, and applies downloaded copies to the bar.
/// </summary>
public sealed class SyncCoordinator : ObservableObject
{
    private static readonly TimeSpan UploadDelay = TimeSpan.FromSeconds(5);

    private readonly LauncherViewModel _launcher;
    private readonly IDialogService _dialogs;
    private readonly DispatcherTimer _timer;
    private bool _uploading;
    private bool _uploadRequestedWhileBusy;
    private string _status = string.Empty;

    public SyncCoordinator(SyncService service, LauncherViewModel launcher, IDialogService dialogs)
    {
        Service = service;
        _launcher = launcher;
        _dialogs = dialogs;
        _timer = new DispatcherTimer { Interval = UploadDelay };
        _timer.Tick += async (_, _) =>
        {
            _timer.Stop();
            await UploadNowAsync();
        };
        launcher.LayoutChanged += (_, _) => ScheduleUpload();
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
                }
                else if (value.StartsWith("Not uploaded", StringComparison.Ordinal) || value.StartsWith("A newer", StringComparison.Ordinal) || value.StartsWith("Startup check failed", StringComparison.Ordinal))
                {
                    AppLog.Default.Warning("Sync", value);
                }
                else
                {
                    AppLog.Default.Info("Sync", value);
                }
            }
        }
    }

    public LauncherLayout ExportLayout() => _launcher.ExportLayout();

    public void ScheduleUpload()
    {
        if (!Service.IsLinked || !Service.Settings.AutoUpload)
        {
            return;
        }

        _timer.Stop();
        _timer.Start();
    }

    /// <summary>Runs a pending debounced upload right away (used before exit). Returns false when it did not finish within <paramref name="timeout"/>.</summary>
    public async Task<bool> FlushPendingAsync(TimeSpan timeout)
    {
        if (!_timer.IsEnabled && !_uploading)
        {
            return true;
        }

        _timer.Stop();
        var upload = UploadNowAsync();
        var finished = await Task.WhenAny(upload, Task.Delay(timeout)) == upload;
        if (!finished)
        {
            AppLog.Default.Warning("Sync", "Exiting before the pending upload finished");
        }

        return finished;
    }

    public async Task UploadNowAsync()
    {
        if (_uploading)
        {
            _uploadRequestedWhileBusy = true;
            return;
        }

        _uploading = true;
        try
        {
            var result = await Service.AutoUploadAsync(_launcher.ExportLayout(), CancellationToken.None);
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
            _uploading = false;
            if (_uploadRequestedWhileBusy)
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

            var load = _dialogs.Confirm(
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
        _launcher.ReplaceLayout(profile.Layout);
        Status = $"Loaded \"{remote.Name}\" (saved by {profile.SavedBy} on {Describe(profile.SavedAt)}).";
    }

    public static string Describe(DateTimeOffset? time) =>
        time is { } t ? t.LocalDateTime.ToString("g", CultureInfo.CurrentCulture) : "an unknown date";

    /// <summary>Exceptions the sync feature reports to the user instead of crashing.</summary>
    public static bool IsSyncError(Exception ex) => ex is DriveException
        or GoogleAuthException
        or HttpRequestException
        or FormatException
        or ArgumentException
        or IOException
        or UnauthorizedAccessException
        or InvalidOperationException
        or OperationCanceledException;
}
