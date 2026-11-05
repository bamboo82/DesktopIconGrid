using System.Windows;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using DesktopIconGrid.Core.Diagnostics;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Services;
using DesktopIconGrid.ViewModels;

namespace DesktopIconGrid.Views;

/// <summary>
/// Docking the bar to a screen edge as an application desktop toolbar (maximised windows stay clear of it), with
/// optional auto-hide: the bar collapses to a two-pixel strip a second after the pointer leaves and comes back
/// when the pointer touches the edge, the hotkey shows it or a group popup is open. All geometry is in device
/// pixels through <see cref="AppBar"/>; WPF picks the new Left/Top/Width/Height up from the window messages.
/// While the bar is in the tray (hidden, closed to the tray, or started hidden) the shell holds nothing for it:
/// the strip and the auto-hide slot are given back and claimed again when the bar is shown. A full-screen program
/// hides the bar without that, since nothing else can use the edge meanwhile.
/// </summary>
public partial class MainWindow
{
    private static readonly TimeSpan AutoHideDelay = TimeSpan.FromSeconds(1);

    private AppBar? _appBar;
    private string _dockedEdge = LauncherSettings.DockNone;
    private string? _autoHideEdge;
    private bool _collapsed;
    private bool _movingForDock;
    private bool _hiddenForFullScreen;
    private bool _dockSuspended;
    private int _dockThickness;
    private WindowPlacement? _undockedPlacement;
    private DispatcherTimer? _autoHideTimer;
    private double _baseMinWidth;
    private double _baseMinHeight;

    /// <summary>True while the bar is a desktop toolbar at a screen edge.</summary>
    public bool IsDocked => _dockedEdge != LauncherSettings.DockNone;

    private bool IsAutoHide => _autoHideEdge is not null;

    private void InitializeDocking()
    {
        _baseMinWidth = MinWidth;
        _baseMinHeight = MinHeight;
        SizeChanged += (_, _) => OnDockedSizeChanged();
        IsVisibleChanged += (_, _) => OnBarVisibilityChanged();
        MouseEnter += (_, _) =>
        {
            if (IsAutoHide && _collapsed)
            {
                Expand();
            }
        };
    }

