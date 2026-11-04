using System.Windows;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.ViewModels;
using DesktopIconGrid.Views;

namespace DesktopIconGrid.Services;

public sealed class DialogService(IIconProvider icons) : IDialogService
{
    /// <summary>Creates the view model for the sync dialog; set once sync is wired up.</summary>
    public Func<SyncViewModel>? SyncViewModelFactory { get; set; }

    public void ShowSync()
    {
        if (SyncViewModelFactory is null)
        {
            ShowInfo("Google Drive sync", "Sync is not available in this build.");
            return;
        }

        var window = new SyncWindow { DataContext = SyncViewModelFactory(), Owner = Owner };
        window.ShowDialog();
    }

    public bool ShowItemProperties(LauncherItem item, bool isNew)
    {
        var viewModel = new ItemPropertiesViewModel(item, isNew, icons);
        var window = new ItemPropertiesWindow { DataContext = viewModel, Owner = Owner };
        return window.ShowDialog() == true;
    }

    public LauncherSettings? ShowSettings(LauncherSettings current)
    {
        var viewModel = new SettingsViewModel(current);
        var window = new SettingsWindow { DataContext = viewModel, Owner = Owner };
        return window.ShowDialog() == true ? viewModel.ToSettings() : null;
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
