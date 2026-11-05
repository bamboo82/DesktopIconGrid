using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using DesktopIconGrid.Core.Diagnostics;
using DesktopIconGrid.Services;

namespace DesktopIconGrid.Views;

/// <summary>
/// An invisible window (no size, never activated, not in Alt+Tab) that owns the tray icon's WPF context menu, so
/// the menu takes the dialogs' look (Fluent, light or dark) instead of the WinForms menu strip.
/// </summary>
/// <remarks>
/// A WPF popup whose placement target has no presentation source waits for one and never appears, so the
/// target here is a laid-out element of a window that is really shown (at zero size). Before the menu opens the
/// window is moved under the pointer: the menu is then placed and measured in that monitor's DPI with a single
/// transform. The popup's own window is given the foreground afterwards so Esc and the arrow keys work and a
/// click elsewhere closes it.
/// </remarks>
public sealed class TrayMenuHost : Window
{
    private readonly Border _anchor = new();
    private readonly ContextMenu _menu = new()
    {
        Placement = PlacementMode.MousePoint,
        StaysOpen = false,
    };

    public TrayMenuHost()
    {
        Width = 0;
        Height = 0;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = 0;
        Top = 0;
        Content = _anchor;
        _anchor.ContextMenu = _menu; // the menu finds the window's (Fluent) resources through its owner
        _menu.PlacementTarget = _anchor;

        var handle = new WindowInteropHelper(this).EnsureHandle();
        NativeMethods.SetExStyle(handle, NativeMethods.GetExStyle(handle) | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE);
        DialogTheme.Apply(this);
    }

    /// <summary>Opens the menu at the mouse pointer.</summary>
    public void Open(IEnumerable<TrayMenuEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        DialogTheme.Apply(this); // follow the setting as it is now
        _menu.IsOpen = false;
        _menu.PlacementTarget = _anchor; // the fallback below detaches it; every opening tries the anchored way first
        _menu.Placement = PlacementMode.MousePoint;
        _menu.HorizontalOffset = 0;
        _menu.VerticalOffset = 0;
        _menu.Items.Clear();
        foreach (var entry in entries)
        {
            if (entry.IsSeparator)
            {
                _menu.Items.Add(new Separator());
                continue;
            }

            var item = new MenuItem
            {
                Header = entry.Text!.Replace("_", "__", StringComparison.Ordinal),
                IsChecked = entry.IsChecked,
                IsEnabled = entry.IsEnabled,
            };
            var execute = entry.Execute;
            if (execute is not null)
            {
                item.Click += (_, _) => execute();
            }

            _menu.Items.Add(item);
        }

        if (!IsVisible)
        {
            Show(); // zero size, not activated: nothing to see, but a loaded placement target for the popup
        }

        var handle = new WindowInteropHelper(this).Handle;
        if (!NativeMethods.GetCursorPos(out var cursor))
        {
            cursor = default;
        }

        NativeMethods.SetWindowPos(handle, IntPtr.Zero, cursor.X, cursor.Y, 0, 0, NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);

        HwndSource? popup;
        try
        {
            _menu.IsOpen = true;
            popup = PresentationSource.FromVisual(_menu) as HwndSource;
            if (popup is null)
            {
                // No popup window was created (WPF is waiting for the target): open without a target, at the
                // pointer, in this monitor's DPI.
                _menu.IsOpen = false;
                _menu.PlacementTarget = null;
                _menu.Placement = PlacementMode.AbsolutePoint;
                var scale = VisualTreeHelper.GetDpi(this);
                _menu.HorizontalOffset = cursor.X / scale.DpiScaleX;
                _menu.VerticalOffset = cursor.Y / scale.DpiScaleY;
                _menu.IsOpen = true;
                popup = PresentationSource.FromVisual(_menu) as HwndSource;
                AppLog.Default.Warning("App", popup is null
                    ? "The tray menu did not open (no popup window)"
                    : "The tray menu opened without a placement target");
            }
        }
        catch (InvalidOperationException ex)
        {
            AppLog.Default.Error("App", "The tray menu could not be opened", ex);
            return;
        }

        // The host never takes the foreground; the popup's own window can. Without it, keys would not reach the
        // menu and a click elsewhere would not close it.
        if (popup is not null && popup.Handle != IntPtr.Zero)
        {
            NativeMethods.SetForegroundWindow(popup.Handle);
        }
    }
}
