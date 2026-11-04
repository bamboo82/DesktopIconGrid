using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Shell;
using System.Windows.Threading;
using DesktopIconGrid.Core.Diagnostics;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Services;
using DesktopIconGrid.ViewModels;
using Microsoft.Win32;

namespace DesktopIconGrid.Views;

/// <summary>The launcher bar: a borderless window with a move grip, the icon grid, close/options buttons and the group popup.</summary>
public partial class MainWindow : Window
{
    private readonly LauncherViewModel _viewModel;
    private GlobalHotkey? _hotkey;
    private string? _registeredHotkey;
    private TrayIcon? _tray;
    private TrayMenuHost? _trayMenu;
    private bool _exiting;
    private bool _closing;
    private bool _backdropActive;

    public MainWindow(LauncherViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();

        viewModel.GroupActivated += OnGroupActivated;
        viewModel.ExitRequested += (_, _) => ExitApplication();
        viewModel.SettingsChanged += (_, _) =>
        {
            EnsureMinimumSize();
            ApplyHotkey(reportErrors: true);
            ApplyAppearance();
        };
        Deactivated += OnDeactivated;
        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
        MainGrid.MouseLeftButtonDown += GridBackground_MouseLeftButtonDown;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ApplyPlacement();

        HideFromAltTab();
        if (HwndSource.FromHwnd(new WindowInteropHelper(this).Handle) is { } source)
        {
            _hotkey = new GlobalHotkey(source);
            _hotkey.Pressed += (_, _) => ToggleVisibility();
            ApplyHotkey(reportErrors: true);
        }

        ApplyAppearance();
        CreateTrayIcon();
    }

    private void CreateTrayIcon()
    {
        if (TryFindResource("AppIconImage") is not ImageSource image)
        {
            return;
        }

        try
        {
            _tray = new TrayIcon(image, "Desktop Icon Grid");
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return;
        }

        _tray.Clicked += (_, _) => ToggleVisibility();
        _tray.MenuRequested += (_, _) => (_trayMenu ??= new TrayMenuHost()).Open(BuildTrayMenu());
        _tray.BalloonClicked += (_, _) => _viewModel.OpenLogViewerCommand.Execute(null);
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        AppLog.Default.Debug("App", "Tray icon created");
        TrayReady?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Raised once the tray icon exists, so notices can go there instead of message boxes.</summary>
    public event EventHandler? TrayReady;

    /// <summary>Shows a balloon from the tray icon; false when there is none.</summary>
    public bool TryNotify(string title, string message)
    {
        if (_tray is null)
        {
            return false;
        }

        _tray.Notify(title, message);
        return true;
    }

    /// <summary>The display settings changed (scale, monitors): the tray icon is re-rendered for the system DPI.</summary>
    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(() => _tray?.Refresh());

    private IEnumerable<TrayMenuEntry> BuildTrayMenu()
    {
        var shown = IsVisible && WindowState != WindowState.Minimized;
        yield return new TrayMenuEntry(shown ? "Hide bar" : "Show bar", ToggleVisibility);
        yield return new TrayMenuEntry("Reset bar position", ResetPosition);
        yield return TrayMenuEntry.Separator;
        yield return new TrayMenuEntry("New shortcut…", () => RunOnBar(() => _viewModel.NewShortcutCommand.Execute(null)));
        yield return new TrayMenuEntry("New group…", () => RunOnBar(() => _viewModel.NewGroupCommand.Execute(null)));
        yield return new TrayMenuEntry("Edit names…", () => RunOnBar(() => _viewModel.OpenNameTableCommand.Execute(null)));
        yield return new TrayMenuEntry("Launch Multi Launch group", () => _viewModel.LaunchMultiLaunchCommand.Execute(null), IsEnabled: _viewModel.HasMultiLaunchMembers);
        yield return new TrayMenuEntry("Always on top", () => _viewModel.AlwaysOnTop = !_viewModel.AlwaysOnTop, IsChecked: _viewModel.AlwaysOnTop);
        yield return TrayMenuEntry.Separator;
        yield return new TrayMenuEntry("Settings…", () => RunOnBar(() => _viewModel.OpenSettingsCommand.Execute(null)));
        yield return new TrayMenuEntry("Sync with Google Drive…", () => RunOnBar(() => _viewModel.OpenSyncCommand.Execute(null)));
        yield return new TrayMenuEntry("Log viewer…", () => _viewModel.OpenLogViewerCommand.Execute(null));
        yield return new TrayMenuEntry("Open layout file location", () => _viewModel.OpenLayoutFolderCommand.Execute(null));
        yield return new TrayMenuEntry("About…", () => _viewModel.AboutCommand.Execute(null));
        yield return TrayMenuEntry.Separator;
        yield return new TrayMenuEntry("Exit", ExitApplication);
    }

    /// <summary>Brings the bar to the front before running an action that opens a dialog owned by it.</summary>
    private void RunOnBar(Action action)
    {
        if (!IsVisible)
        {
            ToggleVisibility();
        }

        Dispatcher.BeginInvoke(action);
    }

    /// <summary>Moves the bar to the middle of the primary screen's work area (for when it got lost on a disconnected monitor).</summary>
    public void ResetPosition()
    {
        AppLog.Default.Info("App", "Bar position reset to the primary screen");
        Show();
        WindowState = WindowState.Normal;
        var area = SystemParameters.WorkArea;
        Width = Math.Min(Width, area.Width);
        Height = Math.Min(Height, area.Height);
        Left = area.Left + Math.Max(0, (area.Width - Width) / 2);
        Top = area.Top + Math.Max(0, (area.Height - Height) / 2);
        Activate();
    }

    public void ExitApplication()
    {
        if (_closing)
        {
            return; // already on the way out (a pending upload is being flushed)
        }

        _exiting = true;
        Close();
    }

    /// <summary>
    /// Theme brushes, the Windows 11 backdrop and the opacity, in that order: the backdrop decides whether the bar
    /// background is a tint and whether the layered alpha applies. Runs once the window has a handle, and again on
    /// every settings change and when Windows High Contrast is switched.
    /// </summary>
    private void ApplyAppearance()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return; // before SourceInitialized; it calls again
        }

