using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Services;
using DesktopIconGrid.ViewModels;

namespace DesktopIconGrid.Views;

/// <summary>The launcher bar: a borderless window with a move grip, the icon grid, close/options buttons and the group popup.</summary>
public partial class MainWindow : Window
{
    private readonly LauncherViewModel _viewModel;
    private GlobalHotkey? _hotkey;
    private string? _registeredHotkey;

    public MainWindow(LauncherViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();

        viewModel.GroupActivated += OnGroupActivated;
        viewModel.ExitRequested += (_, _) => Close();
        viewModel.SettingsChanged += (_, _) =>
        {
            EnsureMinimumSize();
            ApplyHotkey(reportErrors: true);
        };
        Deactivated += OnDeactivated;
        MainGrid.MouseLeftButtonDown += GridBackground_MouseLeftButtonDown;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ApplyPlacement();

        if (HwndSource.FromHwnd(new WindowInteropHelper(this).Handle) is { } source)
        {
            _hotkey = new GlobalHotkey(source);
            _hotkey.Pressed += (_, _) => ToggleVisibility();
            ApplyHotkey(reportErrors: true);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _hotkey?.Dispose();
        _hotkey = null;
        base.OnClosed(e);
    }

    /// <summary>Hides the bar, or brings it back to the front. Bound to the global hotkey.</summary>
    public void ToggleVisibility()
    {
        if (IsVisible && WindowState != WindowState.Minimized)
        {
            _viewModel.IsGroupPopupOpen = false;
            Hide();
            return;
        }

        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
    }

    private void ApplyHotkey(bool reportErrors)
    {
        if (_hotkey is null)
        {
            return;
        }

        var text = _viewModel.Model.Settings.ToggleHotkey;
        if (string.Equals(text, _registeredHotkey, StringComparison.Ordinal))
        {
            return;
        }

        HotkeyGesture? gesture = null;
        if (!string.IsNullOrWhiteSpace(text) && !HotkeyGesture.TryParse(text, out gesture))
        {
            gesture = null;
        }

        if (!_hotkey.Register(gesture, out var error))
        {
            _registeredHotkey = null;
            if (reportErrors && error is not null)
            {
                MessageBox.Show(this, error, "Show/hide hotkey", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            return;
        }

        _registeredHotkey = text;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel)
        {
            return;
        }

        var bounds = WindowState == WindowState.Normal
            ? new Rect(Left, Top, ActualWidth, ActualHeight)
            : RestoreBounds;
        if (bounds.Width > 0 && bounds.Height > 0)
        {
            _viewModel.UpdateWindowPlacement(new WindowPlacement
            {
                Left = bounds.Left,
                Top = bounds.Top,
                Width = bounds.Width,
                Height = bounds.Height,
            });
        }
    }

    protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseDown(e);
        if (!_viewModel.IsGroupPopupOpen || e.OriginalSource is not DependencyObject source)
        {
            return;
        }

        if (IsInsidePopup(source))
        {
            return;
        }

        // Clicking the open group's own icon toggles it through Activate; everything else closes the popup.
        if (FindAncestor<Button>(source)?.DataContext is ItemViewModel { IsGroup: true } clicked
            && ReferenceEquals(clicked, _viewModel.OpenGroup))
        {
            return;
        }

        _viewModel.IsGroupPopupOpen = false;
    }

    private void ApplyPlacement()
    {
        var placement = _viewModel.WindowPlacement;
        if (placement is { IsUsable: true } && IntersectsAScreen(placement))
        {
            Left = placement.Left;
            Top = placement.Top;
            Width = placement.Width;
            Height = placement.Height;
            return;
        }

        // First run: room for six icons in two rows, centred in the work area.
        Width = Math.Max(MinWidth, 36 + 6 * _viewModel.CellWidth);
        Height = Math.Max(MinHeight, 8 + 2 * _viewModel.CellHeight);
        var area = SystemParameters.WorkArea;
        Left = area.Left + Math.Max(0, (area.Width - Width) / 2);
        Top = area.Top + Math.Max(0, (area.Height - Height) / 2);
    }

    private static bool IntersectsAScreen(WindowPlacement placement)
    {
        var virtualScreen = new Rect(
            SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth,
            SystemParameters.VirtualScreenHeight);
        var window = new Rect(placement.Left, placement.Top, placement.Width, placement.Height);
        window.Intersect(virtualScreen);
        return !window.IsEmpty && window.Width >= 40 && window.Height >= 24;
    }

    private void EnsureMinimumSize()
    {
        var neededWidth = 36 + _viewModel.CellWidth;
        var neededHeight = 8 + _viewModel.CellHeight;
        if (ActualWidth < neededWidth)
        {
            Width = neededWidth;
        }

        if (ActualHeight < neededHeight)
        {
            Height = neededHeight;
        }
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        if (!DragState.IsDragging)
        {
            _viewModel.IsGroupPopupOpen = false;
        }
    }

    private void OnGroupActivated(object? sender, GroupActivatedEventArgs e)
    {
        GroupPopup.PlacementTarget = e.Anchor as UIElement ?? MainGrid;
        GroupPopup.Placement = PlacementMode.Bottom;
    }

    private void Grip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            _viewModel.AlwaysOnTop = !_viewModel.AlwaysOnTop;
            e.Handled = true;
            return;
        }

        _viewModel.IsGroupPopupOpen = false;
        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // The button was released before the move could start; nothing to do.
        }
    }

    /// <summary>Pressing on empty grid background moves the bar, like the grip does.</summary>
    private void GridBackground_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source
            || FindAncestor<ButtonBase>(source) is not null
            || FindAncestor<ScrollBar>(source) is not null)
        {
            return;
        }

        _viewModel.IsGroupPopupOpen = false;
        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // Button released before the move started.
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Options_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { ContextMenu: { } menu } button)
        {
            return;
        }

        menu.PlacementTarget = button;
        menu.Placement = PlacementMode.Top;
        menu.IsOpen = true;
    }

    private bool IsInsidePopup(DependencyObject source)
    {
        var child = GroupPopup.Child;
        return child is not null && (ReferenceEquals(child, source) || IsAncestor(child, source));
    }

    private static DependencyObject? ParentOf(DependencyObject element) =>
        element is Visual or Visual3D ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);

    private static bool IsAncestor(DependencyObject ancestor, DependencyObject element)
    {
        for (var current = ParentOf(element); current is not null; current = ParentOf(current))
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }
        }

        return false;
    }

    private static T? FindAncestor<T>(DependencyObject element)
        where T : DependencyObject
    {
        for (var current = element; current is not null; current = ParentOf(current))
        {
            if (current is T match)
            {
                return match;
            }
        }

        return null;
    }
}
