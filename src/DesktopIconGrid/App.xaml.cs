using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using DesktopIconGrid.Core;
using DesktopIconGrid.Core.Diagnostics;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Core.Storage;
using DesktopIconGrid.Core.Sync;
using DesktopIconGrid.Resources;
using DesktopIconGrid.Services;
using DesktopIconGrid.SpecialIcons;
using DesktopIconGrid.ViewModels;
using DesktopIconGrid.Views;
using Microsoft.Win32;

namespace DesktopIconGrid;

public partial class App : Application
{
    private const string InstanceMutexName = @"Local\DesktopIconGrid.SingleInstance";
    private const string ShowEventName = @"Local\DesktopIconGrid.Show";

    private static readonly HttpClient Http = CreateHttpClient();

    private Mutex? _instanceMutex;
    private EventWaitHandle? _showEvent;
    private RegisteredWaitHandle? _showWait;
    private ProcessTracker? _tracker;
    private SystemMonitor? _monitor;
    private SyncCoordinator? _sync;
    private WindowSwitcher? _windows;
    private Func<bool>? _fluentDialogsSetting;

    /// <summary>The app-wide HTTP client (Google APIs, icon downloads). Per-request timeouts are applied by the callers.</summary>
    public static HttpClient SharedHttp => Http;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            AppLog.Default.Error("App", "Unhandled exception (process is terminating)", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            AppLog.Default.Error("App", "Unobserved task exception", args.Exception);
            args.SetObserved();
        };

