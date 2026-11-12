using System.Windows;
using DesktopIconGrid.Core.Diagnostics;
using DesktopIconGrid.Core.Imports;
using DesktopIconGrid.Core.Launching;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Resources;
using DesktopIconGrid.ViewModels;
using DesktopIconGrid.Views;

namespace DesktopIconGrid.Services;

public sealed class DialogService(IIconProvider icons) : IDialogService, ILaunchPrompts
{
    /// <summary>How long *Add app…* waits for the Start menu's list (hundreds of entries, each asked for its target).</summary>
    private static readonly TimeSpan AddAppListTimeout = TimeSpan.FromSeconds(30);

    private AppLogWindow? _logWindow;

    /// <summary>Creates the view model for the sync dialog; set once sync is wired up.</summary>
    public Func<SyncViewModel>? SyncViewModelFactory { get; set; }

    /// <summary>Shows a notice without interrupting (the tray balloon); returns false when it cannot, so a message box is used instead.</summary>
    public Func<string, string, bool>? Notifier { get; set; }

    public void Notify(string title, string message)
    {
        AppLog.Default.Warning("App", $"{title}: {message.ReplaceLineEndings(" ")}");
        if (Notifier is null || !Notifier(title, message))
        {
            Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    public void ShowAppLog()
    {
        if (_logWindow is { IsLoaded: true })
        {
            _logWindow.Activate();
            return;
        }

        var dispatcher = Application.Current?.Dispatcher ?? System.Windows.Threading.Dispatcher.CurrentDispatcher;
        _logWindow = new AppLogWindow { DataContext = new AppLogViewModel(AppLog.Default, dispatcher), Owner = Application.Current?.MainWindow };
        _logWindow.Closed += (_, _) => _logWindow = null;
        _logWindow.Show();
    }

    public void ShowSync()
    {
        if (SyncViewModelFactory is null)
        {
            ShowInfo(Strings.Sync_Notification, Strings.Sync_NotAvailable);
            return;
        }

        var window = new SyncWindow { DataContext = SyncViewModelFactory(), Owner = Owner };
        window.ShowDialog();
    }

    public bool ShowItemProperties(LauncherItem item, bool isNew)
    {
        var viewModel = new ItemPropertiesViewModel(item, isNew, icons, item is ShortcutItem ? BrowserCatalog.Choices() : null);
        var window = new ItemPropertiesWindow { DataContext = viewModel, Owner = Owner };
        return window.ShowDialog() == true;
    }

    public LauncherSettings? ShowSettings(LauncherSettings current)
    {
        var viewModel = new SettingsViewModel(current);
        var window = new SettingsWindow { DataContext = viewModel, Owner = Owner };
        return window.ShowDialog() == true ? viewModel.ToSettings() : null;
    }

    public void ShowNameTable(LauncherViewModel launcher)
    {
        var viewModel = new NameTableViewModel(launcher);
        var window = new NameTableWindow { DataContext = viewModel, Owner = Owner };
        if (window.ShowDialog() == true)
        {
            viewModel.Apply();
        }
    }

    public void ShowLog(LogViewerViewModel viewModel)
    {
        var window = new LogViewerWindow { DataContext = viewModel, Owner = Application.Current?.MainWindow };
        window.Show();
        window.Activate();
    }

    public void ShowFolderStack(string name, string folder, bool newestFirst, Func<string, IReadOnlyList<string>, LaunchResult>? openWith = null) =>
        new StackWindow(name, folder, newestFirst, icons) { OpenWith = openWith }.ShowAtPointer();

    public bool ConfirmApproval(string title, string intro, string details, string approveText) =>
        new CommandApprovalWindow(title, intro, details, approveText) { Owner = Owner }.ShowDialog() == true;

    public bool ConfirmDefaultNo(string title, string message)
    {
        var owner = Owner;
        var result = owner is null
            ? MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No)
            : MessageBox.Show(owner, message, title, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        return result == MessageBoxResult.Yes;
    }

    public bool Confirm(string title, string message) =>
        Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public void ShowError(string title, string message) =>
        Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);

    public void ShowInfo(string title, string message) =>
        Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    public string? ClipboardText()
    {
        try
        {
            return Clipboard.ContainsText() ? Clipboard.GetText() : null;
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            return null; // another program holds the clipboard open
        }
    }

    public string? Ask(string title, string prompt) => InputWindow.Show(Owner, title, prompt);

    public string? AskText(string title, string prompt, string? initialValue) => InputWindow.Show(Owner, title, prompt, initialValue);

    public IReadOnlyList<InstalledApp> ShowAddApp(IReadOnlySet<string> locationsOnBar)
    {
        var viewModel = new AddAppViewModel(locationsOnBar);
        var window = new AddAppWindow
        {
            DataContext = viewModel,
            Owner = Owner,
            Load = async () => await IconWorker.Shared.RunAsync(InstalledApps.Enumerate, AddAppListTimeout, "installed apps") ?? [],
            IconOf = location => IconWorker.Shared.RunAsync(() => icons.GetIcon(location, 32), IconWorker.LookupTimeout, location, IconWorker.IsOnSlowStorage(location)),
        };
        return window.ShowDialog() == true ? viewModel.Chosen : [];
    }

    public ImportRequest? ShowImport(IReadOnlyList<ShortcutItem> onBar, bool canGroup)
    {
        // A copy taken here, on the window's thread: the search reads it on the worker while sync may change the bar.
        var snapshot = onBar.Select(s => (ShortcutItem)s.Clone()).ToList();
        var viewModel = new ImportViewModel(new HashSet<string>(snapshot.Select(ImportPlan.KeyOf), StringComparer.OrdinalIgnoreCase), canGroup);
        var window = new ImportWindow
        {
            DataContext = viewModel,
            Owner = Owner,
            Load = async () =>
            {
                var found = await IconWorker.Shared.RunAsync(() => ImportDiscovery.FindAll(snapshot), AddAppListTimeout, "import sources");
                if (found is null)
                {
                    AppLog.Default.Warning("Import", $"The search for shortcuts and sessions was stopped after {AddAppListTimeout.TotalSeconds:0} s");
                }

                return found;
            },
            IconOf = location => IconWorker.Shared.RunAsync(() => icons.GetIcon(location, 32), IconWorker.LookupTimeout, location, IconWorker.IsOnSlowStorage(location)),
        };
        return window.ShowDialog() == true ? new ImportRequest(viewModel.Chosen, viewModel.GroupPerSource) : null;
    }

    public Core.Storage.LayoutHistoryEntry? ShowLayoutHistory(Func<IReadOnlyList<Core.Storage.LayoutHistoryEntry>> list)
    {
        var window = new LayoutHistoryWindow(() => IconWorker.Shared.RunAsync(list, AddAppListTimeout, "layout history")) { Owner = Owner };
        return window.ShowDialog() == true ? window.Chosen : null;
    }

    public RepairResult? ShowRepair(ShortcutItem shortcut)
    {
        ArgumentNullException.ThrowIfNull(shortcut);
        var location = shortcut.EffectiveLocation;
        var viewModel = new RepairViewModel(
            shortcut,
            cancellationToken => LocationRepair.Search(location, InstalledPrograms.Folders(), InstalledPrograms.RegisteredPath, cancellationToken),
            canBeOnlyThisPc: ThisDevice.Id is not null);
        var window = new RepairWindow { DataContext = viewModel, Owner = Owner };
        return window.ShowDialog() == true ? viewModel.Result : null;
    }

    public string? PickFolder(string title, string? initialFolder)
    {
        // A network share that is off takes up to a minute to say so: such a folder is not looked at on the UI thread,
        // and the dialog is not left to open where it was last used either, which is that share when it was picked there.
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = title, Multiselect = false, InitialDirectory = LocalFolderToOpen(initialFolder) };

        var owner = Owner;
        var picked = owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
        return picked == true && !string.IsNullOrWhiteSpace(dialog.FolderName) ? dialog.FolderName : null;
    }

    /// <summary><paramref name="folder"/> when it is on a local drive and there; otherwise Documents, or the profile folder when Documents is on a share.</summary>
    internal static string LocalFolderToOpen(string? folder)
    {
        if (!string.IsNullOrWhiteSpace(folder) && !IconWorker.IsOnSlowStorage(folder) && System.IO.Directory.Exists(folder))
        {
            return folder;
        }

        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return documents.Length > 0 && !IconWorker.IsOnSlowStorage(documents) && System.IO.Directory.Exists(documents)
            ? documents
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    private static Window? Owner
    {
        get
        {
            var application = Application.Current;
            if (application is null)
            {
                return null;
            }

            var active = application.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive && w.IsVisible);
            var main = application.MainWindow;
            return active ?? (main is { IsVisible: true } ? main : null);
        }
    }

    private static MessageBoxResult Show(string message, string title, MessageBoxButton buttons, MessageBoxImage image)
    {
        var owner = Owner;
        return owner is null
            ? MessageBox.Show(message, title, buttons, image)
            : MessageBox.Show(owner, message, title, buttons, image);
    }
}
