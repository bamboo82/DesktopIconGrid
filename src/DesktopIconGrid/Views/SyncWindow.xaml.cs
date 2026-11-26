using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using DesktopIconGrid.Resources;
using DesktopIconGrid.ViewModels;
using Microsoft.Win32;

namespace DesktopIconGrid.Views;

public partial class SyncWindow : Window
{
    public SyncWindow()
    {
        InitializeComponent();
        DialogTheme.Apply(this);
        WorkAreaFit.Attach(this);
        DataContextChanged += (_, _) => PlaceAccount();
        AccountLine.SizeChanged += (_, _) => FitAccountLineHeader();
    }

    private SyncViewModel? ViewModel => DataContext as SyncViewModel;

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel)
        {
            // PasswordBox.Password cannot be bound, so the secret is copied both ways by hand.
            SecretBox.Password = viewModel.ClientSecret;
            viewModel.PropertyChanged += ViewModel_PropertyChanged;
            PlaceAccount();
            await viewModel.InitializeAsync();
        }
    }

    /// <summary>
    /// Puts the Google account's panel where the place in use wants it (0.16.1 WP2): in its group under the choice of
    /// place while Google Drive keeps the copies, in the last line with a folder, closed as it comes unless a sign-in runs
    /// or has just failed. It is one panel, moved, so nothing typed in it (the client ID, the secret) is lost; while it is
    /// not shown it stays where it was, collapsed with its host.
    /// </summary>
    private void PlaceAccount()
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        HeaderedContentControl? host = viewModel.AccountPlace switch
        {
            SyncAccountPlace.Group => AccountGroup,
            SyncAccountPlace.Line => AccountLine,
            _ => null,
        };
        if (host is null || ReferenceEquals(host.Content, AccountPanel))
        {
            return;
        }

        if (AccountPanel.Parent is ContentControl from)
        {
            from.Content = null; // its presenter lets the panel go at once, so the other host can take it
        }

        var inLine = ReferenceEquals(host, AccountLine);
        AccountPanel.Margin = inLine ? new Thickness(0, 8, 0, 0) : new Thickness(0);
        if (inLine)
        {
            AccountLine.IsExpanded = viewModel.IsConnecting || viewModel.ConnectFailed;
        }

        host.Content = AccountPanel;
    }

    /// <summary>
    /// The account line's header takes the width its expander's template gives it, so the account's state stands at its
    /// right (0.16.1 WP2): the classic template puts a header at the left at its own width, the Windows 11 one before
    /// its chevron. That width is the slot of the header's presenter less its margins, read after each layout of the line.
    /// </summary>
    private void FitAccountLineHeader()
    {
        if (VisualTreeHelper.GetParent(AccountLineHeader) is not FrameworkElement presenter)
        {
            return;
        }

        var slot = LayoutInformation.GetLayoutSlot(presenter);
        var width = Math.Floor(slot.Width - presenter.Margin.Left - presenter.Margin.Right);
        if (width > 0 && (double.IsNaN(AccountLineHeader.Width) || Math.Abs(AccountLineHeader.Width - width) >= 1))
        {
            AccountLineHeader.Width = width;
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
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        switch (e.PropertyName)
        {
            case nameof(SyncViewModel.ClientSecret) when !SameSecret(SecretBox.Password, viewModel.ClientSecret):
                SecretBox.Password = viewModel.ClientSecret; // e.g. after Import client_secret.json
                break;
            case nameof(SyncViewModel.AccountPlace):
                PlaceAccount();
                break;
            case nameof(SyncViewModel.IsConnecting) or nameof(SyncViewModel.ConnectFailed)
                when (viewModel.IsConnecting || viewModel.ConnectFailed) && viewModel.AccountPlace == SyncAccountPlace.Line:
                AccountLine.IsExpanded = true; // a sign-in runs, or has just failed: what it says is in the line (0.16.1 WP2)
                break;
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
        if (!SameSecret(viewModel.ClientSecret, SecretBox.Password))
        {
            viewModel.ClientSecret = SecretBox.Password;
        }
    }

    private static bool SameSecret(string? a, string? b) =>
        string.Equals(a ?? string.Empty, b ?? string.Empty, StringComparison.Ordinal);

    private void ImportCredentials_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = Strings.Sync_ChooseSecretFile,
            Filter = Strings.Sync_JsonFilter,
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
            MessageBox.Show(this, ex.Message, Strings.Sync_ImportTitle, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
