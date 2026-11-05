namespace DesktopIconGrid.Core.Models;

/// <summary>
/// Last known position and size of the bar: in device-independent pixels (WPF units; written for older versions
/// and used as the fallback), and since 0.6.0 also in device pixels relative to the monitor the bar was on, named by
/// its device name, so the bar returns to that monitor whatever the display scales involved.
/// </summary>
public sealed class WindowPlacement
{
    public double Left { get; set; }
    public double Top { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }

    /// <summary>Device name of the monitor the bar was on, e.g. <c>\\.\DISPLAY2</c>.</summary>
    public string? Monitor { get; set; }

    /// <summary>Bounds in device pixels, relative to the top-left corner of <see cref="Monitor"/>.</summary>
    public int? PixelLeft { get; set; }
    public int? PixelTop { get; set; }
    public int? PixelWidth { get; set; }
    public int? PixelHeight { get; set; }

    public bool IsUsable => Width > 0 && Height > 0
        && double.IsFinite(Left) && double.IsFinite(Top)
        && double.IsFinite(Width) && double.IsFinite(Height);

    public bool HasPixels => Monitor is not null && PixelLeft is not null && PixelTop is not null && PixelWidth is > 0 && PixelHeight is > 0;

    /// <summary>
    /// Where the bar goes, in device pixels, when the monitor it was saved on is still there: the saved place, pulled
    /// inside the monitor when it would stick out (after a change of resolution, for instance). <see langword="null"/>
    /// when there is no pixel data or the monitor is gone; the caller then falls back to the WPF-unit values.
    /// </summary>
    public PixelRect? ResolvePixels(IEnumerable<MonitorBounds> monitors)
    {
        ArgumentNullException.ThrowIfNull(monitors);
        if (!HasPixels)
        {
            return null;
        }

        var monitor = monitors.FirstOrDefault(m => string.Equals(m.DeviceName, Monitor, StringComparison.OrdinalIgnoreCase));
        if (monitor is null || monitor.Width <= 0 || monitor.Height <= 0)
        {
            return null;
        }

        var width = Math.Min(PixelWidth!.Value, monitor.Width);
        var height = Math.Min(PixelHeight!.Value, monitor.Height);
        var left = Math.Clamp(monitor.Left + PixelLeft!.Value, monitor.Left, monitor.Right - width);
        var top = Math.Clamp(monitor.Top + PixelTop!.Value, monitor.Top, monitor.Bottom - height);
        return new PixelRect(left, top, width, height);
    }
}
