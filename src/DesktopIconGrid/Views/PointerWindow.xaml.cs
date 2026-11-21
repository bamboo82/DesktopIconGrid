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
/// The bar's icons in a window under the mouse pointer, opened by the Show-at-pointer hotkey with the search box
/// focused. It works whether the bar is shown, hidden in the tray or tucked away at a screen edge, and goes away after
/// a launch, on Esc, on Alt+F4 and when another window takes the focus. A group opens in place, with a way back.
/// The window is made once and only hidden after that; it closes for real when the app quits (<see cref="CloseForGood"/>).
/// <para>
/// Since 0.15.0 (WP3, the owner's request) it is the bar's twin: its icon area has the size of the bar's, so the icons
/// fall into the same rows and columns and scroll where the bar's do, with the search box above them; and its background
/// is the bar's, drawn the same way (<see cref="BarSurface"/>), backdrop, transparency and the see-through look included.
/// What it leaves out: the grip, ✕ and ▸, resizing and docking. Before 0.15.0 it was a small opaque window of three to
/// six columns and at most five rows.
/// </para>
/// </summary>
public partial class PointerWindow : Window
{
    /// <summary>How far above the pointer the window's top edge goes, so the pointer rests on the search box.</summary>
    private const double GrabOffset = 14;

    private readonly LauncherViewModel _viewModel;
    private readonly BarSurface _surface;
    private bool _dismissing;
    private bool _closingForGood;

    public PointerWindow(LauncherViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        _surface = new BarSurface(this, resizeBorder: 0, "Window at the pointer");
        BarIconArea = () => new Size(6 * viewModel.CellWidth, 2 * viewModel.CellHeight);
        UiFonts.ApplyBar(this); // the bar's font, Chinese in Taipei Sans TC Light where this PC has it (0.12.0 WP5, 0.14.0 WP2)
        Icons.DataContext = viewModel.RootItems;
        Icons.GroupOpening += (_, group) => OpenGroup(group);
        viewModel.ItemActivated += OnItemActivated;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Deactivated += (_, _) => Dismiss();
    }

    /// <summary>The group shown in place of the bar's top level, if any.</summary>
    internal ItemViewModel? OpenedGroup { get; private set; }

    /// <summary>
    /// The bar's icon area in WPF units, as the bar has it when shown and expanded (0.15.0 WP3; the bar window gives it,
    /// <see cref="MainWindow.BarIconArea"/>). Read at each opening, so a bar resized meanwhile shows at the next one.
    /// </summary>
    internal Func<Size> BarIconArea { get; set; }

    /// <summary>The window's background, as the bar's (0.15.0 WP3; tests stand in for High Contrast through it).</summary>
    internal BarSurface Surface => _surface;

    /// <summary>
    /// A menu is open (it holds the mouse while it is): the key held to show this window hides it only when the menu
    /// has closed (0.15.0 WP2).
    /// </summary>
    internal bool IsMenuOpen => IsVisible && Mouse.Captured is System.Windows.Controls.ContextMenu or System.Windows.Controls.MenuItem;

    /// <summary>
    /// The window's size in WPF units, its icon area set within it: the bar's icon area (<see cref="BarIconArea"/>), the
    /// search box above it and, in a group, the group's header under the box, inside the border. Held to
    /// <paramref name="workArea"/> (the pointer's monitor's work area, in WPF units at its scale): an icon area that would
    /// not fit gives up what does not, and the icons flow again in it as in a narrower bar (<see cref="FitIconArea"/>).
    /// </summary>
    internal Size MeasureSize(Size workArea)
    {
        var bar = BarIconArea();
        SetIconArea(bar);

        // WPF skips a measure at the size an element was last measured at unless the element itself was invalidated. A
        // change below the frame (a group's header shown or hidden, the icon area set) marks only the element it is on
        // until a layout pass, and once the window has its content's size the frame's last measure was this same trial.
        // So the layout is brought up to date first, and the frame is measured anew (UpdateLayout does nothing while a
        // layout pass is running).
        UpdateLayout();
        Chrome.InvalidateMeasure();
        Chrome.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var around = new Size(Math.Max(0, Chrome.DesiredSize.Width - bar.Width), Math.Max(0, Chrome.DesiredSize.Height - bar.Height));
        var fitted = FitIconArea(bar, around, workArea);
        if (fitted != bar)
        {
            SetIconArea(fitted);
        }

        // A trial measure: the next layout pass measures the content again at the window's own size.
        InvalidateMeasure();
        return new Size(around.Width + fitted.Width, around.Height + fitted.Height);
    }

