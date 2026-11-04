using System.IO;
using System.Net.Http;
using System.Reflection;
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
    private static readonly HttpClient Http = CreateHttpClient();

    /// <summary>The app-wide HTTP client (Google APIs, icon downloads).</summary>
    public static HttpClient SharedHttp => Http;
    private ProcessTracker? _tracker;
    private SystemMonitor? _monitor;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        AppLog.Default.AttachFile(Path.Combine(AppPaths.LogsDirectory, "app.log"));
        AppLog.Default.Info("App", $"Desktop Icon Grid {Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)} starting");

        var storage = new LayoutStorage(LayoutStorage.GetDefaultFilePath());
        var load = storage.Load();
        var model = LauncherModel.FromLayout(load.Layout);
        AppLog.Default.Info("App", load.IsFirstRun
            ? "No layout file yet; starting with an empty bar"
            : $"Layout loaded: {model.Items.Count} top-level items, {model.TotalShortcutCount} shortcuts{(model.RepairCount > 0 ? $", {model.RepairCount} repaired" : string.Empty)}");
        if (load.Error is not null)
        {
            AppLog.Default.Error("App", $"Layout file could not be read: {load.Error}");
        }

        var icons = new ShellIconProvider();
        var dialogs = new DialogService(icons);
        _tracker = new ProcessTracker(Dispatcher, Path.Combine(storage.Directory, "logs"));
        _monitor = new SystemMonitor(Dispatcher);
        Func<Window?> owner = () => Current?.MainWindow;
        var syncService = CreateSyncService();
        var cacheDirectory = Path.Combine(storage.Directory, "drive-cache");
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

        var sync = new SyncCoordinator(syncService, viewModel, dialogs);
        dialogs.SyncViewModelFactory = () => new SyncViewModel(sync, dialogs);
        _ = Task.Run(() => DriveCache.Purge(cacheDirectory, TimeSpan.FromDays(1)));

        var window = new MainWindow(viewModel);
        MainWindow = window;
        window.Show();

        if (load.CorruptFileBackupPath is not null)
        {
            dialogs.ShowError(
                "Layout file could not be read",
                $"The saved layout was unreadable and the bar starts empty.\n\n{load.Error}\n\nThe old file was kept at:\n{load.CorruptFileBackupPath}");
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

        _ = sync.RunStartupCheckAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        AppLog.Default.Info("App", "Exiting");
        _monitor?.Dispose();
        _tracker?.Dispose();
        base.OnExit(e);
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
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(100) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("DesktopIconGrid/0.1");
        return client;
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLog.Default.Error("App", "Unhandled exception", e.Exception);
        MessageBox.Show(
            $"{e.Exception.Message}\n\n{e.Exception}",
            "Desktop Icon Grid – unexpected error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }
}
