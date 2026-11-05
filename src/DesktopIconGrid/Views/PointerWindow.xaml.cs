using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using DesktopIconGrid.Core.Diagnostics;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Services;
using DesktopIconGrid.ViewModels;

namespace DesktopIconGrid.Views;

/// <summary>
/// The bar's icons in a small window under the mouse pointer, opened by the Show-at-pointer hotkey with the search box
/// focused. It works whether the bar is shown, hidden in the tray or tucked away at a screen edge, and goes away after
/// a launch, on Esc and when another window takes the focus. A group opens in place, with a way back.
/// </summary>
public partial class PointerWindow : Window
{
    /// <summary>Up to this many icons side by side, and at least this many so the search box has room.</summary>
    internal const int MaxColumns = 6;

    internal const int MinColumns = 3;

    /// <summary>More rows than this scroll.</summary>
    internal const int MaxRows = 5;

    /// <summary>Border and padding around the icons (see the markup), in WPF units.</summary>
    private const double ChromeWidth = 2 + 6 + 2;

    /// <summary>Border, padding, the search box and the gap below it, plus a little room so the rows never need a scroll bar.</summary>
    private const double ChromeHeight = 2 + 6 + 24 + 3 + 2 + 2;

    /// <summary>How far above the pointer the window's top edge goes, so the pointer rests on the search box.</summary>
    private const double GrabOffset = 14;

    private readonly LauncherViewModel _viewModel;
    private bool _dismissing;

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
    /// The window's size in WPF units for <paramref name="count"/> icons: up to <see cref="MaxColumns"/> columns and
    /// <see cref="MaxRows"/> rows, with room for the scroll bar when there are more.
    /// </summary>
    internal static Size SizeFor(int count, double cellWidth, double cellHeight, double scrollBarWidth)
    {
        var columns = Math.Clamp(count, MinColumns, MaxColumns);
        var rows = Math.Clamp((int)Math.Ceiling(count / (double)columns), 1, MaxRows);
        var scrollBar = count > columns * MaxRows ? scrollBarWidth : 0;
        return new Size(ChromeWidth + columns * cellWidth + scrollBar, ChromeHeight + rows * cellHeight);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        NativeMethods.SetExStyle(handle, NativeMethods.GetExStyle(handle) | NativeMethods.WS_EX_TOOLWINDOW); // not in Alt+Tab
    }

    /// <summary>
    /// Opens under the mouse pointer on whichever monitor it is, sized in that monitor's pixels before it is shown,
    /// with the search box focused. The hotkey press gives the app the right to take the foreground.
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
        var size = SizeFor(_viewModel.RootItems.Items.Count, _viewModel.CellWidth, _viewModel.CellHeight, SystemParameters.VerticalScrollBarWidth);
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
            ShowRoot();
        }
        finally
        {
            _dismissing = false;
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

    private void OpenGroup(ItemViewModel group)
    {
        if (group.Children is not { } children)
        {
            return;
        }

        OpenedGroup = group;
        Icons.DataContext = children;
        GroupTitle.Text = group.Name;
        GroupHeader.Visibility = Visibility.Visible;
        SearchBox.Focus();
    }

    private void ShowRoot()
    {
        OpenedGroup = null;
        Icons.DataContext = _viewModel.RootItems;
        GroupHeader.Visibility = Visibility.Collapsed;
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
