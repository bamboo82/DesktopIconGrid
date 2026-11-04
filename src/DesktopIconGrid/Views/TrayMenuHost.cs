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
/// (Fluent, light or dark) instead of the WinForms menu strip. The menu opens at the mouse pointer in the
/// pointer's own DPI and is given the foreground once its popup exists, so Esc and arrow keys work and a click
/// elsewhere closes it.
/// </summary>
public sealed class TrayMenuHost : Window
{
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

        try
        {
            _menu.IsOpen = true;
        }
        catch (InvalidOperationException ex)
        {
            AppLog.Default.Error("App", "The tray menu could not be opened", ex);
            return;
        }

        // The host itself is invisible and cannot take the foreground; the popup's own window can. Without it,
        // keys would not reach the menu and a click elsewhere would not close it.
        if (PresentationSource.FromVisual(_menu) is HwndSource popup && popup.Handle != IntPtr.Zero)
        {
            NativeMethods.SetForegroundWindow(popup.Handle);
        }
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetForegroundWindow(IntPtr hWnd);
    }
}
