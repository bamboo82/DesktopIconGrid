using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using DesktopIconGrid.Core.Diagnostics;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Services;
using DesktopIconGrid.ViewModels;

namespace DesktopIconGrid.Views;

/// <summary>
/// The bar's icons in a small window under the mouse pointer, opened by the Show-at-pointer hotkey with the search box
/// focused. It works whether the bar is shown, hidden in the tray or tucked away at a screen edge, and goes away after
/// a launch, on Esc, on Alt+F4 and when another window takes the focus. A group opens in place, with a way back.
/// The window is made once and only hidden after that; it closes for real when the app quits (<see cref="CloseForGood"/>).
/// </summary>
public partial class PointerWindow : Window
{
    /// <summary>Up to this many cells side by side, and at least this many so the search box has room.</summary>
    internal const int MaxColumns = 6;

    internal const int MinColumns = 3;

    /// <summary>More rows than this scroll.</summary>
    internal const int MaxRows = 5;

    /// <summary>Room to spare under the rows, so rounding to whole pixels at another display scale never brings up a scroll bar.</summary>
    internal const double RoundingSlack = 2;

    /// <summary>How far above the pointer the window's top edge goes, so the pointer rests on the search box.</summary>
    private const double GrabOffset = 14;

    private readonly LauncherViewModel _viewModel;
    private bool _dismissing;
    private bool _closingForGood;

    public PointerWindow(LauncherViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        Icons.DataContext = viewModel.RootItems;
        Icons.GroupOpening += (_, group) => OpenGroup(group);
        viewModel.ItemActivated += OnItemActivated;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Deactivated += (_, _) => Dismiss();
    }

    /// <summary>The group shown in place of the bar's top level, if any.</summary>
    internal ItemViewModel? OpenedGroup { get; private set; }

    /// <summary>
    /// The window's size in WPF units for what it shows now, measured from its own markup rather than counted: the
    /// entries as the grid lays them out (<see cref="FlowLayout"/>, so lines, gaps and captions take the room they take),
    /// as wide as they are side by side up to <see cref="MaxColumns"/> cells (at least <see cref="MinColumns"/>, so the
    /// search box has room) and as tall as their rows up to <see cref="MaxRows"/> (more scroll, and the scroll bar's
    /// width is added so it takes no column), plus everything around them: the border, the search box and, in a group,
    /// its header.
    /// </summary>
    internal Size MeasureSize()
    {
        var cellWidth = _viewModel.CellWidth;
        var cellHeight = _viewModel.CellHeight;
        var aroundWidth = Chrome.BorderThickness.Left + Chrome.BorderThickness.Right + Chrome.Padding.Left + Chrome.Padding.Right
            + Icons.Margin.Left + Icons.Margin.Right + Icons.ItemsHost.Margin.Left + Icons.ItemsHost.Margin.Right;

        // The entries side by side, as the grid lays them out in one endless row.
        Chrome.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var oneRow = Icons.LayoutPanel?.DesiredSize.Width ?? 0;
        var panelWidth = Math.Clamp(oneRow, MinColumns * cellWidth, MaxColumns * cellWidth);

        // At that width: the rows, and the height of everything else (the search box, a group's header, margins).
        Chrome.Measure(new Size(aroundWidth + panelWidth, double.PositiveInfinity));
        var rows = Icons.LayoutPanel?.DesiredSize.Height ?? 0;
        var aroundHeight = Chrome.DesiredSize.Height - rows;
        var maxRows = MaxRows * cellHeight;
        var scrollBar = rows > maxRows + 0.5 ? SystemParameters.VerticalScrollBarWidth : 0;

        // Those were trial measures: the next layout pass measures the content again at the window's own size, even when
        // that size does not change (the scroll bar is decided there).
        InvalidateMeasure();
        return new Size(aroundWidth + panelWidth + scrollBar, aroundHeight + Math.Min(rows, maxRows) + RoundingSlack);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        NativeMethods.SetExStyle(handle, NativeMethods.GetExStyle(handle) | NativeMethods.WS_EX_TOOLWINDOW); // not in Alt+Tab
    }