        try
        {
            Start(startHidden: e.Args.Any(a => string.Equals(a, StartupRegistration.HiddenArgument, StringComparison.OrdinalIgnoreCase)));
        }
        catch (Exception ex)
        {
            AppLog.Default.Error("App", "Startup failed", ex);
            MessageBox.Show(
                string.Format(Strings.App_StartFailed, ex.Message, Path.Combine(AppPaths.LogsDirectory, "app.log")),
                Strings.App_Name,
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    /// <param name="startHidden">From <c>--hidden</c> (the Run entry written by "start hidden"): create the bar but leave it in the tray.</param>
    private void Start(bool startHidden)
    {
        // Claim the instance before touching app.log, so a second copy neither shares the file nor rotates it.
        if (!ClaimSingleInstance())
        {
            System.Diagnostics.Debug.WriteLine("Desktop Icon Grid is already running; asked it to show the bar.");
            Shutdown(0);
            return;
        }

        DataLocation.PublishProgramDrive();
        AppLog.Default.AttachFile(Path.Combine(AppPaths.LogsDirectory, "app.log"));
        AppLog.Default.Info("App", $"Desktop Icon Grid {Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)} starting");
        if (DataLocation.IsPortable)
        {
            AppLog.Default.Info("App", $"Portable mode ({DataLocation.PortableMarker} found): data in {DataLocation.Directory}, %{DataLocation.AppDriveVariable}% = {Environment.GetEnvironmentVariable(DataLocation.AppDriveVariable)}");
        }
        else if (DataLocation.PortableFolderIsReadOnly)
        {
            AppLog.Default.Warning("App", $"{DataLocation.PortableMarker} found, but the program's folder cannot be written; data in {DataLocation.Directory}");
        }

        // The sync service sets ThisDevice.Id, which the load needs: a portable copy's repair of 0.8.0's paths gives this
        // PC its own location, and does nothing without the id (review 18 D1).
        var syncService = CreateSyncService();
        var storage = new LayoutStorage(LayoutStorage.GetDefaultFilePath());
        var load = storage.Load();
        var model = LauncherModel.FromLayout(load.Layout);
        UiLanguage.Apply(model.Settings.Language);
        AppLog.Default.Info("App", load.IsFirstRun
            ? "No layout file yet; starting with an empty bar"
            : $"Layout loaded: {model.Items.Count} top-level items, {model.TotalShortcutCount} shortcuts{(model.RepairCount > 0 ? $", {model.RepairCount} repaired" : string.Empty)}{(load.RecoveredFromBackup ? " (recovered from layout.json.bak)" : string.Empty)}");
        if (load.Error is not null)
        {
            AppLog.Default.Error("App", $"Layout file could not be read: {load.Error}");
        }

        if (load.NewerVersion is { } newer)
        {
            AppLog.Default.Warning("App", $"The layout was written by a newer version (format {newer}); unknown items are kept as placeholders");
        }

        var icons = new ShellIconProvider();
        var dialogs = new DialogService(icons);
        _tracker = new ProcessTracker(Dispatcher, AppPaths.LogsDirectory);
        _monitor = new SystemMonitor(Dispatcher);
        Func<Window?> owner = () => Current?.MainWindow;
        var cacheDirectory = AppPaths.DriveCacheDirectory;
        var specialIcons = new SpecialIconRegistry([
            new CpuUsageHandler(_monitor, owner),
            new DiskUsageHandler(_monitor, owner),
            new DriveBucketHandler(syncService, dialogs, owner, icons, cacheDirectory, Dispatcher),
            new SoundReplayerHandler(owner, AppPaths.SoundsDirectory, Dispatcher),
        ]);
        var launcher = new LaunchService(_tracker, dialogs);
        _windows = new WindowSwitcher();
        var viewModel = new LauncherViewModel(
            model,
            storage,
            icons,
            dialogs,
            launcher,
            _tracker,
            specialIcons,
            action => Dispatcher.BeginInvoke(DispatcherPriority.Background, action),
            _windows);

        // First paint; the bar window refines this once it has a handle (backdrop, High Contrast, settings changes).
        ThemeManager.Apply(ThemeManager.EffectivePalette(viewModel.Palette));
        _fluentDialogsSetting = () => viewModel.Model.Settings.FluentDialogs;
        DialogTheme.Configure(_fluentDialogsSetting());
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

        _sync = new SyncCoordinator(syncService, viewModel, dialogs.Confirm) { Notify = dialogs.Notify };
        dialogs.SyncViewModelFactory = () => new SyncViewModel(_sync, dialogs);
        viewModel.FlushBeforeExit = () => _sync.FlushPendingAsync(TimeSpan.FromSeconds(5));
        _ = Task.Run(() => DriveCache.Purge(cacheDirectory, TimeSpan.FromDays(1)));

        // A portable copy never adds a Run entry by itself on the PCs it visits: only when the option is turned on there.
        var startup = CreateStartupRegistration();
        var applied = (viewModel.Model.Settings.StartWithWindows, viewModel.Model.Settings.StartHidden);
        ApplyStartupRegistration(startup, viewModel.Model.Settings, mayAdd: !DataLocation.IsPortableCopy);
        viewModel.SettingsChanged += (_, _) =>
        {
            var settings = viewModel.Model.Settings;
            if ((settings.StartWithWindows, settings.StartHidden) == applied)
            {
                return;
            }

            var justTicked = settings.StartWithWindows && !applied.StartWithWindows;
            applied = (settings.StartWithWindows, settings.StartHidden);
            var change = ApplyStartupRegistration(startup, settings, mayAdd: !DataLocation.IsPortableCopy);
            if (change == StartupChange.NotAdded)
            {
                if (dialogs.Confirm(Strings.Settings_StartWithWindows, string.Format(Strings.Startup_PortableQuestion, startup?.ExecutablePath)))
                {
                    change = ApplyStartupRegistration(startup, settings, mayAdd: true);
                }
                else if (justTicked)
                {
                    // No to what was just ticked: the option goes off again, instead of showing on while nothing starts.
                    // When it was on already (set on another PC, carried by the stick or a synced copy) and only Start
                    // hidden changed, it stays on: off here would take the entry away on that PC at its next start.
                    var off = settings.Clone();
                    off.StartWithWindows = false;
                    applied = (false, off.StartHidden);
                    viewModel.ApplySettings(off);
                }
            }

            if (change == StartupChange.LeftToAnotherCopy)
            {
                dialogs.Notify(Strings.Settings_StartWithWindows, string.Format(Strings.Startup_AnotherCopy, startup?.RegisteredExecutable));
            }
        };

        var window = new MainWindow(viewModel);
        MainWindow = window;
        window.TrayReady += (_, _) =>
        {
            dialogs.Notifier = window.TryNotify;
            if (DataLocation.PortableFolderIsReadOnly)
            {
                dialogs.Notify(Strings.App_Name, string.Format(Strings.App_PortableReadOnly, DataLocation.Directory)); // once, at start
            }
        };
        if (startHidden)
        {
            // The handle is enough: SourceInitialized creates the tray icon and registers the hotkey, and the bar
            // appears on the first Show() from either of them.
            new WindowInteropHelper(window).EnsureHandle();
            AppLog.Default.Info("App", "Started hidden; the bar waits in the tray");
        }
        else
        {
            window.Show();
        }

        ListenForShowRequests(window);

        if (load.CorruptFileBackupPath is not null && !load.RecoveredFromBackup)
        {
            dialogs.ShowError(
                Strings.App_LayoutUnreadableTitle,
                string.Format(Strings.App_LayoutUnreadable, load.Error, load.CorruptFileBackupPath));
        }
        else if (load.RecoveredFromBackup)
        {
            dialogs.ShowInfo(
                Strings.App_LayoutRestoredTitle,
                string.Format(Strings.App_LayoutRestored, load.CorruptFileBackupPath is null ? string.Empty : string.Format(Strings.App_UnreadableKeptAt, load.CorruptFileBackupPath)));
        }
        else if (load.Error is not null)
        {
            dialogs.ShowError(Strings.App_LayoutUnreadableTitle, $"{load.Error}\n\n{storage.FilePath}");
        }
        else if (model.RepairCount > 0)
        {
            // Persist the repaired structure so the next start is clean.
            viewModel.SaveNow();
        }

        if (load.NewerVersion is not null)
        {
            dialogs.ShowInfo(Strings.App_LayoutNewerTitle, Strings.App_LayoutNewer);
        }

        _ = _sync.RunStartupCheckAsync();
        _ = viewModel.WarmCatalogIconsAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        AppLog.Default.Info("App", "Exiting");
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _showWait?.Unregister(null);
        _showEvent?.Dispose();
        _monitor?.Dispose();
        _windows?.Dispose();
        _tracker?.Dispose();
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }

    /// <summary>
    /// Takes the single-instance mutex and creates the show event; when another instance holds the mutex,
    /// signals that instance and returns false.
    /// </summary>
    private bool ClaimSingleInstance()
    {
        _instanceMutex = new Mutex(initiallyOwned: true, InstanceMutexName, out var createdNew);
        if (createdNew)
        {
            // Create the event right away: a second copy started during this one's startup must find it.
            _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
            return true;
        }

        try
        {
            using var show = EventWaitHandle.OpenExisting(ShowEventName);
            // This process is the one the user just started, so it may hand the foreground to the running instance.
            NativeMethods.AllowSetForegroundWindow(NativeMethods.ASFW_ANY);
            show.Set();
        }
        catch (Exception ex) when (ex is WaitHandleCannotBeOpenedException or UnauthorizedAccessException or IOException)
        {
            // The other instance is starting up or shutting down; nothing more to do.
        }

        _instanceMutex.Dispose();
        _instanceMutex = null;
        return false;
    }

    /// <summary>Waits (off the UI thread) for a second instance to ask for the bar, then shows it.</summary>
    private void ListenForShowRequests(MainWindow window)
    {
        if (_showEvent is null)
        {
            return;
        }

        _showWait = ThreadPool.RegisterWaitForSingleObject(
            _showEvent,
            (_, _) => Dispatcher.BeginInvoke(() =>
            {
                AppLog.Default.Info("App", "Show request received from another instance");
                window.ShowFromOtherInstance();
            }),
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);
    }

    /// <summary>Windows switched between light and dark mode: recolour the dialog brushes (the Fluent theme itself follows on its own).</summary>
    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category == UserPreferenceCategory.General && _fluentDialogsSetting is { } setting)
        {
            Dispatcher.BeginInvoke(() => DialogTheme.Configure(setting()));
        }
    }

