namespace DesktopIconGrid.Services;

/// <summary>
/// Sets whole-window transparency through the Win32 layered-window alpha. Unlike WPF's
/// <c>Window.Opacity</c> this works for ordinary (non-<c>AllowsTransparency</c>) windows, keeps
/// hardware rendering and leaves the resize borders alone.
/// </summary>
public static class WindowAlpha
{
    /// <summary>Applies an opacity percentage (100 = opaque). Removes the layered style again at 100%.</summary>
    public static void Apply(IntPtr hwnd, int opacityPercent)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var percent = Math.Clamp(opacityPercent, 10, 100);
        var style = NativeMethods.GetExStyle(hwnd);
        if (percent >= 100)
        {
            if ((style & NativeMethods.WS_EX_LAYERED) != 0)
            {
                NativeMethods.SetLayeredWindowAttributes(hwnd, 0, 255, NativeMethods.LWA_ALPHA);
                NativeMethods.SetExStyle(hwnd, style & ~NativeMethods.WS_EX_LAYERED);
            }

            return;
        }

        if ((style & NativeMethods.WS_EX_LAYERED) == 0)
        {
            NativeMethods.SetExStyle(hwnd, style | NativeMethods.WS_EX_LAYERED);
        }

        NativeMethods.SetLayeredWindowAttributes(hwnd, 0, (byte)Math.Round(255 * percent / 100.0), NativeMethods.LWA_ALPHA);
    }
}
