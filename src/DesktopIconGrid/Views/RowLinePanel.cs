using System.Windows;
using System.Windows.Controls;

namespace DesktopIconGrid.Views;

/// <summary>
/// The inside of a row line (0.14.0): its caption, when it has one, at the left after <see cref="Inset"/>, then
/// <see cref="Gap"/>, then the line to <see cref="Inset"/> before the right edge (標題 ────────). A caption too long for
/// the strip is cut with an ellipsis so the line keeps at least <see cref="MinLine"/>; in a strip too narrow for that
/// to leave the caption half of it (a bar one icon wide), the caption may take the strip up to the insets, as a
/// label's does, and the line what is left. The first child is the caption, the second the line; without a caption
/// shown the line takes the strip, inset at both ends.
/// </summary>
public sealed class RowLinePanel : Panel
{
    public const double Inset = 6;

    public const double Gap = 6;

    public const double MinLine = 24;

    private UIElement? Caption => InternalChildren.Count > 0 && InternalChildren[0].Visibility == Visibility.Visible ? InternalChildren[0] : null;

    private UIElement? Line => InternalChildren.Count > 1 ? InternalChildren[1] : null;

    /// <summary>The room the caption may take in a strip <paramref name="width"/> wide.</summary>
    internal static double CaptionRoom(double width)
    {
        if (double.IsInfinity(width))
        {
            return double.PositiveInfinity;
        }

        var besideLine = width - Inset - Gap - MinLine - Inset;
        return Math.Max(0, besideLine >= width / 2 ? besideLine : width - (2 * Inset));
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = 0, height = 0;
        if (Caption is { } caption)
        {
            caption.Measure(new Size(CaptionRoom(availableSize.Width), double.PositiveInfinity));
            width = caption.DesiredSize.Width + Gap;
            height = caption.DesiredSize.Height;
        }

        Line?.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return new Size(width + MinLine + (2 * Inset), height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var start = Inset;
        if (Caption is { } caption)
        {
            var room = CaptionRoom(finalSize.Width);
            if (caption.DesiredSize.Width > room)
            {
                caption.Measure(new Size(room, double.PositiveInfinity)); // so it ends in an ellipsis rather than being clipped
            }

            var captionWidth = Math.Min(caption.DesiredSize.Width, room);
            var captionHeight = caption.DesiredSize.Height;
            caption.Arrange(new Rect(Inset, Math.Max(0, (finalSize.Height - captionHeight) / 2), captionWidth, captionHeight));
            start = Inset + captionWidth + Gap;
        }

        Line?.Arrange(new Rect(start, 0, Math.Max(0, finalSize.Width - start - Inset), finalSize.Height));
        return finalSize;
    }
}
