using System.Windows;
using System.Windows.Media;
using DesktopIconGrid.Core.Models;

namespace DesktopIconGrid.Services;

/// <summary>
/// Applies a <see cref="ThemePalette"/> by replacing the <c>Theme.*</c> brushes in the application
/// resources. Styles reference them with <c>DynamicResource</c>, so the whole bar restyles at once.
/// </summary>
public static class ThemeManager
{
    public const string BackgroundKey = "Theme.Background";
    public const string BarBackgroundKey = "Theme.BarBackground";
    public const string PopupBackgroundKey = "Theme.PopupBackground";
    public const string BorderKey = "Theme.Border";
    public const string ForegroundKey = "Theme.Foreground";
    public const string HintKey = "Theme.Hint";
    public const string HoverKey = "Theme.Hover";
    public const string HoverBorderKey = "Theme.HoverBorder";
    public const string PressedKey = "Theme.Pressed";
    public const string GripKey = "Theme.Grip";
    public const string GripPatternKey = "Theme.GripPattern";
    public const string AccentKey = "Theme.Accent";
    public const string DropTargetKey = "Theme.DropTarget";

    /// <summary>Id of the palette used under Windows High Contrast (never stored in settings).</summary>
    public const string HighContrastId = "high-contrast";

    /// <summary>
    /// Writes the palette's brushes. With <paramref name="translucentBar"/> the bar's own background becomes a
    /// tint of <paramref name="tintOpacity"/> over the Windows 11 backdrop; the popup background starts solid
    /// and <see cref="SetPopupBackground"/> changes it once the popup's own backdrop is known.
    /// </summary>
    public static void Apply(ThemePalette palette, ResourceDictionary? resources = null, bool translucentBar = false, double tintOpacity = 1.0)
    {
        ArgumentNullException.ThrowIfNull(palette);
        resources ??= Application.Current?.Resources;
        if (resources is null)
        {
            return;
        }

        var grip = ToBrush(palette.Grip);
        resources[BackgroundKey] = ToBrush(palette.Background);
        resources[BarBackgroundKey] = ToBrush(palette.Background, translucentBar ? Math.Clamp(tintOpacity, 0.0, 1.0) : 1.0);
        resources[PopupBackgroundKey] = ToBrush(palette.Background);
        resources[BorderKey] = ToBrush(palette.Border);
        resources[ForegroundKey] = ToBrush(palette.Foreground);
        resources[HintKey] = ToBrush(palette.Foreground, opacity: 0.65);
        resources[HoverKey] = ToBrush(palette.Hover);
        resources[HoverBorderKey] = ToBrush(palette.HoverBorder);
        resources[PressedKey] = ToBrush(palette.Pressed);
        resources[GripKey] = grip;
        resources[GripPatternKey] = CreateGripPattern(grip);
        resources[AccentKey] = ToBrush(palette.Accent);
        resources[DropTargetKey] = ToBrush(palette.DropTarget);
    }

    /// <summary>The group popup's background: a tint when its window got a backdrop, otherwise the solid theme colour.</summary>
    public static void SetPopupBackground(ThemePalette palette, bool translucent, double tintOpacity, ResourceDictionary? resources = null)
    {
        ArgumentNullException.ThrowIfNull(palette);
        resources ??= Application.Current?.Resources;
        if (resources is not null)
        {
            resources[PopupBackgroundKey] = ToBrush(palette.Background, translucent ? Math.Clamp(tintOpacity, 0.0, 1.0) : 1.0);
        }
    }

    /// <summary>The palette the bar really uses: under Windows High Contrast the system colours replace the theme.</summary>
    public static ThemePalette EffectivePalette(ThemePalette palette) =>
        SystemParameters.HighContrast ? HighContrast() : palette;

    /// <summary>
    /// A palette built from the current High Contrast colours: window colour and text, highlight for the
    /// selection and drop target, outlines instead of fills for hover (fills would hide the text).
    /// </summary>
    public static ThemePalette HighContrast()
    {
        var window = Hex(SystemColors.WindowColor);
        var text = Hex(SystemColors.WindowTextColor);
        var highlight = Hex(SystemColors.HighlightColor);
        var frame = Hex(SystemColors.WindowFrameColor);
        var hot = Hex(SystemColors.HotTrackColor);
        return new ThemePalette(
            HighContrastId, "High Contrast",
            Background: window,
            Border: frame,
            Foreground: text,
            Hover: "#00000000",
            HoverBorder: highlight,
            Pressed: highlight,
            Grip: text,
            Accent: hot,
            DropTarget: "#66" + highlight[1..]);
    }

    public static Color ToColor(string hex)
    {
        try
        {
            return (Color)ColorConverter.ConvertFromString(hex);
        }
        catch (FormatException)
        {
            return Colors.Gray;
        }
    }

    public static SolidColorBrush ToBrush(string hex, double opacity = 1.0)
    {
        var brush = new SolidColorBrush(ToColor(hex)) { Opacity = opacity };
        brush.Freeze();
        return brush;
    }

    private static string Hex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    /// <summary>Tiled dots for the move grip.</summary>
    private static DrawingBrush CreateGripPattern(Brush dot)
    {
        var drawing = new GeometryDrawing(dot, null, new EllipseGeometry(new Point(2, 2), 1, 1));
        var brush = new DrawingBrush(drawing)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 4, 4),
            ViewportUnits = BrushMappingMode.Absolute,
            Viewbox = new Rect(0, 0, 4, 4),
            ViewboxUnits = BrushMappingMode.Absolute,
        };
        brush.Freeze();
        return brush;
    }
}
