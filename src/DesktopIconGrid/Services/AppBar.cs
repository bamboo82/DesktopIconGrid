using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using DesktopIconGrid.Core.Diagnostics;
using DesktopIconGrid.Core.Models;

namespace DesktopIconGrid.Services;

/// <summary>
/// Registers the bar with the shell as an application desktop toolbar (what the taskbar is): a strip along one
/// screen edge that maximised windows stay clear of. Everything here is in device pixels; the owner moves the
/// window to the rectangles this class computes or the shell grants, and reacts to the shell's notifications.
/// </summary>
public sealed class AppBar : IDisposable
{
    /// <summary>Thickness of the strip an auto-hidden bar leaves at the edge for the pointer to touch.</summary>
    public const int CollapsedThickness = 2;

    private const uint AbmNew = 0;
    private const uint AbmRemove = 1;
    private const uint AbmQueryPos = 2;
    private const uint AbmSetPos = 3;
    private const uint AbmActivate = 6;
    private const uint AbmSetAutoHideBar = 8;
    private const uint AbmWindowPosChanged = 9;
    private const int AbnPosChanged = 1;
    private const int AbnFullScreenApp = 2;
    private const int WmActivate = 0x0006;
    private const int WmWindowPosChanged = 0x0047;

    private readonly HwndSource _source;
    private readonly uint _callbackMessage;
    private bool _registered;
    private bool _disposed;

    public AppBar(HwndSource source)
    {
        _source = source;
        _callbackMessage = NativeMethods.RegisterWindowMessage("DesktopIconGrid.AppBar");
        _source.AddHook(WndProc);
    }

    /// <summary>The shell moved something (another bar, the taskbar, the work area): dock again.</summary>
    public event EventHandler? PositionChanged;

    /// <summary>A full-screen application gained (true) or lost (false) the foreground on the bar's monitor.</summary>
    public event EventHandler<bool>? FullScreenChanged;

    public bool IsRegistered => _registered;

    /// <summary>Bounds of the monitor the window is on, in device pixels.</summary>
    public Rect MonitorBounds() =>
        ScreenGeometry.MonitorOfWindow(_source.Handle) is { } monitor
            ? new Rect(monitor.Left, monitor.Top, monitor.Width, monitor.Height)
            : ScreenGeometry.MonitorsInPixels()[0];

    /// <summary>The window's current bounds in device pixels.</summary>
    public Rect WindowBounds() =>
        NativeMethods.GetWindowRect(_source.Handle, out var rect) ? FromRect(rect) : Rect.Empty;

    /// <summary>Moves and sizes the window (device pixels) without activating it.</summary>
    public void MoveWindow(Rect bounds) =>
        NativeMethods.SetWindowPos(_source.Handle, IntPtr.Zero, (int)bounds.X, (int)bounds.Y, (int)bounds.Width, (int)bounds.Height, NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);

    /// <summary>Reserves a strip of <paramref name="thickness"/> pixels along <paramref name="edge"/> of <paramref name="monitor"/> and returns the rectangle the shell granted.</summary>
    public Rect Dock(string edge, int thickness, Rect monitor)
    {
        EnsureRegistered();
        var data = NewData();
        data.uEdge = EdgeCode(edge);
        data.rc = ToRect(ProposedRect(edge, thickness, monitor));
        NativeMethods.SHAppBarMessage(AbmQueryPos, ref data);
        data.rc = ToRect(KeepThickness(edge, thickness, FromRect(data.rc)));
        NativeMethods.SHAppBarMessage(AbmSetPos, ref data);
        return FromRect(data.rc);
    }

    /// <summary>Gives the reserved space back (and unregisters). Safe to call when not registered.</summary>
    public void Release()
    {
        if (!_registered)
        {
            return;
        }

        var data = NewData();
        NativeMethods.SHAppBarMessage(AbmRemove, ref data);
        _registered = false;
    }