    /// <summary>The Run-key registration for this executable, or <see langword="null"/> when the executable path is unknown.</summary>
    private static StartupRegistration? CreateStartupRegistration()
    {
        var executable = Environment.ProcessPath;
        return string.IsNullOrEmpty(executable) ? null : new StartupRegistration(new RegistryRunKey(), executable, portable: DataLocation.IsPortableCopy);
    }

    /// <summary>
    /// Writes, repairs or removes this program's Run entry so it matches the settings (see <see cref="StartupRegistration.Apply"/>);
    /// registry trouble is logged, never fatal.
    /// </summary>
    private static StartupChange ApplyStartupRegistration(StartupRegistration? startup, LauncherSettings settings, bool mayAdd)
    {
        if (startup is null)
        {
            return StartupChange.None;
        }

        try
        {
            var change = startup.Apply(settings.StartWithWindows, settings.StartHidden, mayAdd);
            switch (change)
            {
                case StartupChange.Written:
                    AppLog.Default.Info("App", $"Registered to start with Windows{(settings.StartHidden ? " (hidden)" : string.Empty)}: {startup.ExecutablePath}");
                    break;
                case StartupChange.Removed:
                    AppLog.Default.Info("App", "No longer starts with Windows");
                    break;
                case StartupChange.LeftToAnotherCopy:
                    AppLog.Default.Info("App", $"Start with Windows is left to another copy: {startup.RegisteredExecutable}");
                    break;
            }

            return change;
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            AppLog.Default.Warning("App", $"The Run registry entry could not be updated: {ex.Message}");
            return StartupChange.None;
        }
    }

