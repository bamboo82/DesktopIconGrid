using System.Runtime.InteropServices;

namespace DesktopIconGrid.Services;

/// <summary>
/// Win32 declarations for windows, monitors, the desktop toolbar (SHAppBarMessage), the notification area
/// (Shell_NotifyIcon), the icons drawn for it and the DWM backdrop: one declaration each, shared by every caller.
/// BITMAPINFOHEADER, DestroyIcon, DeleteObject, SetForegroundWindow and DwmSetWindowAttribute are in NativeMethods.cs.
/// </summary>
internal static partial class NativeMethods
{
    public const int GWL_EXSTYLE = -20;
    public const int GWL_STYLE = -16;
    public const long WS_CAPTION = 0x00C00000; // WS_BORDER | WS_DLGFRAME: a title bar
    public const long WS_THICKFRAME = 0x00040000; // a sizing border
    public const long WS_EX_TOOLWINDOW = 0x00000080;
    public const long WS_EX_LAYERED = 0x00080000;
    public const long WS_EX_NOACTIVATE = 0x08000000;
    public const uint LWA_ALPHA = 0x2;

    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;

    /// <summary>Posts the change to the window's thread instead of waiting for it: a hung program cannot block the bar.</summary>
    public const uint SWP_ASYNCWINDOWPOS = 0x4000;

    public const uint GA_ROOTOWNER = 3;
    public const uint MONITOR_DEFAULTTONEAREST = 2;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    // ----- windows -------------------------------------------------------------------------------------------

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern uint RegisterWindowMessage(string lpString);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);

    /// <summary>The window's extended style. 32-bit user32 has no GetWindowLongPtr export, so it reads GetWindowLong there.</summary>
    public static long GetExStyle(IntPtr hwnd) =>
        IntPtr.Size == 8 ? GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64() : GetWindowLong(hwnd, GWL_EXSTYLE);

    /// <summary>The window's style (<c>WS_*</c>), read the same way as <see cref="GetExStyle"/>.</summary>
    public static long GetStyle(IntPtr hwnd) =>
        IntPtr.Size == 8 ? GetWindowLongPtr(hwnd, GWL_STYLE).ToInt64() : GetWindowLong(hwnd, GWL_STYLE);

    public static void SetExStyle(IntPtr hwnd, long style)
    {
        if (IntPtr.Size == 8)
        {
            SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(style));
        }
        else
        {
            SetWindowLong(hwnd, GWL_EXSTYLE, unchecked((int)style));
        }
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    // ----- top-level windows of other programs (switching to a running program) -----------------------------

    public const long WS_EX_APPWINDOW = 0x00040000;
    public const uint GW_OWNER = 4;
    public const int DWMWA_CLOAKED = 14;
    public const int SW_MINIMIZE = 6;
    public const int SW_RESTORE = 9;
    public const int SW_MAXIMIZE = 3;

    [StructLayout(LayoutKind.Sequential)]
    public struct WINDOWPLACEMENT
    {
        public int Length;
        public int Flags;
        public int ShowCmd;
        public int MinX;
        public int MinY;
        public int MaxX;
        public int MaxY;
        public RECT NormalPosition;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowPlacement(IntPtr hwnd, ref WINDOWPLACEMENT placement);

    /// <summary>Does not wait for the window's thread: a hung program cannot block the bar.</summary>
    public const int WPF_ASYNCWINDOWPLACEMENT = 0x0004;

    /// <summary>In a minimised window's placement: it comes back maximised.</summary>
    public const int WPF_RESTORETOMAXIMIZED = 0x0002;

    /// <summary>Whether the window is snapped to a half or into a snap layout (Windows 10 1903 and later).</summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindowArranged(IntPtr hwnd);

    public const int SW_SHOWNORMAL = 1;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPlacement(IntPtr hwnd, ref WINDOWPLACEMENT placement);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsZoomed(IntPtr hwnd);
    public const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    public const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    public const uint WINEVENT_SKIPOWNPROCESS = 0x0002;
    public const int OBJID_WINDOW = 0;

    public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    public delegate void WinEventProc(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint eventThread, uint eventTime);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsIconic(IntPtr hwnd);

    [DllImport("user32.dll")]
    public static extern IntPtr GetWindow(IntPtr hwnd, uint command);

    [DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = CharSet.Unicode)]
    public static extern int GetClassName(IntPtr hwnd, [Out] char[] className, int maxCount);

    [DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);

    /// <summary>Posts the change instead of waiting for the window: a hung program cannot block the bar.</summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ShowWindowAsync(IntPtr hwnd, int command);

    [DllImport("user32.dll")]
    public static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr module, WinEventProc callback, uint processId, uint threadId, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWinEvent(IntPtr hook);

    // ----- monitors ------------------------------------------------------------------------------------------

    public delegate bool MonitorEnumProc(IntPtr monitor, IntPtr deviceContext, ref RECT bounds, IntPtr data);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumDisplayMonitors(IntPtr deviceContext, IntPtr clip, MonitorEnumProc callback, IntPtr data);

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    public const int MDT_EFFECTIVE_DPI = 0;

    /// <summary>The monitor's DPI (96 at 100 %); an HRESULT, 0 on success. Windows 8.1 and later.</summary>
    [DllImport("shcore.dll")]
    public static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    public static extern int GetSystemMetricsForDpi(int nIndex, uint dpi);

    [DllImport("user32.dll")]
    public static extern uint GetDpiForSystem();

    // ----- desktop toolbar (AppBar) --------------------------------------------------------------------------

    [StructLayout(LayoutKind.Sequential)]
    public struct APPBARDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public RECT rc;
        public IntPtr lParam;
    }

    [DllImport("shell32.dll")]
    public static extern UIntPtr SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);

    // ----- notification area ---------------------------------------------------------------------------------

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct NOTIFYICONDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool Shell_NotifyIcon(uint message, ref NOTIFYICONDATA data);

    // ----- icons drawn by the app ----------------------------------------------------------------------------

    [StructLayout(LayoutKind.Sequential)]
    public struct ICONINFO
    {
        [MarshalAs(UnmanagedType.Bool)]
        public bool fIcon;
        public uint xHotspot;
        public uint yHotspot;
        public IntPtr hbmMask;
        public IntPtr hbmColor;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr LoadIcon(IntPtr instance, IntPtr iconName);

    [DllImport("user32.dll")]
    public static extern IntPtr CreateIconIndirect(ref ICONINFO iconInfo);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER bitmapInfo, uint usage, out IntPtr bits, IntPtr section, uint offset);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateBitmap(int width, int height, uint planes, uint bitCount, byte[] bits);

    // ----- DWM -----------------------------------------------------------------------------------------------

    [StructLayout(LayoutKind.Sequential)]
    public struct MARGINS
    {
        public int Left;
        public int Right;
        public int Top;
        public int Bottom;
    }

    [DllImport("dwmapi.dll")]
    public static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);
}
