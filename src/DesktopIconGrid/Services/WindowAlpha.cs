using System.Runtime.InteropServices;

namespace DesktopIconGrid.Services;

/// <summary>
/// Sets whole-window transparency through the Win32 layered-window alpha. Unlike WPF's
/// <c>Window.Opacity</c> this works for ordinary (non-<c>AllowsTransparency</c>) windows, keeps
/// hardware rendering and leaves the resize borders alone.
/// </summary>
public static class WindowAlpha
{
    private const int GwlExStyle = -20;
    private const long WsExLayered = 0x00080000;
    private const uint LwaAlpha = 0x2;

    /// <summary>Applies an opacity percentage (100 = opaque). Removes the layered style again at 100%.</summary>
    public static void Apply(IntPtr hwnd, int opacityPercent)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var percent = Math.Clamp(opacityPercent, 10, 100);
        var style = GetExStyle(hwnd);
        if (percent >= 100)
        {
            if ((style & WsExLayered) != 0)
            {
                NativeMethods.SetLayeredWindowAttributes(hwnd, 0, 255, LwaAlpha);
                SetExStyle(hwnd, style & ~WsExLayered);
            }

            return;
        }

        if ((style & WsExLayered) == 0)
        {
            SetExStyle(hwnd, style | WsExLayered);
        }

        NativeMethods.SetLayeredWindowAttributes(hwnd, 0, (byte)Math.Round(255 * percent / 100.0), LwaAlpha);
    }

    private static long GetExStyle(IntPtr hwnd) =>
        IntPtr.Size == 8 ? NativeMethods.GetWindowLongPtr(hwnd, GwlExStyle).ToInt64() : NativeMethods.GetWindowLong(hwnd, GwlExStyle);

    private static void SetExStyle(IntPtr hwnd, long style)
    {
        if (IntPtr.Size == 8)
        {
            NativeMethods.SetWindowLongPtr(hwnd, GwlExStyle, new IntPtr(style));
        }
        else
        {
            NativeMethods.SetWindowLong(hwnd, GwlExStyle, unchecked((int)style));
        }
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        public static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
        public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
        public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);
    }
}