    /// <summary>Reads the dock settings and docks, re-docks or undocks. Runs once the window has a handle and on every settings change.</summary>
    private void ApplyDocking()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero || HwndSource.FromHwnd(handle) is not { } source)
        {
            return;
        }

        var settings = _viewModel.Model.Settings;
        var edge = settings.DockEdge;
        if (edge == LauncherSettings.DockNone)
        {
            Undock();
            return;
        }

        // Visibility is already Visible while Show() creates the window (SourceInitialized), so a bar that is being
        // shown docks before it appears; one that stays in the tray reserves nothing until it is shown.
        if (Visibility != Visibility.Visible && !_hiddenForFullScreen)
        {
            SuspendDocking();
            return;
        }

        _dockSuspended = false;
        if (_appBar is null)
        {
            _appBar = new AppBar(source);
            _appBar.PositionChanged += (_, _) => Dispatcher.BeginInvoke(ReDock);
            _appBar.FullScreenChanged += (_, fullScreen) => Dispatcher.BeginInvoke(() => OnFullScreenChanged(fullScreen));
        }

        if (!IsDocked)
        {
            _undockedPlacement = CurrentPlacement();
            _dockThickness = ThicknessFromWindow(edge);
            Grip.Visibility = Visibility.Collapsed;
            Topmost = true; // a desktop toolbar sits above other windows; the setting is restored on undock
            AppLog.Default.Info("App", $"Bar docked to the {edge} edge");
        }
        else if (edge != _dockedEdge && AppBar.IsHorizontal(edge) != AppBar.IsHorizontal(_dockedEdge))
        {
            _dockThickness = ThicknessFromWindow(edge);
        }

        _dockedEdge = edge;

        if (_autoHideEdge is not null && (!settings.AutoHide || _autoHideEdge != edge))
        {
            _appBar.SetAutoHide(_autoHideEdge, false);
            _autoHideEdge = null;
        }

        if (settings.AutoHide && _autoHideEdge is null)
        {
            _appBar.Release(); // an auto-hide bar reserves no space
            if (_appBar.SetAutoHide(edge, true))
            {
                _autoHideEdge = edge;
            }
        }

        if (IsAutoHide)
        {
            ResizeMode = ResizeMode.NoResize;
            Expand();
            StartAutoHideTimer();
        }
        else
        {
            StopAutoHideTimer();
            ResizeMode = ResizeMode.CanResize;
            ReDock();
        }
    }

    /// <summary>Asks the shell for the strip again (after a settings change or when the shell rearranged the edges).</summary>
    private void ReDock()
    {
        if (!IsDocked || _appBar is null || _dockSuspended)
        {
            return; // suspended: a notification still queued from before the strip was given back must not claim it again
        }

        if (IsAutoHide)
        {
            if (_collapsed)
            {
                Collapse(force: true);
            }
            else
            {
                Expand();
            }

            return;
        }

        Move(_appBar.Dock(_dockedEdge, _dockThickness, _appBar.MonitorBounds()));
    }

    private void Undock()
    {
        _dockSuspended = false;
        if (!IsDocked)
        {
            return;
        }

        StopAutoHideTimer();
        if (_appBar is not null)
        {
            if (_autoHideEdge is not null)
            {
                _appBar.SetAutoHide(_autoHideEdge, false);
                _autoHideEdge = null;
            }

            _appBar.Release();
        }

        _dockedEdge = LauncherSettings.DockNone;
        _collapsed = false;
        MinWidth = _baseMinWidth;
        MinHeight = _baseMinHeight;
        ResizeMode = ResizeMode.CanResize;
        Grip.Visibility = Visibility.Visible;
        SetBinding(TopmostProperty, new Binding(nameof(LauncherViewModel.AlwaysOnTop)));
        if (_hiddenForFullScreen)
        {
            _hiddenForFullScreen = false;
            Show();
        }

        if (_undockedPlacement is { } free)
        {
            Left = free.Left;
            Top = free.Top;
            Width = free.Width;
            Height = free.Height;
            _undockedPlacement = null;
        }

        AppLog.Default.Info("App", "Bar undocked");
    }

    /// <summary>
    /// The bar went to the tray: give the reserved strip and the edge's auto-hide slot back, so maximised windows use
    /// the whole screen while nothing is shown at the edge. The dock state itself (edge, thickness, free placement)
    /// is kept, and <see cref="OnBarVisibilityChanged"/> docks again when the bar is shown.
    /// </summary>
    private void SuspendDocking()
    {
        _dockSuspended = true;
        if (_appBar is null || !_appBar.IsRegistered)
        {
            return;
        }

        StopAutoHideTimer();
        if (_autoHideEdge is not null)
        {
            _appBar.SetAutoHide(_autoHideEdge, false);
            _autoHideEdge = null;
        }

        _appBar.Release();
        AppLog.Default.Debug("App", "Docked bar hidden: its screen edge is free until the bar is shown");
    }

    private void OnBarVisibilityChanged()
    {
        if (IsVisible)
        {
            if (_dockSuspended)
            {
                ApplyDocking();
            }
        }
        else if (IsDocked && !_hiddenForFullScreen)
        {
            SuspendDocking();
        }
    }

    /// <summary>The bar's thickness along <paramref name="edge"/> from its current size, never below the window's minimum.</summary>
    private int ThicknessFromWindow(string edge)
    {
        var bounds = _appBar!.WindowBounds();
        var scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var minimum = (int)Math.Ceiling((AppBar.IsHorizontal(edge) ? _baseMinHeight : _baseMinWidth) * scale);
        var current = (int)(AppBar.IsHorizontal(edge) ? bounds.Height : bounds.Width);
        return Math.Max(current, minimum);
    }

    /// <summary>The user resized the docked bar at its inner edge: the new size is the new thickness.</summary>
    private void OnDockedSizeChanged()
    {
        if (!IsDocked || IsAutoHide || _movingForDock || _appBar is null)
        {
            return;
        }

        var thickness = ThicknessFromWindow(_dockedEdge);
        if (thickness != _dockThickness)
        {
            _dockThickness = thickness;
            ReDock();
        }
    }

    private void Expand()
    {
        if (_appBar is null)
        {
            return;
        }

        _collapsed = false;
        MinWidth = _baseMinWidth;
        MinHeight = _baseMinHeight;
        Move(AppBar.ProposedRect(_dockedEdge, _dockThickness, _appBar.MonitorBounds()));
    }

    private void Collapse(bool force = false)
    {
        if (_appBar is null)
        {
            return;
        }

        if (!force && (IsMouseOver || _viewModel.IsGroupPopupOpen || DragState.IsDragging || !IsVisible))
        {
            return;
        }

        _collapsed = true;
        MinWidth = 0;
        MinHeight = 0;
        Move(AppBar.Collapsed(_dockedEdge, _appBar.MonitorBounds()));
    }

    private void Move(Rect devicePixels)
    {
        _movingForDock = true;
        try
        {
            _appBar!.MoveWindow(devicePixels);
        }
        finally
        {
            // SizeChanged from this move arrives during the layout pass, before a Loaded-priority callback.
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => _movingForDock = false);
        }
    }

    private void StartAutoHideTimer()
    {
        _autoHideTimer ??= new DispatcherTimer(AutoHideDelay, DispatcherPriority.Normal, (_, _) =>
        {
            if (IsAutoHide && !_collapsed && !IsMouseOver)
            {
                Collapse();
            }
        }, Dispatcher);
        _autoHideTimer.Start();
    }

    private void StopAutoHideTimer() => _autoHideTimer?.Stop();

    /// <summary>A full-screen program (video, game) has the bar's monitor: get out of its way, and come back afterwards.</summary>
    private void OnFullScreenChanged(bool fullScreen)
    {
        if (!IsDocked)
        {
            return;
        }

        if (fullScreen)
        {
            if (IsVisible)
            {
                _hiddenForFullScreen = true;
                Hide();
            }
        }
        else if (_hiddenForFullScreen)
        {
            _hiddenForFullScreen = false;
            Show();
        }
    }

    /// <summary>While docked, the position to remember is the free one, so undocking (now or after a restart) returns there.</summary>
    private WindowPlacement? DockedPlacementToSave() => IsDocked ? _undockedPlacement : null;

    private void DisposeDocking()
    {
        StopAutoHideTimer();
        _appBar?.Dispose();
        _appBar = null;
    }
}
