using System.Runtime.InteropServices;
using DesktopIconGrid.Core.Diagnostics;
using DesktopIconGrid.Core.Models;

namespace DesktopIconGrid.Services;

/// <summary>
/// Asks the Desktop Window Manager to paint a Windows 11 backdrop (Mica or Acrylic) behind a window. The
/// window must then leave its background transparent so the backdrop shows through; the caller decides that
/// from the return value, so an unsupported system or a refused call never leaves an unpainted window.
/// </summary>
public static class WindowBackdrop
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaSystemBackdropType = 38;
    private const int CornerDoNotRound = 1;
    private const int BackdropNone = 1;
    private const int BackdropMica = 2;
    private const int BackdropAcrylic = 3;

    /// <summary>Windows 11 22H2 (build 22621) introduced the system backdrop attribute.</summary>
    public static bool IsSupported => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621);

    /// <summary>
    /// Applies the backdrop named by <see cref="LauncherSettings.BarBackdrop"/> to the bar window (its WindowChrome
    /// extends the frame into the client area). Returns true when DWM accepted Mica or Acrylic; false for a solid
    /// background, an older Windows or a refused call (the caller then paints solid). The bar keeps square corners
    /// in both modes so its one-pixel border is never clipped.
    /// </summary>
    public static bool Apply(IntPtr hwnd, string? backdrop, bool darkMode)
    {
        if (hwnd == IntPtr.Zero || !IsSupported)
        {
            return false;
        }

        var type = backdrop?.Trim().ToLowerInvariant() switch
        {
            LauncherSettings.BackdropMica => BackdropMica,
            LauncherSettings.BackdropAcrylic => BackdropAcrylic,
            _ => BackdropNone,
        };

        var corners = CornerDoNotRound;
        _ = NativeMethods.DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref corners, sizeof(int));

        // Mica takes its tint from the app's light/dark mode, so follow the theme's brightness.
        var dark = darkMode ? 1 : 0;
        _ = NativeMethods.DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int));

        var result = NativeMethods.DwmSetWindowAttribute(hwnd, DwmwaSystemBackdropType, ref type, sizeof(int));
        if (result != 0)
        {
            AppLog.Default.Warning("App", $"Windows refused the {backdrop} backdrop (HRESULT 0x{result:X8}); the bar stays solid");
            return false;
        }

        return type != BackdropNone;
    }

    /// <summary>
    /// Acrylic for a popup window (the group popup has no WindowChrome, so the DWM frame is extended into its
    /// whole client area here). Returns false, leaving the popup solid, when either call is refused.
    /// </summary>
    public static bool ApplyToPopup(IntPtr hwnd, bool darkMode)
    {
        if (hwnd == IntPtr.Zero || !IsSupported)
        {
            return false;
        }

        var margins = new NativeMethods.MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        var extended = NativeMethods.DwmExtendFrameIntoClientArea(hwnd, ref margins);
        if (extended != 0)
        {
            AppLog.Default.Debug("App", $"The popup frame could not be extended (HRESULT 0x{extended:X8}); the popup stays solid");
            return false;
        }

        var dark = darkMode ? 1 : 0;
        _ = NativeMethods.DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int));
        var type = BackdropAcrylic;
        var result = NativeMethods.DwmSetWindowAttribute(hwnd, DwmwaSystemBackdropType, ref type, sizeof(int));
        if (result != 0)
        {
            AppLog.Default.Debug("App", $"Windows refused the popup backdrop (HRESULT 0x{result:X8}); the popup stays solid");
            return false;
        }

        return true;
    }
}
