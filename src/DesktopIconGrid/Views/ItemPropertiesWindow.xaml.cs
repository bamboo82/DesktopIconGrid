using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using DesktopIconGrid.Services;
using DesktopIconGrid.ViewModels;
using Microsoft.Win32;

namespace DesktopIconGrid.Views;

/// <summary>Shortcut Properties / Group Properties dialog.</summary>
public partial class ItemPropertiesWindow : Window
{
    private const string LocationFilter =
        "Programs and shortcuts|*.exe;*.lnk;*.bat;*.cmd;*.msc;*.url;*.appref-ms|All files|*.*";

    private const string IconFilter =
        "Icons and images|*.ico;*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.exe;*.dll|All files|*.*";

    public ItemPropertiesWindow()
    {
        InitializeComponent();
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
            Title = "Choose a program, file or shortcut",
            Filter = LocationFilter,
            CheckFileExists = true,
            DereferenceLinks = false,
            InitialDirectory = InitialDirectoryFor(viewModel.Location),
        };

        if (dialog.ShowDialog(this) == true)
        {
            viewModel.Location = dialog.FileName;
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
            Title = "Choose a folder",
            InitialDirectory = InitialDirectoryFor(viewModel.Location),
        };

        if (dialog.ShowDialog(this) == true)
        {
            viewModel.Location = dialog.FolderName;
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

        var url = InputWindow.Show(
            this,
            "Icon from the web",
            "Address of the image (right-click a picture in your browser → Copy image address):",
            initial,
            "The image is downloaded once, scaled to 256 px, centred on a transparent square and saved as PNG in the app's icons folder.");
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        IsEnabled = false;
        try
        {
            var path = await IconDownloader.DownloadAsync(App.SharedHttp, url, AppPaths.IconsDirectory, CancellationToken.None);
            viewModel.IconPath = path;
        }
        catch (Exception ex) when (ex is InvalidOperationException or OperationCanceledException or System.IO.IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, ex.Message, "Icon from the web", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            IsEnabled = true;
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
            Title = "Choose an icon",
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
