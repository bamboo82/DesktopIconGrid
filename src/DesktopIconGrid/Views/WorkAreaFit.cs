using System.Windows;
using System.Windows.Interop;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Services;

namespace DesktopIconGrid.Views;

/// <summary>
/// Keeps a dialog that sizes itself to its content inside the work area of the screen it opens on: never taller than
/// that work area (its body scrolls instead, with its buttons still in sight), and moved back inside it when it opened
/// partly off it, as a tall dialog centred on a bar near the edge of the screen would, or when it grew past an edge
/// afterwards (Advanced opened in Properties, a hint appeared).
/// </summary>
internal static class WorkAreaFit
{
    /// <summary>Room left free above and below the dialog, in WPF units.</summary>
    private const double Gap = 8;

    /// <summary>Call in the dialog's constructor.</summary>
    public static void Attach(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        window.SourceInitialized += (_, _) => LimitHeight(window);
        window.Loaded += (_, _) =>
        {
            MoveInside(window);
            window.SizeChanged += (_, _) => MoveInside(window); // SizeToContent grows it downward
        };
    }

    /// <summary>The work area of the screen the dialog opens on: its owner's, or its own.</summary>
    private static ScreenArea? AreaOf(Window window)
    {
        var anchor = window.Owner is { } owner ? new WindowInteropHelper(owner).Handle : IntPtr.Zero;
        if (anchor == IntPtr.Zero)
        {
            anchor = new WindowInteropHelper(window).Handle;
        }

        return anchor != IntPtr.Zero && NativeMethods.GetWindowRect(anchor, out var rect)
            ? ScreenGeometry.AreaAt((rect.Left + rect.Right) / 2, (rect.Top + rect.Bottom) / 2)
            : null;
    }

    private static void LimitHeight(Window window)
    {
        if (AreaOf(window) is { WorkArea.Height: > 0 } area)
        {
            window.MaxHeight = Math.Max(240, (area.WorkArea.Height / area.Scale) - (2 * Gap));
        }
    }

    private static void MoveInside(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero || !NativeMethods.GetWindowRect(handle, out var rect)
            || ScreenGeometry.AreaAt((rect.Left + rect.Right) / 2, (rect.Top + rect.Bottom) / 2) is not { WorkArea: { Width: > 0 } work })
        {
            return;
        }

        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        var left = Math.Clamp(rect.Left, work.Left, Math.Max(work.Left, work.Right - width));
        var top = Math.Clamp(rect.Top, work.Top, Math.Max(work.Top, work.Bottom - height));
        if (left != rect.Left || top != rect.Top)
        {
            ScreenGeometry.MoveToPixels(window, new PixelRect(left, top, width, height));
        }
    }
}
