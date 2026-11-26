using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using DesktopIconGrid.Core.Diagnostics;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Services;

namespace DesktopIconGrid.Views;

/// <summary>
/// The bar's background on a window (0.14.0 WP3), shared with the window at the pointer since 0.15.0 (WP3, so that it
/// looks as the bar does). A Windows 11 backdrop (Mica or Acrylic) when the bar has one chosen and Windows takes it;
/// without one, a background that is not opaque lets the desktop show through (<see cref="WindowTransparency"/>); else,
/// and always under High Contrast, the solid colour. Only the background takes the transparency: the window's content
/// draws <c>Theme.BarBackground</c>, whose tint <see cref="ThemeManager"/> sets for the bar, and everything on it stays
/// solid. While it is see-through, text is smoothed in grey and gets its edges, and the lines their colour
/// (<see cref="MainWindow.SetSeeThroughLook"/>).
/// </summary>
/// <param name="window">The window; its content paints <c>Theme.BarBackground</c>.</param>
/// <param name="resizeBorder">How far in from its edges the window can be resized at first (<see cref="ResizeBorder"/>).</param>
/// <param name="name">How the log names the window.</param>
internal sealed class BarSurface(Window window, double resizeBorder, string name)
{
    private double _resizeBorder = resizeBorder;

    /// <summary>Windows paints the chosen backdrop behind the window.</summary>
    public bool BackdropActive { get; private set; }

    /// <summary>The background lets the desktop show through, without a backdrop.</summary>
    public bool SeeThrough { get; private set; }

    /// <summary>Whether Windows is in High Contrast; tests stand in for it.</summary>
    internal Func<bool> HighContrast { get; set; } = () => SystemParameters.HighContrast;

    /// <summary>
    /// How far in from its edges the window can be resized, in units: the bar's 6 (<see cref="BarResizing"/>), nothing for
    /// a window that is not resized and for the bar while it cannot be (0.16.1 WP1). Inside it the window answers as an
    /// edge to drag; without it every point is the window's own, so a press at the edge does what a press on what is
    /// there does. A change takes effect at once.
    /// </summary>
    public double ResizeBorder
    {
        get => _resizeBorder;
        set
        {
            if (_resizeBorder.Equals(value))
            {
                return;
            }

            _resizeBorder = value;
            SetChrome();
        }
    }

    /// <summary>
    /// Brings the window's background to the settings: <paramref name="backdrop"/> (<see cref="LauncherSettings.BarBackdrop"/>),
    /// the theme's <paramref name="palette"/> and the background's <paramref name="opacity"/> in percent. Call it once the
    /// window has a handle, and again whenever one of them may have changed.
    /// </summary>
    /// <param name="picture">
    /// A background picture is drawn (0.15.0 WP5): text and lines take their edges at every opacity then, since they may
    /// stand on any part of it.
    /// </param>
    public void Apply(HwndSource source, string? backdrop, ThemePalette palette, int opacity, double iconSize, bool picture = false)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(palette);
        var highContrast = HighContrast();
        var wanted = highContrast ? LauncherSettings.BackdropSolid : backdrop; // High Contrast asks for flat colours
        if (SeeThrough && !string.Equals(wanted, LauncherSettings.BackdropSolid, StringComparison.Ordinal) && WindowBackdrop.IsSupported)
        {
            SeeThrough = WindowTransparency.Set(source, seeThrough: false); // a backdrop takes over the background
        }

        var active = WindowBackdrop.Apply(source.Handle, wanted, darkMode: ThemeCatalog.Luminance(palette.Background) <= 0.6);
        if (active != BackdropActive)
        {
            BackdropActive = active;
            SetChrome();
            AppLog.Default.Info("App", active ? $"{name} background: {wanted} backdrop" : $"{name} background: solid colour");
        }

        // Without a backdrop, a background that is not opaque shows the desktop; an opaque one stays as it was. High
        // Contrast keeps its flat colours.
        var seeThrough = !active && !highContrast && opacity < LauncherSettings.MaxOpacity;
        if (seeThrough != SeeThrough)
        {
            SeeThrough = WindowTransparency.Set(source, seeThrough);
            if (seeThrough)
            {
                AppLog.Default.Info("App", SeeThrough ? $"{name} background: solid colour at {opacity} %, the desktop shows through" : $"{name} background: solid colour, opaque");
            }
        }

        if (active || SeeThrough)
        {
            window.Background = Brushes.Transparent;
            if (source.CompositionTarget is { } target)
            {
                target.BackgroundColor = Colors.Transparent; // WindowChrome does this too while DWM composes; said here as well
            }
        }
        else
        {
            window.ClearValue(Control.BackgroundProperty);
        }

        // On a see-through background the edges of text are smoothed in grey: ClearType works its colours out against the
        // window's own colour, not against what shows through, and would fringe there. On an opaque one, ClearType (0.15.0
        // WP9); with a backdrop, WPF's own choice, as before.
        TextOptions.SetTextRenderingMode(window, TextSharpness.RenderingFor(SeeThrough, active));
        MainWindow.SetSeeThroughLook(window.Resources, SeeThrough || picture ? palette.Foreground : null, iconSize, picture);
    }

    /// <summary>The window's frame: none drawn, no caption, <see cref="ResizeBorder"/> at its edges, the backdrop's glass or none.</summary>
    private void SetChrome() =>
        // For DWM to paint behind the client area WPF must not: with a backdrop, extend the frame into it and leave the window transparent.
        WindowChrome.SetWindowChrome(window, new WindowChrome
        {
            CaptionHeight = 0,
            ResizeBorderThickness = new Thickness(_resizeBorder),
            GlassFrameThickness = BackdropActive ? new Thickness(-1) : new Thickness(0),
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false,
        });
}
