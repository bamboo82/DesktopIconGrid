using System.Runtime.InteropServices;
using System.Windows;

namespace DesktopIconGrid.Services;

/// <summary>
/// Where the monitors are, for deciding whether a saved bar position is still on a screen. The virtual-screen
/// bounding box is not enough: an L-shaped layout has a dead rectangle inside it where a bar would be invisible.
/// </summary>
public static class ScreenGeometry
{
    /// <summary>At least this much of the bar must lie on one monitor to count as visible.</summary>
    public const double MinVisibleWidth = 40;

    public const double MinVisibleHeight = 24;

    private const int SmCxScreen = 0;

    /// <summary>True when at least <see cref="MinVisibleWidth"/> × <see cref="MinVisibleHeight"/> of <paramref name="window"/> lies on one of <paramref name="monitors"/>.</summary>
    public static bool IsVisibleOn(Rect window, IEnumerable<Rect> monitors)
    {
        ArgumentNullException.ThrowIfNull(monitors);
        foreach (var monitor in monitors)
        {
            var visible = Rect.Intersect(monitor, window);
            if (!visible.IsEmpty && visible.Width >= MinVisibleWidth && visible.Height >= MinVisibleHeight)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Bounds of every monitor in WPF units (device pixels divided by the primary monitor's scale, the unit that
    /// <c>Window.Left</c>/<c>Top</c> use); the virtual screen when enumeration is unavailable.
    /// </summary>
    public static IReadOnlyList<Rect> Monitors()
    {
        var result = new List<Rect>();
        if (OperatingSystem.IsWindows())
        {
            var pixelWidth = NativeMethods.GetSystemMetrics(SmCxScreen);
            var scale = pixelWidth > 0 ? SystemParameters.PrimaryScreenWidth / pixelWidth : 1.0;
            NativeMethods.MonitorEnumProc callback = (IntPtr _, IntPtr _, ref NativeMethods.RECT rect, IntPtr _) =>
            {
                result.Add(new Rect(rect.Left * scale, rect.Top * scale, (rect.Right - rect.Left) * scale, (rect.Bottom - rect.Top) * scale));
                return true;
            };
            try
            {
                NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
            }
            catch (EntryPointNotFoundException)
            {
                result.Clear();
            }

            GC.KeepAlive(callback);
        }

        if (result.Count == 0)
        {
            result.Add(new Rect(
                SystemParameters.VirtualScreenLeft,
                SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenWidth,
                SystemParameters.VirtualScreenHeight));
        }

        return result;
    }

    private static class NativeMethods
    {
        public delegate bool MonitorEnumProc(IntPtr monitor, IntPtr deviceContext, ref RECT bounds, IntPtr data);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EnumDisplayMonitors(IntPtr deviceContext, IntPtr clip, MonitorEnumProc callback, IntPtr data);

        [DllImport("user32.dll")]
        public static extern int GetSystemMetrics(int index);
    }
}
