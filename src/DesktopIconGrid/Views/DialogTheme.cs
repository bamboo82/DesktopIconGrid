using System.IO;
using System.Security;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace DesktopIconGrid.Views;

/// <summary>
/// Gives dialogs the Windows 11 (Fluent) look that follows the system light/dark mode, when the setting asks
/// for it. Applied per window, so the bar keeps its own themed chrome; the few fixed dialog brushes (the grey of the
/// log's Debug lines and path dividers, warnings, errors, swatch borders) are recoloured for dark surfaces at the same time.
/// Hints are not among them: they take the dialog's text colour (0.13.0 WP3).
/// </summary>
public static class DialogTheme
{
    public const string HintBrushKey = "DialogHintBrush";
    public const string WarningBrushKey = "WarningBrush";
    public const string SwatchBorderBrushKey = "SwatchBorderBrush";
    public const string ErrorBrushKey = "ErrorTextBrush";

    /// <summary>Mirrors <c>LauncherSettings.FluentDialogs</c>; read when a dialog is created.</summary>
    public static bool UseFluent { get; private set; } = true;

    /// <summary>True while dialogs are drawn on dark surfaces (Fluent on and Windows in dark mode).</summary>
    public static bool IsDark { get; private set; }

    /// <summary>
    /// Called at startup and whenever the setting or the Windows app mode changes: remembers the choice for
    /// dialogs created later and recolours the dialog brushes. Dialogs already open restyle through the
    /// Fluent theme itself and through <c>DynamicResource</c> lookups of these brushes.
    /// </summary>
    public static void Configure(bool useFluent, ResourceDictionary? resources = null)
    {
        UseFluent = useFluent;
        IsDark = useFluent && IsSystemDarkMode();
        resources ??= Application.Current?.Resources;
        if (resources is null)
        {
            return;
        }

        resources[HintBrushKey] = Frozen(IsDark ? "#FFA8A8A8" : "#FF6E6E6E");
        resources[WarningBrushKey] = Frozen(IsDark ? "#FFFFB86B" : "#FFB55A00");
        resources[SwatchBorderBrushKey] = Frozen(IsDark ? "#FF5C5C5C" : "#FF9A9A9A");
        resources[ErrorBrushKey] = Frozen(IsDark ? "#FFFF6B6B" : "#FFC62828");
    }

    /// <summary>Call after <c>InitializeComponent</c> in every dialog window (not the bar).</summary>
    public static void Apply(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        Services.UiFonts.Apply(window); // Chinese in Taipei Sans TC Light where this PC has it (0.12.0 WP5)
#pragma warning disable WPF0001 // ThemeMode is marked experimental in the WPF reference assemblies
        window.ThemeMode = UseFluent ? ThemeMode.System : ThemeMode.None;
#pragma warning restore WPF0001
        ScaleText(window, Services.UiFonts.TextFactor);
    }

    /// <summary>
    /// A dialog at the text size chosen in Settings (0.13.0 WP4): its whole content is scaled as one (a
    /// <see cref="FrameworkElement.LayoutTransform"/> on it), so what fits at 100 % fits at 150 % in proportion, logs and
    /// code text with it; the width and height it sets, and their limits, grow by the same factor. A dialog that would be
    /// taller or wider than the work area is held to it (<see cref="WorkAreaFit"/>) and its body scrolls, as before.
    /// </summary>
    internal static void ScaleText(Window window, double factor)
    {
        if (Math.Abs(factor - 1) < 0.001 || window.Content is not FrameworkElement root || !root.LayoutTransform.Value.IsIdentity)
        {
            return;
        }

        root.LayoutTransform = new ScaleTransform(factor, factor);
        if (!double.IsNaN(window.Width))
        {
            window.Width *= factor;
        }

        if (!double.IsNaN(window.Height))
        {
            window.Height *= factor;
        }

        window.MinWidth *= factor;
        window.MinHeight *= factor;
        if (!double.IsPositiveInfinity(window.MaxWidth))
        {
            window.MaxWidth *= factor;
        }

        if (!double.IsPositiveInfinity(window.MaxHeight))
        {
            window.MaxHeight *= factor;
        }
    }

    /// <summary>Windows' "Choose your default app mode" setting; dark when the value is 0.</summary>
    public static bool IsSystemDarkMode()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
        }
        catch (Exception ex) when (ex is SecurityException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static SolidColorBrush Frozen(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}