        var settings = _viewModel.Model.Settings;
        var palette = ThemeManager.EffectivePalette(_viewModel.Palette);
        var wanted = SystemParameters.HighContrast ? LauncherSettings.BackdropSolid : settings.BarBackdrop; // High Contrast asks for flat colours
        if (!string.Equals(wanted, LauncherSettings.BackdropSolid, StringComparison.Ordinal) && WindowBackdrop.IsSupported)
        {
            WindowAlpha.Apply(handle, LauncherSettings.MaxOpacity); // a layered window cannot take a backdrop: drop the style first
        }

        var active = WindowBackdrop.Apply(handle, wanted, darkMode: ThemeCatalog.Luminance(palette.Background) <= 0.6);
        if (active != _backdropActive)
        {
            _backdropActive = active;

            // For DWM to paint behind the client area WPF must not: extend the frame into it and leave the window transparent.
            WindowChrome.SetWindowChrome(this, new WindowChrome
            {
                CaptionHeight = 0,
                ResizeBorderThickness = new Thickness(6),
                GlassFrameThickness = active ? new Thickness(-1) : new Thickness(0),
                CornerRadius = new CornerRadius(0),
                UseAeroCaptionButtons = false,
            });
            if (active)
            {
                Background = Brushes.Transparent;
                if (HwndSource.FromHwnd(handle) is { CompositionTarget: { } target })
                {
                    target.BackgroundColor = Colors.Transparent;
                }
            }
            else
            {
                ClearValue(BackgroundProperty);
            }

            AppLog.Default.Info("App", active ? $"Bar background: {wanted} backdrop" : "Bar background: solid colour");
        }

