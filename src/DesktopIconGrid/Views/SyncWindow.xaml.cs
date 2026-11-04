using System.ComponentModel;
using System.IO;
using System.Windows;
using DesktopIconGrid.ViewModels;
using Microsoft.Win32;

namespace DesktopIconGrid.Views;

public partial class SyncWindow : Window
{
    public SyncWindow()
    {
        InitializeComponent();
        DialogTheme.Apply(this);
    }

    private SyncViewModel? ViewModel => DataContext as SyncViewModel;

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel)
        {
            await viewModel.InitializeAsync();
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e) => ViewModel?.CancelPendingOperation();

    private void ImportCredentials_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "Choose the client_secret JSON file from Google Cloud Console",
            Filter = "JSON files|*.json|All files|*.*",
            CheckFileExists = true,
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            viewModel.ImportCredentials(File.ReadAllText(dialog.FileName));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, ex.Message, "Import credentials", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