    /// <summary>
    /// The icon area that fits: the bar's, less what would go past <paramref name="workArea"/> once
    /// <paramref name="around"/> (the border, the search box, a group's header) is added. An empty work area (none known)
    /// holds nothing back.
    /// </summary>
    internal static Size FitIconArea(Size bar, Size around, Size workArea) => workArea.Width <= 0 || workArea.Height <= 0
        ? bar
        : new Size(
            Math.Max(0, Math.Min(bar.Width, workArea.Width - around.Width)),
            Math.Max(0, Math.Min(bar.Height, workArea.Height - around.Height)));

    private void SetIconArea(Size area)
    {
        Icons.Width = area.Width;
        Icons.Height = area.Height;
    }

    /// <summary>The background, brought to the bar's settings now (0.15.0 WP3): the window has a handle by then.</summary>
    private void ApplyLook()
    {
        if (HwndSource.FromHwnd(new WindowInteropHelper(this).Handle) is { } source)
        {
            _surface.Apply(source, _viewModel.Model.Settings.BarBackdrop, ThemeManager.EffectivePalette(_viewModel.Palette), _viewModel.Opacity, _viewModel.IconSize);
        }
    }

    /// <summary>The work area of <paramref name="area"/> in WPF units at its scale; empty when none is known.</summary>
    private static Size WorkAreaUnits(ScreenArea? area) => area is { } known && known.Scale > 0
        ? new Size(known.WorkArea.Width / known.Scale, known.WorkArea.Height / known.Scale)
        : Size.Empty;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        NativeMethods.SetExStyle(handle, NativeMethods.GetExStyle(handle) | NativeMethods.WS_EX_TOOLWINDOW); // not in Alt+Tab
    }

    /// <summary>
    /// Opens under the mouse pointer on whichever monitor it is, sized in that monitor's pixels before it is shown,
    /// with the search box focused and the bar's background as the bar has it now. The hotkey press gives the app the
    /// right to take the foreground. A window made in the same turn of the dispatcher is measured before its bindings are
    /// alive (WPF attaches a binding made without a data context later, at DataBind priority): the bar makes it ahead
    /// (<see cref="MainWindow"/>), and it is fitted once more after it is shown.
    /// </summary>
    public void ShowAtPointer()
    {
        _viewModel.ClearSearch();
        ShowRoot();
        new WindowInteropHelper(this).EnsureHandle();
        ApplyLook();
        if (!NativeMethods.GetCursorPos(out var pointer))
        {
            pointer = default;
        }

        var area = ScreenGeometry.AreaAt(pointer.X, pointer.Y);
        var scale = area?.Scale ?? ScreenGeometry.PrimaryScale();
        var size = MeasureSize(WorkAreaUnits(area));
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

    /// <summary>Shows a group's icons in place of the top level, under a header with a way back; the window grows by the header, the icon area keeps its size.</summary>
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
    /// Resizes the shown window to what it shows now (a group's header, or back to the top level without it), keeping its
    /// top edge and its centre where they are, as far as the work area allows.
    /// </summary>
    private void FitToContent()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (!IsVisible || handle == IntPtr.Zero || !NativeMethods.GetWindowRect(handle, out var now))
        {
            return; // not shown: ShowAtPointer sizes it
        }

        var scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var centre = (now.Left + now.Right) / 2;
        var area = ScreenGeometry.AreaAt(centre, now.Top);
        var size = MeasureSize(WorkAreaUnits(area));
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
