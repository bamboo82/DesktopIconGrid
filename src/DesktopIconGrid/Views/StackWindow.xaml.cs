using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using DesktopIconGrid.Core.Diagnostics;
using DesktopIconGrid.Core.Launching;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Infrastructure;
using DesktopIconGrid.Resources;
using DesktopIconGrid.Services;

namespace DesktopIconGrid.Views;

/// <summary>One row of a folder stack; its icon arrives after the row is shown.</summary>
public sealed class StackRow(StackEntry entry) : ObservableObject
{
    private ImageSource? _image;

    public string Path => entry.Path;

    public string Name => entry.Name;

    public ImageSource? Image
    {
        get => _image;
        set => SetProperty(ref _image, value);
    }
}

/// <summary>
/// A folder shortcut opened as a stack: the folder's contents in a small list at the pointer. A click opens an entry,
/// a drag takes the real file out, <em>Open folder</em> shows it in Explorer. It follows changes in the folder (not on a
/// network share, which is read when the stack opens) and closes when it loses the focus.
/// </summary>
public partial class StackWindow : Window
{
    private readonly string _folder;
    private readonly bool _newestFirst;
    private readonly IIconProvider _icons;
    private readonly ObservableCollection<StackRow> _rows = [];
    private readonly DispatcherTimer _refresh;
    private FileSystemWatcher? _watcher;
    private Point _pressedAt;
    private StackRow? _pressed;
    private bool _dragging;
    private bool _closing;

    public StackWindow(string name, string folder, bool newestFirst, IIconProvider icons)
    {
        InitializeComponent();
        _folder = folder;
        _newestFirst = newestFirst;
        _icons = icons;
        Title = name;
        Caption.Text = name;
        List.ItemsSource = _rows;
        _refresh = new DispatcherTimer(TimeSpan.FromMilliseconds(400), DispatcherPriority.Background, (_, _) => { _refresh!.Stop(); Fill(); }, Dispatcher);
        _refresh.Stop();
        Deactivated += (_, _) => Dismiss();
        Closed += (_, _) => _watcher?.Dispose();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Dismiss();
            }
        };
    }

    /// <summary>Fills the list and shows the window at the mouse pointer, inside its monitor's work area.</summary>
    public void ShowAtPointer()
    {
        Fill();
        Watch();
        new WindowInteropHelper(this).EnsureHandle();
        if (!NativeMethods.GetCursorPos(out var pointer))
        {
            pointer = default;
        }

        var area = ScreenGeometry.AreaAt(pointer.X, pointer.Y);
        var scale = area?.Scale ?? ScreenGeometry.PrimaryScale();
        Measure(new Size(Width, MaxHeight));
        var rect = PopupPlacement.AtPointer(
            pointer.X,
            pointer.Y,
            (int)Math.Ceiling(Width * scale),
            (int)Math.Ceiling(Math.Min(DesiredSize.Height, MaxHeight) * scale),
            area?.WorkArea ?? default,
            grabOffset: (int)Math.Round(12 * scale));
        ScreenGeometry.MoveToPixels(this, rect);
        Show();
        Activate();
        List.Focus();
    }

    private void Fill()
    {
        var (entries, total) = FolderStack.List(_folder, _newestFirst);
        _rows.Clear();
        foreach (var entry in entries)
        {
            var row = new StackRow(entry);
            _rows.Add(row);
            _ = LoadIconAsync(row);
        }

        Empty.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        More.Visibility = total > entries.Count ? Visibility.Visible : Visibility.Collapsed;
        More.Text = total > entries.Count ? string.Format(Strings.Stack_More, total - entries.Count) : string.Empty;
    }

    private async Task LoadIconAsync(StackRow row)
    {
        try
        {
            row.Image = await IconWorker.Shared.RunAsync(() => _icons.GetIcon(row.Path, 32), IconWorker.LookupTimeout, row.Path);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            AppLog.Default.Debug("Icons", $"No icon for {row.Path}: {ex.Message}");
        }
    }

    private void Watch()
    {
        if (_folder.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return; // a share: read when the stack opens, not watched
        }

        try
        {
            _watcher = new FileSystemWatcher(_folder) { NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite };
            FileSystemEventHandler changed = (_, _) => Dispatcher.BeginInvoke(() => { _refresh.Stop(); _refresh.Start(); });
            _watcher.Created += changed;
            _watcher.Deleted += changed;
            _watcher.Changed += changed;
            _watcher.Renamed += (_, _) => Dispatcher.BeginInvoke(() => { _refresh.Stop(); _refresh.Start(); });
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            _watcher = null; // the list just does not follow changes
        }
    }

    private void Dismiss()
    {
        if (_closing || _dragging)
        {
            return; // a drag out of the stack takes the focus away; the stack closes when it ends
        }

        _closing = true;
        Close();
    }

    private void Open(string path)
    {
        var result = ProcessLauncher.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        result.Process?.Dispose();
        if (result.Outcome == LaunchOutcome.Failed)
        {
            MessageBox.Show(this, $"{result.ErrorMessage}\n{path}", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Dismiss();
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e) => Open(_folder);

    private void List_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && List.SelectedItem is StackRow row)
        {
            e.Handled = true;
            Open(row.Path);
        }
    }

    private StackRow? RowOf(MouseEventArgs e) =>
        e.OriginalSource is DependencyObject element && ItemsControl.ContainerFromElement(List, element) is ListBoxItem { DataContext: StackRow row } ? row : null;

    private void List_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _pressed = RowOf(e);
        _pressedAt = e.GetPosition(this);
    }

    private void List_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_pressed is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var moved = e.GetPosition(this) - _pressedAt;
        if (Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var row = _pressed;
        _pressed = null;
        _dragging = true;
        try
        {
            DragDrop.DoDragDrop(List, new DataObject(DataFormats.FileDrop, new[] { row.Path }), DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
        }
        finally
        {
            _dragging = false;
        }

        if (!IsActive)
        {
            Dismiss();
        }
    }

    private void List_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var row = _pressed;
        _pressed = null;
        if (row is not null && ReferenceEquals(row, RowOf(e)))
        {
            Open(row.Path); // a click, not a drag
        }
    }
}
