using System.Windows.Interop;
using System.Windows.Media;
using DesktopIconGrid.Core.Diagnostics;

namespace DesktopIconGrid.Services;

/// <summary>
/// A background that lets the desktop show through while everything drawn on it stays solid (0.14.0 WP3, the owner's
/// request): the bar's background brush carries the transparency, not the window. Before 0.14.0 the window was layered
/// with one alpha for all of it, so icons and names faded with the background.
/// </summary>
/// <remarks>
/// The window stays an ordinary one, not layered: WPF draws on a transparent surface (the composition target's background
/// is transparent) and the Desktop Window Manager is asked to use that surface's alpha, through
/// <c>DwmEnableBlurBehindWindow</c> with an empty blur region. Since Windows 8 that call blurs nothing; what it still does
/// is compose the window by its alpha, on Windows 10 as on Windows 11, without a system backdrop. A layered window
/// (<c>AllowsTransparency</c>) would do the same per pixel, but it cannot take Mica or Acrylic, must be chosen before the
/// window exists, and passes clicks through where it is fully transparent; an ordinary window is hit by its rectangle,
/// so a click on the background always reaches the bar. What it costs: text on such a background is better smoothed in
/// grey than with ClearType, whose colours are worked out against the window's own colour, not against what shows
/// through; the bar does that while it is see-through and keeps ClearType while its background is opaque.
/// </remarks>
public static class WindowTransparency
{
    /// <summary>
    /// Makes the window of <paramref name="source"/> see-through where it draws nothing solid, or ends that. Returns
    /// whether the window is see-through now: false when it was not asked for, and when Windows refused, which is logged;
    /// the caller then paints the background solid, so a refused call never leaves a window that is not painted.
    /// </summary>
    public static bool Set(HwndSource source, bool seeThrough)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Handle == IntPtr.Zero)
        {
            return false;
        }

        var blurBehind = new NativeMethods.DWM_BLURBEHIND { dwFlags = NativeMethods.DWM_BB_ENABLE, fEnable = seeThrough };
        var region = IntPtr.Zero;
        if (seeThrough)
        {
            region = NativeMethods.CreateRectRgn(0, 0, -1, -1); // empty: no part of the window is blurred
            blurBehind.dwFlags |= NativeMethods.DWM_BB_BLURREGION;
            blurBehind.hRgnBlur = region;
        }

        int result;
        try
        {
            result = NativeMethods.DwmEnableBlurBehindWindow(source.Handle, ref blurBehind);
        }
        finally
        {
            if (region != IntPtr.Zero)
            {
                NativeMethods.DeleteObject(region);
            }
        }

        if (!seeThrough)
        {
            return false;
        }

        if (result != 0)
        {
            AppLog.Default.Warning("App", $"Windows refused the see-through background (HRESULT 0x{result:X8}); the background stays solid");
            return false;
        }

        if (source.CompositionTarget is { } target)
        {
            target.BackgroundColor = Colors.Transparent;
        }

        return true;
    }
}
