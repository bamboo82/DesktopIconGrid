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
    public StackEntry Entry => entry;

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
    private int _fillRequest;
    private (int X, int Y)? _anchor;
    private bool _placedWithRows;
    private bool _openedByDrop;

    public StackWindow(string name, string folder, bool newestFirst, IIconProvider icons)
    {
        InitializeComponent();
        _folder = folder;
        _newestFirst = newestFirst;
        _icons = icons;
        Title = name;
        Caption.Text = name;
        List.ItemsSource = _rows;
        _refresh = new DispatcherTimer(TimeSpan.FromMilliseconds(400), DispatcherPriority.Background, (_, _) => RefreshWhenSafe(), Dispatcher);
        _refresh.Stop();
        Deactivated += (_, _) => Dismiss();
        Closing += (_, _) => _closing = true; // Alt+F4: the deactivation that follows must not close it again
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

        _anchor = (pointer.X, pointer.Y);
        Place();
        Show();
        Activate();
        List.Focus();
    }

    /// <summary>
    /// Puts the window at the pointer it was opened at, inside that monitor's work area, for the size it has now: once
    /// when it opens (still empty) and again when the rows are in, since the list arrives after the window.
    /// </summary>
    private void Place()
    {
        if (_anchor is not { } pointer)
        {
            return;
        }

        var area = ScreenGeometry.AreaAt(pointer.X, pointer.Y);
        var scale = area?.Scale ?? ScreenGeometry.PrimaryScale();
        UpdateLayout();
        Measure(new Size(Width, MaxHeight));
        var rect = PopupPlacement.AtPointer(
            pointer.X,
            pointer.Y,
            (int)Math.Ceiling(Width * scale),
            (int)Math.Ceiling(Math.Min(DesiredSize.Height, MaxHeight) * scale),
            area?.WorkArea ?? default,
            grabOffset: (int)Math.Round(12 * scale));
        ScreenGeometry.MoveToPixels(this, rect);
    }

    /// <summary>
    /// A change in the folder: the list is read again, but not while the pointer is over it or a button is down. A
    /// finished download would otherwise move the rows just before a click, which then opens another file (review 24 S4).
    /// The timer keeps running and tries again until the pointer has left.
    /// </summary>
    private void RefreshWhenSafe()
    {
        if (List.IsMouseOver || Mouse.LeftButton == MouseButtonState.Pressed || _dragging)
        {
            return; // tried again at the next tick
        }

        _refresh.Stop();
        Fill();
    }

    /// <summary>
    /// Lists the folder on a worker thread (a share that sleeps must not freeze the bar, review 24 S1) and shows the
    /// rows when the list is there. A later call replaces an earlier one still under way.
    /// </summary>
    private async void Fill()
    {
        var request = ++_fillRequest;
        IReadOnlyList<StackEntry> entries;
        int total;
        try
        {
            (entries, total) = await Task.Run(() => FolderStack.List(_folder, _newestFirst));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // async void: nothing may escape to the dispatcher.
            (entries, total) = ([], FolderStack.Unreadable);
            AppLog.Default.Warning("Launch", $"The stack of {_folder} could not be listed: {ex.Message}");
        }

        if (request != _fillRequest || _closing)
        {
            return;
        }

        var selected = (List.SelectedItem as StackRow)?.Path;
        _rows.Clear();
        foreach (var entry in entries)
        {
            var row = new StackRow(entry);
            _rows.Add(row);
            _ = LoadIconAsync(row);
        }

        if (selected is not null && _rows.FirstOrDefault(row => string.Equals(row.Path, selected, StringComparison.OrdinalIgnoreCase)) is { } again)
        {
            List.SelectedItem = again; // the keyboard's place survives a refresh
        }

        Empty.Text = total == FolderStack.Unreadable ? Strings.Stack_Unreadable : Strings.Stack_Empty;
        Empty.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        More.Visibility = total > entries.Count ? Visibility.Visible : Visibility.Collapsed;
        More.Text = total > entries.Count ? string.Format(Strings.Stack_More, total - entries.Count) : string.Empty;
        if (!_placedWithRows)
        {
            _placedWithRows = true; // the first list only: a later refresh must not move the window under the pointer
            Place();
        }
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
        if (IconWorker.IsOnSlowStorage(_folder))
        {
            return; // a share or a mapped network drive: read when the stack opens, not watched
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
        // Started in the stack's folder, as a double-click in Explorer does: a batch file there finds its neighbours.
        var result = ProcessLauncher.Start(new ProcessStartInfo(path) { UseShellExecute = true, WorkingDirectory = !IconWorker.IsOnSlowStorage(_folder) && Directory.Exists(_folder) ? _folder : string.Empty });
        result.Process?.Dispose();
        if (result.Outcome == LaunchOutcome.Failed)
        {
            _dragging = true; // the box takes the focus: keep the stack (and with it the box) open
            try
            {
                MessageBox.Show(this, $"{result.ErrorMessage}\n{path}", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                _dragging = false;
            }

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

        if (!IsActive || _openedByDrop)
        {
            Dismiss(); // dragged out, or dropped onto a program here: the stack has done its work
        }
    }

    /// <summary>Opens files with a program of the stack; set by whoever shows the stack. Without it rows take no drops.</summary>
    public Func<string, IReadOnlyList<string>, LaunchResult>? OpenWith { get; init; }

    private static IReadOnlyList<string> DraggedFiles(DragEventArgs e) =>
        e.Data.GetDataPresent(DataFormats.FileDrop) && e.Data.GetData(DataFormats.FileDrop) is string[] files ? files : [];

    private StackRow? DropTarget(DragEventArgs e) =>
        OpenWith is not null
        && e.OriginalSource is DependencyObject element
        && ItemsControl.ContainerFromElement(List, element) is ListBoxItem { DataContext: StackRow row }
        && FolderStack.TakesDrop(row.Entry, DraggedFiles(e))
            ? row
            : null;

    private void List_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = DropTarget(e) is null ? DragDropEffects.None : DragDropEffects.Copy;
        e.Handled = true;
    }

    /// <summary>A file dropped onto a program, batch file or shortcut of the stack: opened with it.</summary>
    private void List_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (DropTarget(e) is not { } row || OpenWith is not { } openWith)
        {
            return;
        }

        e.Effects = DragDropEffects.Copy; // the file stays where it is
        var result = openWith(row.Path, DraggedFiles(e));
        if (result.Outcome == LaunchOutcome.Failed)
        {
            // After the drag has ended, so the box is not shown from inside DoDragDrop.
            Dispatcher.BeginInvoke(() =>
            {
                _dragging = true;
                try
                {
                    MessageBox.Show(this, $"{result.ErrorMessage}\n{row.Path}", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                finally
                {
                    _dragging = false;
                }
            });
            return;
        }

        _openedByDrop = true;
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
