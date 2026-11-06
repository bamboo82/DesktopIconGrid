using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using DesktopIconGrid.Resources;
using DesktopIconGrid.Services;
using DesktopIconGrid.ViewModels;
using Microsoft.Win32;

namespace DesktopIconGrid.Views;

/// <summary>Repair shortcut: where a missing program is now, and whether the fix is for this PC only.</summary>
public partial class RepairWindow : Window
{
    private readonly CancellationTokenSource _search = new();

    public RepairWindow()
    {
        InitializeComponent();
        DialogTheme.Apply(this);
    }

    private RepairViewModel? ViewModel => DataContext as RepairViewModel;

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel)
        {
            await viewModel.SearchAsync(_search.Token);
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e) => _search.Cancel();

    protected override void OnClosed(EventArgs e)
    {
        _search.Dispose();
        base.OnClosed(e);
    }

    /// <summary>A file or a folder, as the "…" button in Properties offers: no guess from the old name holds for both.</summary>
    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: { } menu } button)
        {
            menu.PlacementTarget = button;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        }
    }

    private void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        var folders = new OpenFolderDialog { Title = Strings.Repair_Title, Multiselect = false, InitialDirectory = NearestFolder(ViewModel?.Location) };
        if (ViewModel is { } viewModel && folders.ShowDialog(this) == true && !string.IsNullOrWhiteSpace(folders.FolderName))
        {
            viewModel.AddChosen(folders.FolderName);
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
            Title = Strings.Repair_Title,
            Filter = Strings.Common_AllFilesFilter,
            FileName = Path.GetFileName(viewModel.Location.TrimEnd('\\', '/')),
            InitialDirectory = NearestFolder(viewModel.Location),
        };
        if (dialog.ShowDialog(this) == true)
        {
            viewModel.AddChosen(dialog.FileName);
        }
    }

    /// <summary>
    /// The nearest folder above the old location that is still there, on a local drive (a share that is off would hold
    /// up the dialog); otherwise a local folder, so the picker does not open where it was last used.
    /// </summary>
    private static string NearestFolder(string? location)
    {
        var path = Environment.ExpandEnvironmentVariables(location?.Trim().Trim('"') ?? string.Empty);
        if (path.Length > 0 && !IconWorker.IsOnSlowStorage(path))
        {
            try
            {
                for (var folder = Path.GetDirectoryName(path); !string.IsNullOrEmpty(folder); folder = Path.GetDirectoryName(folder))
                {
                    if (Directory.Exists(folder))
                    {
                        return folder;
                    }
                }
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                // not a path: the local fallback below
            }
        }

        return DialogService.LocalFolderToOpen(null);
    }

    private void Repair_Click(object sender, RoutedEventArgs e) => DialogResult = ViewModel?.CanRepair == true;
}