    /// <summary>
    /// Opens under the mouse pointer on whichever monitor it is, sized in that monitor's pixels before it is shown,
    /// with the search box focused. The hotkey press gives the app the right to take the foreground. A window made in
    /// the same turn of the dispatcher is measured before its bindings are alive (WPF attaches a binding made without a
    /// data context later, at DataBind priority), so its grid looks empty then: the bar makes it ahead
    /// (<see cref="MainWindow"/>), and it is fitted once more after it is shown.
    /// </summary>
    public void ShowAtPointer()
    {
        _viewModel.ClearSearch();
        ShowRoot();
        new WindowInteropHelper(this).EnsureHandle();
        if (!NativeMethods.GetCursorPos(out var pointer))
        {
            pointer = default;
        }

        var area = ScreenGeometry.AreaAt(pointer.X, pointer.Y);
        var scale = area?.Scale ?? ScreenGeometry.PrimaryScale();
        var size = MeasureSize();
        var rect = PopupPlacement.AtPointer(
            pointer.X,
            pointer.Y,
            (int)Math.Ceiling(size.Width * scale),
            (int)Math.Ceiling(size.Height * scale),
            area?.WorkArea ?? default,
            grabOffset: (int)Math.Round(GrabOffset * scale));
        ScreenGeometry.MoveToPixels(this, rect);

        Show();
        if (!Activate())
        {
            AppLog.Default.Debug("App", "Show at pointer: Windows kept the focus elsewhere");
        }

        UpdateLayout();
        if (!SearchBox.Focus())
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => SearchBox.Focus()); // once the box is laid out
        }

        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, FitToContent); // the same size unless it was measured too early

        AppLog.Default.Debug("App", $"Icons shown at the pointer ({rect.Left},{rect.Top}, {rect.Width}×{rect.Height} px)");
    }

    /// <summary>Hides the window and ends its search; the next press of the hotkey shows it again.</summary>
    public void Dismiss()
    {
        if (_dismissing || !IsVisible)
        {
            return;
        }

        _dismissing = true;
        try
        {
            Hide();
            _viewModel.ClearSearch();
            _viewModel.EndLaunchFeedback(); // launched from here: the bar has nothing to show for it
            ShowRoot();
        }
        finally
        {
            _dismissing = false;
        }
    }

    /// <summary>Closes the window for real, when the app quits. Anything else that asks it to close only hides it.</summary>
    public void CloseForGood()
    {
        _closingForGood = true;
        Close();
    }

    /// <summary>
    /// Alt+F4 hides the window like Esc does: closed for real, it could not be shown again, and the next press of the
    /// hotkey would fail. The app's own shutdown closes it all the same (WPF ignores the cancel then).
    /// </summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (!_closingForGood && !e.Cancel)
        {
            e.Cancel = true;
            Dismiss();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.ItemActivated -= OnItemActivated;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        base.OnClosed(e);
    }

    /// <summary>
    /// Esc clears the text, then leaves an open group, then closes. Backspace in an empty box leaves the group.
    /// In the box the arrows move the frame and Enter opens the framed match (Down with no text steps into the
    /// icons); typing while an icon has the focus goes back to the box.
    /// </summary>
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Handled)
        {
            return;
        }

        var inBox = SearchBox.IsKeyboardFocusWithin;
        switch (e.Key)
        {
            case Key.Escape:
                e.Handled = true;
                if (_viewModel.IsSearching)
                {
                    _viewModel.ClearSearch();
                }
                else if (OpenedGroup is not null)
                {
                    ShowRoot();
                    SearchBox.Focus();
                }
                else
                {
                    Dismiss();
                }

                return;
            case Key.Back when OpenedGroup is not null && !_viewModel.IsSearching && (!inBox || SearchBox.Text.Length == 0):
                e.Handled = true;
                ShowRoot();
                SearchBox.Focus();
                return;
        }

        if (inBox)
        {
            if (e.Key == Key.Down && !_viewModel.IsSearching)
            {
                e.Handled = SearchBox.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
                return;
            }

            e.Handled = TypeToFind.HandleSearchBoxKey(_viewModel, e.Key);
            return;
        }

        if (TypeToFind.IsTextKey(e.Key, Keyboard.Modifiers))
        {
            SearchBox.Focus(); // the key is left alone, so its character follows the focus into the box
        }
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        ShowRoot();
        SearchBox.Focus();
    }

    /// <summary>Shows a group's icons in place of the top level, under a header with a way back, and fits the window to them.</summary>
    internal void OpenGroup(ItemViewModel group)
    {
        if (group.Children is not { } children)
        {
            return;
        }

        OpenedGroup = group;
        Icons.DataContext = children;
        GroupTitle.Text = group.Name;
        GroupHeader.Visibility = Visibility.Visible;
        FitToContent();
        SearchBox.Focus();
    }

    private void ShowRoot()
    {
        OpenedGroup = null;
        Icons.DataContext = _viewModel.RootItems;
        GroupHeader.Visibility = Visibility.Collapsed;
        FitToContent();
    }

    /// <summary>
    /// Resizes the shown window to what it shows now (a group opened, or back to the top level), keeping its top edge and
    /// its centre where they are, as far as the work area allows.
    /// </summary>
    private void FitToContent()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (!IsVisible || handle == IntPtr.Zero || !NativeMethods.GetWindowRect(handle, out var now))
        {
            return; // not shown: ShowAtPointer sizes it
        }

        var scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var size = MeasureSize();
        var centre = (now.Left + now.Right) / 2;
        var area = ScreenGeometry.AreaAt(centre, now.Top);
        var rect = PopupPlacement.AtPointer(
            centre,
            now.Top,
            (int)Math.Ceiling(size.Width * scale),
            (int)Math.Ceiling(size.Height * scale),
            area?.WorkArea ?? default);
        ScreenGeometry.MoveToPixels(this, rect);
    }

    /// <summary>Closes after a launch (or after a running program was brought forward).</summary>
    private void OnItemActivated(object? sender, ItemViewModel item) => Dismiss();

    /// <summary>A search covers every group, so typing goes back to the top level, where the groups holding matches light up.</summary>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LauncherViewModel.IsSearching) && _viewModel.IsSearching && OpenedGroup is not null)
        {
            ShowRoot();
        }
    }
}
