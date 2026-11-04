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
            // PasswordBox.Password cannot be bound, so the secret is copied both ways by hand.
            SecretBox.Password = viewModel.ClientSecret;
            viewModel.PropertyChanged += ViewModel_PropertyChanged;
            await viewModel.InitializeAsync();
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (ViewModel is { } viewModel)
        {
            viewModel.PropertyChanged -= ViewModel_PropertyChanged;
            CommitSecret(viewModel);
            viewModel.CancelPendingOperation();
        }
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SyncViewModel.ClientSecret) && ViewModel is { } viewModel && SecretBox.Password != viewModel.ClientSecret)
        {
            SecretBox.Password = viewModel.ClientSecret; // e.g. after Import client_secret.json
        }
    }

    private void SecretBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel)
        {
            CommitSecret(viewModel);
        }
    }

    private void CommitSecret(SyncViewModel viewModel)
    {
        if (viewModel.ClientSecret != SecretBox.Password)
        {
            viewModel.ClientSecret = SecretBox.Password;
        }
    }

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