        ThemeManager.Apply(palette, translucentBar: active, tintOpacity: _viewModel.Opacity / 100.0);
        DialogTheme.Configure(settings.FluentDialogs);
        ApplyOpacity();
    }

    private void OnSystemParametersChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null or nameof(SystemParameters.HighContrast))
        {
            Dispatcher.BeginInvoke(ApplyAppearance);
        }
    }

    /// <summary>With a backdrop the tint already carries the transparency; the layered alpha would fade the icons as well.</summary>
    private int EffectiveOpacity => _backdropActive ? LauncherSettings.MaxOpacity : _viewModel.Opacity;

    private void ApplyOpacity()
    {
        WindowAlpha.Apply(new WindowInteropHelper(this).Handle, EffectiveOpacity);
        if (GroupPopup.IsOpen && GroupPopup.Child is { } child && PresentationSource.FromVisual(child) is HwndSource popupSource)
        {
            WindowAlpha.Apply(popupSource.Handle, EffectiveOpacity);
        }
    }

    /// <summary>The popup has its own window: give it an Acrylic backdrop when the bar has one, else the solid colour.</summary>
    private void GroupPopup_Opened(object? sender, EventArgs e)
    {
        if (GroupPopup.Child is not { } child || PresentationSource.FromVisual(child) is not HwndSource popupSource)
        {
            return;
        }

        var palette = ThemeManager.EffectivePalette(_viewModel.Palette);
        var translucent = false;
        if (_backdropActive)
        {
            translucent = WindowBackdrop.ApplyToPopup(popupSource.Handle, darkMode: ThemeCatalog.Luminance(palette.Background) <= 0.6);
            if (translucent && popupSource.CompositionTarget is { } target)
            {
                target.BackgroundColor = Colors.Transparent;
            }
        }

        ThemeManager.SetPopupBackground(palette, translucent, _viewModel.Opacity / 100.0);
        WindowAlpha.Apply(popupSource.Handle, EffectiveOpacity);
    }

    protected override void OnClosed(EventArgs e)
    {
        SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _hotkey?.Dispose();
        _hotkey = null;
        _tray?.Dispose();
        _tray = null;
        _trayMenu?.Close();
        _trayMenu = null;
        base.OnClosed(e);
    }

    /// <summary>A utility bar with a tray icon should not also sit in Alt+Tab.</summary>
    private void HideFromAltTab()
    {
        const int GwlExStyle = -20;
        const long WsExToolWindow = 0x00000080;
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var style = NativeMethods.GetWindowLongPtr(handle, GwlExStyle).ToInt64();
        NativeMethods.SetWindowLongPtr(handle, GwlExStyle, new IntPtr(style | WsExToolWindow));
    }

    /// <summary>Called when a second copy of the app was started: make the bar visible and bring it forward.</summary>
    public void ShowFromOtherInstance()
    {
        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        // A request from another process carries no foreground rights of its own: the second copy called
        // AllowSetForegroundWindow for us, and flipping Topmost is the fallback when Windows still refuses
        // (there is no taskbar button to flash for a tool window).
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero || !NativeMethods.SetForegroundWindow(handle))
        {
            var wasTopmost = Topmost;
            Topmost = true;
            Topmost = wasTopmost;
        }

        Activate();
    }

    /// <summary>Hides the bar, or brings it back to the front. Bound to the global hotkey.</summary>
    public void ToggleVisibility()
    {
        if (IsVisible && WindowState != WindowState.Minimized)
        {
            _viewModel.IsGroupPopupOpen = false;
            Hide();
            AppLog.Default.Debug("App", "Bar hidden");
            return;
        }

        Show();
        AppLog.Default.Debug("App", "Bar shown");
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
            AppLog.Default.Warning("Hotkey", error ?? "Hotkey could not be registered");
            if (reportErrors && error is not null)
            {
                MessageBox.Show(this, error, "Show/hide hotkey", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            return;
        }

        _registeredHotkey = text;
        AppLog.Default.Info("Hotkey", gesture is null ? "Show/hide hotkey cleared" : $"Show/hide hotkey registered: {gesture}");
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel)
        {
            return;
        }

        if (_closing)
        {
            e.Cancel = true; // a second Exit while the pending upload is being flushed below
            return;
        }

        if (!_exiting && _viewModel.CloseToTray && _tray is not null)
        {
            e.Cancel = true;
            _viewModel.IsGroupPopupOpen = false;
            SavePlacement();
            Hide();
            return;
        }

        _closing = true;
        IsEnabled = false; // the message loop keeps running in WaitForPendingWork; no more clicks on the bar
        SavePlacement();
        WaitForPendingWork();
    }

    /// <summary>Lets a pending sync upload finish (bounded) while still pumping messages, so "tidy up, then quit" reaches Drive.</summary>
    private void WaitForPendingWork()
    {
        if (_viewModel.FlushBeforeExit is not { } flush)
        {
            return;
        }

        Task task;
        try
        {
            task = flush();
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            return;
        }

        if (task.IsCompleted)
        {
            return;
        }

        var frame = new DispatcherFrame();
        task.ContinueWith(_ => frame.Continue = false, TaskScheduler.Default);
        var guard = new DispatcherTimer(TimeSpan.FromSeconds(6), DispatcherPriority.Normal, (_, _) => frame.Continue = false, Dispatcher);
        Dispatcher.PushFrame(frame);
        guard.Stop();
    }

    private void SavePlacement()
    {
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

    /// <summary>
    /// Checked against the real monitors, not the virtual screen's bounding box (an L-shaped layout has a dead
    /// corner), and at every plausible display scale, so a mixed-DPI layout never moves a visible bar by mistake.
    /// </summary>
    private static bool IntersectsAScreen(WindowPlacement placement) =>
        ScreenGeometry.IsVisibleAtAnyScale(
            new Rect(placement.Left, placement.Top, placement.Width, placement.Height),
            ScreenGeometry.MonitorsInPixels(),
            ScreenGeometry.PrimaryScale());

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

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.CloseToTray && _tray is not null)
        {
            Close(); // cancelled in OnClosing and turned into Hide()
        }
        else
        {
            ExitApplication();
        }
    }

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

    private static class NativeMethods
    {
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        public static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetForegroundWindow(IntPtr hWnd);
    }
}
