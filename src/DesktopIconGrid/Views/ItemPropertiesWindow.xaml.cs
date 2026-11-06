using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using DesktopIconGrid.Core.Storage;
using DesktopIconGrid.Resources;
using DesktopIconGrid.Services;
using DesktopIconGrid.ViewModels;
using Microsoft.Win32;

namespace DesktopIconGrid.Views;

/// <summary>Shortcut Properties / Group Properties dialog.</summary>
public partial class ItemPropertiesWindow : Window
{
    private static string LocationFilter => Strings.Props_ProgramFilter;

    private static string IconFilter => Strings.Props_ImageFilter;

    public ItemPropertiesWindow()
    {
        InitializeComponent();
        DialogTheme.Apply(this);
    }

    private ItemPropertiesViewModel? ViewModel => DataContext as ItemPropertiesViewModel;

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: { } menu } button)
        {
            menu.PlacementTarget = button;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        }
    }

    private void BrowseFile_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = Strings.Props_ChooseProgram,
            Filter = LocationFilter,
            CheckFileExists = true,
            DereferenceLinks = false,
            InitialDirectory = InitialDirectoryFor(viewModel.Location),
        };

        if (dialog.ShowDialog(this) == true)
        {
            viewModel.Location = DataLocation.ForStorage(dialog.FileName);
        }
    }

    private void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        var dialog = new OpenFolderDialog
        {
            Title = Strings.Props_ChooseFolder,
            InitialDirectory = InitialDirectoryFor(viewModel.Location),
        };

        if (dialog.ShowDialog(this) == true)
        {
            viewModel.Location = DataLocation.ForStorage(dialog.FolderName);
        }
    }

    private void ChangeIcon_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: { } menu } button)
        {
            menu.PlacementTarget = button;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        }
    }

    private async void ChangeIconUrl_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        string? initial = null;
        try
        {
            if (Clipboard.ContainsText() && IconDownloader.IsImageUrl(Clipboard.GetText()))
            {
                initial = Clipboard.GetText().Trim();
            }
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // Clipboard busy; no prefill.
        }

        var url = InputWindow.Show(this, Strings.Props_WebIconTitle, Strings.Props_WebIconPrompt, initial, Strings.Props_WebIconHint);
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        string? error = null;
        using var cancellation = new CancellationTokenSource();
        var busy = BusyWindow.Open(this, Strings.Props_WebIconTitle, string.Format(Strings.Props_Downloading, url.Trim()), cancellation);
        try
        {
            var path = await IconDownloader.DownloadAsync(App.SharedHttp, url, AppPaths.IconsDirectory, cancellation.Token);
            viewModel.IconPath = path;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // Cancelled by the user.
        }
        catch (Exception ex) when (ex is InvalidOperationException or OperationCanceledException or IOException or UnauthorizedAccessException)
        {
            error = ex.Message;
        }
        finally
        {
            busy.Finish();
        }

        if (error is not null)
        {
            MessageBox.Show(this, error, Strings.Props_WebIconTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ChangeIconFile_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = Strings.Props_ChooseIcon,
            Filter = IconFilter,
            CheckFileExists = true,
            InitialDirectory = InitialDirectoryFor(viewModel.IconPath ?? viewModel.Location),
        };

        if (dialog.ShowDialog(this) == true)
        {
            viewModel.IconPath = dialog.FileName;
        }
    }

    private void TextIcon_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        var editor = new TextIconViewModel(viewModel.TextIcon, viewModel.Name);
        var window = new TextIconWindow { DataContext = editor, Owner = this };
        if (window.ShowDialog() == true)
        {
            viewModel.TextIcon = editor.ToTextIcon();
        }
    }

    private void UseDefaultIcon_Click(object sender, RoutedEventArgs e) => ViewModel?.UseDefaultIcon();

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel?.Apply() == true)
        {
            DialogResult = true;
        }
    }

    private static string InitialDirectoryFor(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var expanded = Environment.ExpandEnvironmentVariables(path.Trim());
        var comma = expanded.LastIndexOf(',');
        if (comma > 0 && int.TryParse(expanded.AsSpan(comma + 1), out _))
        {
            expanded = expanded[..comma];
        }

        try
        {
            if (Directory.Exists(expanded))
            {
                return expanded;
            }

            var directory = Path.GetDirectoryName(expanded);
            return directory is not null && Directory.Exists(directory) ? directory : string.Empty;
        }
        catch (ArgumentException)
        {
            return string.Empty;
        }
    }
}
