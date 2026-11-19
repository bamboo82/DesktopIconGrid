using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Services;

namespace DesktopIconGrid.Views;

/// <summary>
/// Keeps a dialog that sizes itself to its content inside the work area of the screen it opens on: never taller or
/// wider than that work area (its body scrolls instead, with its buttons still in sight), and moved back inside it when it opened
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
        window.SourceInitialized += (_, _) => LimitSize(window);
        window.Loaded += (_, _) =>
        {
            MoveInside(window);

            // SizeToContent grows it downward. SizeChanged comes while the content is arranged at the new size, before
            // the window itself takes that size (HwndSource resizes it after the layout pass), so the move waits for it.
            window.SizeChanged += (_, _) => window.Dispatcher.BeginInvoke(DispatcherPriority.Background, () => MoveInside(window));
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

    /// <summary>
    /// A dialog of a fixed size, or one that sizes itself to its content, is kept within the work area for good
    /// (<c>MaxHeight</c>, <c>MaxWidth</c>). A window the user sizes (the names table, which opens 1,230 wide) only opens
    /// within it: limits would stop Maximise from filling the screen and keep it from growing on a larger monitor
    /// (0.12.0 release check U3).
    /// </summary>
    private static void LimitSize(Window window)
    {
        if (AreaOf(window) is not { WorkArea.Height: > 0 } area)
        {
            return;
        }

        var height = Math.Max(240, (area.WorkArea.Height / area.Scale) - (2 * Gap));
        var width = Math.Max(320, (area.WorkArea.Width / area.Scale) - (2 * Gap));
        if (window.ResizeMode is ResizeMode.CanResize or ResizeMode.CanResizeWithGrip && window.SizeToContent == SizeToContent.Manual)
        {
            if (window.Width > width)
            {
                window.Width = width;
            }

            if (window.Height > height)
            {
                window.Height = height;
            }

            return;
        }

        window.MaxHeight = height;
        window.MaxWidth = width;
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