    /// <summary>Claims (or gives up) the auto-hide slot of an edge. False when another bar already owns it.</summary>
    public bool SetAutoHide(string edge, bool on)
    {
        EnsureRegistered();
        var data = NewData();
        data.uEdge = EdgeCode(edge);
        data.lParam = on ? 1 : 0;
        var result = NativeMethods.SHAppBarMessage(AbmSetAutoHideBar, ref data);
        if (on && result == UIntPtr.Zero)
        {
            AppLog.Default.Warning("App", $"Another auto-hide bar already owns the {edge} edge; the bar stays docked without auto-hide");
            return false;
        }

        return true;
    }

    /// <summary>The strip of <paramref name="thickness"/> pixels along <paramref name="edge"/> of <paramref name="monitor"/>.</summary>
    internal static Rect ProposedRect(string edge, int thickness, Rect monitor) => edge switch
    {
        LauncherSettings.DockTop => new Rect(monitor.X, monitor.Y, monitor.Width, thickness),
        LauncherSettings.DockBottom => new Rect(monitor.X, monitor.Bottom - thickness, monitor.Width, thickness),
        LauncherSettings.DockLeft => new Rect(monitor.X, monitor.Y, thickness, monitor.Height),
        LauncherSettings.DockRight => new Rect(monitor.Right - thickness, monitor.Y, thickness, monitor.Height),
        _ => monitor,
    };

    /// <summary>After the shell adjusted a proposed rectangle (ABM_QUERYPOS), restore the bar's thickness on its inner side.</summary>
    internal static Rect KeepThickness(string edge, int thickness, Rect granted) => edge switch
    {
        LauncherSettings.DockTop => new Rect(granted.X, granted.Y, granted.Width, thickness),
        LauncherSettings.DockBottom => new Rect(granted.X, granted.Bottom - thickness, granted.Width, thickness),
        LauncherSettings.DockLeft => new Rect(granted.X, granted.Y, thickness, granted.Height),
        LauncherSettings.DockRight => new Rect(granted.Right - thickness, granted.Y, thickness, granted.Height),
        _ => granted,
    };

    /// <summary>The two-pixel strip an auto-hidden bar collapses to.</summary>
    internal static Rect Collapsed(string edge, Rect monitor) => ProposedRect(edge, CollapsedThickness, monitor);

    /// <summary>True for the edges where the bar's thickness is its height.</summary>
    internal static bool IsHorizontal(string edge) => edge is LauncherSettings.DockTop or LauncherSettings.DockBottom;

    internal static uint EdgeCode(string edge) => edge switch
    {
        LauncherSettings.DockLeft => 0,
        LauncherSettings.DockTop => 1,
        LauncherSettings.DockRight => 2,
        LauncherSettings.DockBottom => 3,
        _ => throw new ArgumentOutOfRangeException(nameof(edge), edge, "Not a dock edge."),
    };

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Release();
        _source.RemoveHook(WndProc);
    }

    private void EnsureRegistered()
    {
        if (_registered)
        {
            return;
        }

        var data = NewData();
        NativeMethods.SHAppBarMessage(AbmNew, ref data);
        _registered = true;
    }

    private NativeMethods.APPBARDATA NewData() => new()
    {
        cbSize = (uint)Marshal.SizeOf<NativeMethods.APPBARDATA>(),
        hWnd = _source.Handle,
        uCallbackMessage = _callbackMessage,
    };

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == (int)_callbackMessage)
        {
            switch ((int)wParam)
            {
                case AbnPosChanged:
                    PositionChanged?.Invoke(this, EventArgs.Empty);
                    break;
                case AbnFullScreenApp:
                    FullScreenChanged?.Invoke(this, lParam != IntPtr.Zero);
                    break;
            }

            handled = true;
        }
        else if (_registered && msg == WmActivate)
        {
            var data = NewData();
            NativeMethods.SHAppBarMessage(AbmActivate, ref data);
        }
        else if (_registered && msg == WmWindowPosChanged)
        {
            var data = NewData();
            NativeMethods.SHAppBarMessage(AbmWindowPosChanged, ref data);
        }

        return IntPtr.Zero;
    }

    private static Rect FromRect(NativeMethods.RECT rect) => new(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);

    private static NativeMethods.RECT ToRect(Rect rect) => new()
    {
        Left = (int)rect.X,
        Top = (int)rect.Y,
        Right = (int)rect.Right,
        Bottom = (int)rect.Bottom,
    };
}
