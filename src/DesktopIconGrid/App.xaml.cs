using System.Windows;
using System.Windows.Threading;
using DesktopIconGrid.Core;
using DesktopIconGrid.Core.Storage;
using DesktopIconGrid.Services;
using DesktopIconGrid.ViewModels;
using DesktopIconGrid.Views;

namespace DesktopIconGrid;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        var storage = new LayoutStorage(LayoutStorage.GetDefaultFilePath());
        var load = storage.Load();
        var model = LauncherModel.FromLayout(load.Layout);

        var icons = new ShellIconProvider();
        var dialogs = new DialogService(icons);
        var launcher = new LaunchService();
        var viewModel = new LauncherViewModel(
            model,
            storage,
            icons,
            dialogs,
            launcher,
            action => Dispatcher.BeginInvoke(DispatcherPriority.Background, action));

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
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            $"{e.Exception.Message}\n\n{e.Exception}",
            "Desktop Icon Grid – unexpected error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }
}
