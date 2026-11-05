using System.Runtime.InteropServices;
using System.Windows;
using DesktopIconGrid.Core.Models;

namespace DesktopIconGrid.Services;

/// <summary>
/// Where the monitors are, for deciding whether a saved bar position is still on a screen. The virtual-screen
/// bounding box is not enough: an L-shaped layout has a dead rectangle inside it where a bar would be invisible.
/// </summary>
/// <remarks>
/// Monitor rectangles come in device pixels; the saved placement is in WPF units, whose scale depends on the
/// monitor the bar was on. Rather than guess that scale, <see cref="IsVisibleAtAnyScale"/> tries every plausible
/// one and only reports "off screen" when none of them lands on a monitor, so a mixed-DPI layout can never move
/// a visible bar by a false verdict (a lost bar still has the tray's Reset position).
/// </remarks>
public static class ScreenGeometry
{
    /// <summary>At least this much of the bar must lie on one monitor to count as visible.</summary>
    public const double MinVisibleWidth = 40;

    public const double MinVisibleHeight = 24;

    /// <summary>Display scales Windows offers, 100 % to 250 %.</summary>
    public static readonly IReadOnlyList<double> PlausibleScales = [1.0, 1.25, 1.5, 1.75, 2.0, 2.25, 2.5];

    private const int SmCxScreen = 0;

    /// <summary>True when at least <see cref="MinVisibleWidth"/> × <see cref="MinVisibleHeight"/> of <paramref name="window"/> lies on one of <paramref name="monitors"/> (same units).</summary>
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
    /// True when the bar, saved in WPF units, would be visible on one of <paramref name="monitorsInPixels"/> at the
    /// primary monitor's scale or at any plausible display scale. Conservative on purpose: see the class remarks.
    /// </summary>
    public static bool IsVisibleAtAnyScale(Rect windowInUnits, IEnumerable<Rect> monitorsInPixels, double primaryScale)
    {
        ArgumentNullException.ThrowIfNull(monitorsInPixels);
        var monitors = monitorsInPixels as IReadOnlyList<Rect> ?? monitorsInPixels.ToList();
        foreach (var scale in PlausibleScales.Prepend(primaryScale).Distinct())
        {
            if (scale <= 0)
            {
                continue;
            }

            var windowInPixels = new Rect(windowInUnits.X * scale, windowInUnits.Y * scale, windowInUnits.Width * scale, windowInUnits.Height * scale);
            if (IsVisibleOn(windowInPixels, monitors))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Scale of the primary monitor (device pixels per WPF unit), 1.0 when unknown.</summary>
    public static double PrimaryScale()
    {
        if (!OperatingSystem.IsWindows())
        {
            return 1.0;
        }

        var pixelWidth = NativeMethods.GetSystemMetrics(SmCxScreen);
        return pixelWidth > 0 && SystemParameters.PrimaryScreenWidth > 0 ? pixelWidth / SystemParameters.PrimaryScreenWidth : 1.0;
    }

    /// <summary>Every monitor with its device name (<c>\\.\DISPLAY1</c>) and bounds in device pixels; empty without a desktop.</summary>
    public static IReadOnlyList<MonitorBounds> Monitors()
    {
        var result = new List<MonitorBounds>();
        if (!OperatingSystem.IsWindows())
        {
            return result;
        }

        NativeMethods.MonitorEnumProc callback = (IntPtr monitor, IntPtr _, ref NativeMethods.RECT _, IntPtr _) =>
        {
            if (Describe(monitor) is { } bounds)
            {
                result.Add(bounds);
            }

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
        return result;
    }

    /// <summary>The monitor a window is on (the nearest one when it is off screen), or <see langword="null"/> without a desktop.</summary>
    public static MonitorBounds? MonitorOfWindow(IntPtr handle)
    {
        if (!OperatingSystem.IsWindows() || handle == IntPtr.Zero)
        {
            return null;
        }

        var monitor = NativeMethods.MonitorFromWindow(handle, NativeMethods.MONITOR_DEFAULTTONEAREST);
        return monitor == IntPtr.Zero ? null : Describe(monitor);
    }

    private static MonitorBounds? Describe(IntPtr monitor)
    {
        var info = new NativeMethods.MONITORINFOEX { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFOEX>() };
        if (!NativeMethods.GetMonitorInfo(monitor, ref info))
        {
            return null;
        }

        var bounds = info.rcMonitor;
        return new MonitorBounds(info.szDevice ?? string.Empty, bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top);
    }

    /// <summary>Bounds of every monitor in device pixels; the virtual screen (as pixels at the primary scale) when enumeration is unavailable.</summary>
    public static IReadOnlyList<Rect> MonitorsInPixels()
    {
        var result = new List<Rect>();
        if (OperatingSystem.IsWindows())
        {
            NativeMethods.MonitorEnumProc callback = (IntPtr _, IntPtr _, ref NativeMethods.RECT rect, IntPtr _) =>
            {
                result.Add(new Rect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top));
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
            var scale = PrimaryScale();
            result.Add(new Rect(
                SystemParameters.VirtualScreenLeft * scale,
                SystemParameters.VirtualScreenTop * scale,
                SystemParameters.VirtualScreenWidth * scale,
                SystemParameters.VirtualScreenHeight * scale));
        }

        return result;
    }
}