    private static SyncService CreateSyncService()
    {
        var machine = MachineIdentity.Key();
        var settingsStore = new SyncSettingsStore(SyncSettingsStore.GetDefaultFilePath(), new DpapiSecretProtector(), machine);
        var settings = settingsStore.Load();
        if (settings.AdoptMachine(machine) || !File.Exists(settingsStore.FilePath))
        {
            // The device id tells this PC's saves from other PCs' saves, so it must be the same at every start.
            try
            {
                settingsStore.Save(settings);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Default.Warning("Sync", $"The device id could not be saved yet: {ex.Message}");
            }
        }

        // Per-PC locations belong to this PC and user, not to the data folder (portable mode, copied data).
        ThisDevice.Id = machine ?? settings.DeviceId;
        var tokenStore = new DpapiTokenStore(DpapiTokenStore.FileForThisPc(Path.GetDirectoryName(settingsStore.FilePath)!, machine, DataLocation.IsPortable));
        var auth = new GoogleAuthorization(Http, tokenStore, () => settings.Credentials);
        var drive = new GoogleDriveClient(Http, auth);
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3);
        return new SyncService(auth, drive, settings, () => settingsStore.Save(settings), version, files: drive);
    }

    private static HttpClient CreateHttpClient()
    {
        // Uploads and downloads can legitimately take longer than any fixed limit; callers apply per-request timeouts.
        var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0";
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"DesktopIconGrid/{version}");
        return client;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLog.Default.Error("App", "Unhandled exception", e.Exception);
        MessageBox.Show(
            string.Format(Strings.App_UnexpectedError, e.Exception.Message, Path.Combine(AppPaths.LogsDirectory, "app.log")),
            Strings.App_UnexpectedErrorTitle,
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        if (MainWindow is null)
        {
            // Nothing is on screen yet: a handled exception here would leave a window-less process behind.
            Shutdown(1);
            return;
        }

        e.Handled = true;
    }
}
