using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DesktopIconGrid.Views;

/// <summary>
/// How the app's text is laid out and smoothed, in every window, menu and tooltip (0.15.0 WP9, the owner's request: text
/// looked blurry). Small text is laid out on whole pixels (<see cref="TextFormattingMode.Display"/>, sharp below about
/// 15 pt) and large text as drawn (<see cref="TextFormattingMode.Ideal"/>, smoother shapes); ClearType on an opaque
/// background, grey smoothing on a see-through one, where ClearType cannot be drawn. Text under a scale (a dialog at a
/// text size other than 100 %) is laid out as drawn: whole pixels at the unscaled size are not whole pixels once scaled.
/// Text under an effect (the see-through look's edge) is grey-smoothed by WPF whatever is asked.
/// </summary>
public static class TextSharpness
{
    /// <summary>Text drawn this many units high or more is laid out as drawn; smaller text on whole pixels.</summary>
    public const double LargeText = 20;

    private static bool _registered;

    /// <summary>The layout for text drawn <paramref name="drawnSize"/> units high, after any scale on it.</summary>
    public static TextFormattingMode FormattingFor(double drawnSize) =>
        drawnSize >= LargeText ? TextFormattingMode.Ideal : TextFormattingMode.Display;

    /// <summary>The layout for text under a scale of <paramref name="scale"/>: whole pixels only without one.</summary>
    public static TextFormattingMode FormattingForScale(double scale) =>
        Math.Abs(scale - 1) < 0.001 ? TextFormattingMode.Display : TextFormattingMode.Ideal;

    /// <summary>
    /// The smoothing for a window's text: grey on a background the desktop shows through without a backdrop (as decided
    /// in 0.14.0), WPF's own choice with Mica or Acrylic (as before), ClearType on an opaque one.
    /// </summary>
    public static TextRenderingMode RenderingFor(bool seeThrough, bool backdrop) =>
        seeThrough ? TextRenderingMode.Grayscale : backdrop ? TextRenderingMode.Auto : TextRenderingMode.ClearType;

    /// <summary>
    /// Gives <paramref name="root"/> and what it holds whole-pixel text: Display (Ideal when <paramref name="scaled"/>),
    /// layout rounding, pixel snapping and <paramref name="rendering"/>. Every window the app makes calls it, the bar's and
    /// the dialogs' (through <see cref="DialogTheme.Apply"/>); the bar's background changes the rendering afterwards.
    /// </summary>
    public static void Apply(FrameworkElement root, TextRenderingMode rendering = TextRenderingMode.ClearType, bool scaled = false)
    {
        ArgumentNullException.ThrowIfNull(root);
        TextOptions.SetTextFormattingMode(root, scaled ? TextFormattingMode.Ideal : TextFormattingMode.Display);
        TextOptions.SetTextRenderingMode(root, rendering);
        root.UseLayoutRounding = true;
        root.SnapsToDevicePixels = true;
    }

    /// <summary>
    /// Every context menu and tooltip gets the same when it opens: each is a window of its own that inherits nothing from
    /// the window it opens over, and in a dialog the Fluent theme's own styles come before any of the app's. Menus and
    /// tooltips are opaque, so ClearType. Called once at start; more calls change nothing.
    /// </summary>
    public static void RegisterMenusAndTooltips()
    {
        if (_registered)
        {
            return;
        }

        _registered = true;
        EventManager.RegisterClassHandler(typeof(ContextMenu), ContextMenu.OpenedEvent, new RoutedEventHandler(OnOpened));
        EventManager.RegisterClassHandler(typeof(ToolTip), ToolTip.OpenedEvent, new RoutedEventHandler(OnOpened));
    }

    private static void OnOpened(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement popup && TextOptions.GetTextFormattingMode(popup) != TextFormattingMode.Display)
        {
            Apply(popup);
        }
    }
}
