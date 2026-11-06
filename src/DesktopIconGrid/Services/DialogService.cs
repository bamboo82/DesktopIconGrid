using System.Windows;
using DesktopIconGrid.Core.Diagnostics;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Resources;
using DesktopIconGrid.ViewModels;
using DesktopIconGrid.Views;

namespace DesktopIconGrid.Services;

public sealed class DialogService(IIconProvider icons) : IDialogService, ILaunchPrompts
{
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

    public string? PickFolder(string title, string? initialFolder)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = title, Multiselect = false };
        if (!string.IsNullOrWhiteSpace(initialFolder) && System.IO.Directory.Exists(initialFolder))
        {
            dialog.InitialDirectory = initialFolder;
        }

        var owner = Owner;
        var picked = owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
        return picked == true && !string.IsNullOrWhiteSpace(dialog.FolderName) ? dialog.FolderName : null;
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
