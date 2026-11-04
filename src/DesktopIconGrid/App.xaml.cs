using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using DesktopIconGrid.Core;
using DesktopIconGrid.Core.Diagnostics;
using DesktopIconGrid.Core.Storage;
using DesktopIconGrid.Core.Sync;
using DesktopIconGrid.Services;
using DesktopIconGrid.SpecialIcons;
using DesktopIconGrid.ViewModels;
using DesktopIconGrid.Views;

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
            Start();
        }
        catch (Exception ex)
        {
            AppLog.Default.Error("App", "Startup failed", ex);
            MessageBox.Show(
                $"Desktop Icon Grid could not start.\n\n{ex.Message}\n\nDetails are in {AppPaths.LogsDirectory}\\app.log.",
                "Desktop Icon Grid",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private void Start()
    {
        // Claim the instance before touching app.log, so a second copy neither shares the file nor rotates it.
        if (!ClaimSingleInstance())
        {
            System.Diagnostics.Debug.WriteLine("Desktop Icon Grid is already running; asked it to show the bar.");
            Shutdown(0);
            return;
        }

        AppLog.Default.AttachFile(Path.Combine(AppPaths.LogsDirectory, "app.log"));
        AppLog.Default.Info("App", $"Desktop Icon Grid {Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)} starting");

        var storage = new LayoutStorage(LayoutStorage.GetDefaultFilePath());
        var load = storage.Load();
        var model = LauncherModel.FromLayout(load.Layout);
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
        var syncService = CreateSyncService();
        var cacheDirectory = AppPaths.DriveCacheDirectory;
        var specialIcons = new SpecialIconRegistry([
            new CpuUsageHandler(_monitor, owner),
            new DiskUsageHandler(_monitor, owner),
            new DriveBucketHandler(syncService, dialogs, owner, icons, cacheDirectory, Dispatcher),
            new SoundReplayerHandler(owner, AppPaths.SoundsDirectory, Dispatcher),
        ]);
        var launcher = new LaunchService(_tracker);
        var viewModel = new LauncherViewModel(
            model,
            storage,
            icons,
            dialogs,
            launcher,
            _tracker,
            specialIcons,
            action => Dispatcher.BeginInvoke(DispatcherPriority.Background, action));

        ThemeManager.Apply(viewModel.Palette);
        viewModel.SettingsChanged += (_, _) => ThemeManager.Apply(viewModel.Palette);

        _sync = new SyncCoordinator(syncService, viewModel, dialogs.Confirm);
        dialogs.SyncViewModelFactory = () => new SyncViewModel(_sync, dialogs);
        viewModel.FlushBeforeExit = () => _sync.FlushPendingAsync(TimeSpan.FromSeconds(5));
        _ = Task.Run(() => DriveCache.Purge(cacheDirectory, TimeSpan.FromDays(1)));

        var window = new MainWindow(viewModel);
        MainWindow = window;
        window.Show();
        ListenForShowRequests(window);

        if (load.CorruptFileBackupPath is not null && !load.RecoveredFromBackup)
        {
            dialogs.ShowError(
                "Layout file could not be read",
                $"The saved layout was unreadable and the bar starts empty.\n\n{load.Error}\n\nThe old file was kept at:\n{load.CorruptFileBackupPath}");
        }
        else if (load.RecoveredFromBackup)
        {
            dialogs.ShowInfo(
                "Layout restored from backup",
                $"The layout file was missing or unreadable, so the previous save (layout.json.bak) was loaded instead.{(load.CorruptFileBackupPath is null ? string.Empty : $"\n\nThe unreadable file was kept at:\n{load.CorruptFileBackupPath}")}");
        }
        else if (load.Error is not null)
        {
            dialogs.ShowError("Layout file could not be read", $"{load.Error}\n\n{storage.FilePath}");
        }
        else if (model.RepairCount > 0)
        {
            // Persist the repaired structure so the next start is clean.
            viewModel.SaveNow();
        }

        if (load.NewerVersion is not null)
        {
            dialogs.ShowInfo(
                "Layout from a newer version",
                "This layout was saved by a newer version of Desktop Icon Grid. Items this version does not understand are shown as placeholders and kept when you save; a copy of the original file is stored next to it.");
        }

        _ = _sync.RunStartupCheckAsync();
        _ = viewModel.WarmCatalogIconsAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        AppLog.Default.Info("App", "Exiting");
        _showWait?.Unregister(null);
        _showEvent?.Dispose();
        _monitor?.Dispose();
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

    private static SyncService CreateSyncService()
    {
        var settingsStore = new SyncSettingsStore(SyncSettingsStore.GetDefaultFilePath());
        var settings = settingsStore.Load();
        var tokenStore = new DpapiTokenStore(Path.Combine(Path.GetDirectoryName(settingsStore.FilePath)!, "google-token.bin"));
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
            $"{e.Exception.Message}\n\nDetails are in {AppPaths.LogsDirectory}\\app.log.",
            "Desktop Icon Grid – unexpected error",
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
