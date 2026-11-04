using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using DesktopIconGrid.Core.Diagnostics;
using DesktopIconGrid.Services;

namespace DesktopIconGrid.Views;

/// <summary>
/// A window that is never shown but owns the tray icon's WPF context menu, so the menu takes the dialogs' look
/// (Fluent, light or dark) and the DPI of the screen it opens on, instead of the WinForms menu strip.
/// </summary>
public sealed class TrayMenuHost : Window
{
    private readonly ContextMenu _menu = new()
    {
        Placement = PlacementMode.AbsolutePoint,
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
        Opacity = 0;
        Left = -32000;
        Top = -32000;
        var anchor = new Border();
        Content = anchor;
        _menu.PlacementTarget = anchor;
        new WindowInteropHelper(this).EnsureHandle(); // a handle without showing: popups need a PresentationSource
        DialogTheme.Apply(this);
    }

    /// <summary>Opens the menu at the mouse pointer.</summary>
    public void Open(IEnumerable<TrayMenuEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        DialogTheme.Apply(this); // follow the setting as it is now
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

        var cursor = CursorPosition();
        _menu.HorizontalOffset = cursor.X;
        _menu.VerticalOffset = cursor.Y;

        // Without the foreground, keys would not reach the menu and a click elsewhere would not close it.
        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero)
        {
            NativeMethods.SetForegroundWindow(handle);
        }

        try
        {
            _menu.IsOpen = true;
        }
        catch (InvalidOperationException ex)
        {
            AppLog.Default.Error("App", "The tray menu could not be opened", ex);
        }
    }

    /// <summary>The pointer in this window's units, so the menu lands under it on any monitor.</summary>
    private Point CursorPosition()
    {
        if (!NativeMethods.GetCursorPos(out var device))
        {
            return new Point(0, 0);
        }

        var point = new Point(device.X, device.Y);
        return PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice.Transform(point) ?? point;
    }

    private static class NativeMethods
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetCursorPos(out POINT point);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetForegroundWindow(IntPtr hWnd);
    }
}
