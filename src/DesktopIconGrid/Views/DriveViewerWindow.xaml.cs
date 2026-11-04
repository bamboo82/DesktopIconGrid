using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using DesktopIconGrid.Resources;
using DesktopIconGrid.ViewModels;
using Microsoft.Win32;

namespace DesktopIconGrid.Views;

/// <summary>The Bucket Viewer: browse a Drive folder, open or download files, drag them out, drop files in.</summary>
public partial class DriveViewerWindow : Window
{
    private Point _dragStart;
    private bool _dragArmed;

    public DriveViewerWindow()
    {
        InitializeComponent();
        DialogTheme.Apply(this);
    }

    private DriveViewerViewModel? ViewModel => DataContext as DriveViewerViewModel;

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel)
        {
            await viewModel.InitializeAsync();
        }
    }

    private IReadOnlyList<DriveEntryViewModel> SelectedRows => FileList.SelectedItems.OfType<DriveEntryViewModel>().ToList();

    private async void Crumb_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel && (sender as FrameworkElement)?.DataContext is DriveCrumb crumb)
        {
            await viewModel.NavigateToCrumbAsync(crumb);
        }
    }

    private async void FileList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel is { } viewModel && RowAt(e.OriginalSource) is { } row)
        {
            await viewModel.OpenEntryAsync(row);
        }
    }

    private async void FileList_KeyDown(object sender, KeyEventArgs e)
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Enter when FileList.SelectedItem is DriveEntryViewModel row:
                e.Handled = true;
                await viewModel.OpenEntryAsync(row);
                break;
            case Key.Back:
                e.Handled = true;
                if (viewModel.UpCommand.CanExecute(null))
                {
                    viewModel.UpCommand.Execute(null);
                }

                break;
        }
    }

    private async void DownloadTo_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        var rows = SelectedRows;
        if (rows.Count == 0)
        {
            MessageBox.Show(this, Strings.Viewer_SelectFirst, Strings.Viewer_Download, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new OpenFolderDialog { Title = Strings.Viewer_ChooseDestination };
        if (dialog.ShowDialog(this) == true)
        {
            await viewModel.DownloadToAsync(dialog.FolderName, rows);
        }
    }

    // ----- drag out: download to the cache, then hand real files to the receiver -------------------

    private void FileList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragArmed = RowAt(e.OriginalSource) is not null && FindAncestor<ButtonBase>(e.OriginalSource as DependencyObject) is null;
        _dragStart = e.GetPosition(FileList);
    }

    private async void FileList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragArmed || e.LeftButton != MouseButtonState.Pressed || ViewModel is not { } viewModel)
        {
            return;
        }

        var delta = e.GetPosition(FileList) - _dragStart;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _dragArmed = false;
        var rows = SelectedRows;
        if (rows.Count == 0)
        {
            return;
        }

        var paths = await viewModel.PrepareLocalCopiesAsync(rows);
        if (paths.Count == 0 || Mouse.LeftButton != MouseButtonState.Pressed)
        {
            return; // nothing to give, or the button was released while downloading
        }

        var data = new DataObject(DataFormats.FileDrop, paths.ToArray());
        DragDrop.DoDragDrop(FileList, data, DragDropEffects.Copy);
    }

    // ----- drop in: upload into the current folder ---------------------------------------------------

    private void FileList_DragOver(object sender, DragEventArgs e)
    {
        var fromSelf = ReferenceEquals(e.Source, FileList) && e.Data.GetDataPresent(DataFormats.FileDrop) && e.AllowedEffects == DragDropEffects.Copy && _dragArmed == false && Mouse.Captured is not null;
        e.Effects = !fromSelf && ViewModel is { CanUpload: true } && e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void FileList_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (ViewModel is { CanUpload: true } viewModel
            && e.Data.GetDataPresent(DataFormats.FileDrop)
            && e.Data.GetData(DataFormats.FileDrop) is string[] paths
            && paths.Length > 0)
        {
            viewModel.Upload(paths);
        }
    }

    private static DriveEntryViewModel? RowAt(object source) =>
        FindAncestor<ListViewItem>(source as DependencyObject)?.DataContext as DriveEntryViewModel;

    private static T? FindAncestor<T>(DependencyObject? element)
        where T : DependencyObject
    {
        for (var current = element; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is T match)
            {
                return match;
            }

            if (current is not Visual)
            {
                return null;
            }
        }

        return null;
    }
}
