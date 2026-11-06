namespace DesktopIconGrid.Core.Models;

/// <summary>How much room an item takes in the bar's flowing grid.</summary>
public enum FlowSlot
{
    /// <summary>One cell: an icon, or an empty gap.</summary>
    Cell,

    /// <summary>A thin divider: <see cref="FlowLayout.NarrowSize"/> wide in a row, or that tall across a single column.</summary>
    Narrow,

    /// <summary>As wide as its content (a caption), at most the row; across a single column, as tall as its content.</summary>
    Auto,
}

/// <summary>A rectangle in the grid's own units.</summary>
public readonly record struct FlowBox(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;

    public double Bottom => Y + Height;
}

/// <summary>What the grid knows about one item before laying it out: its slot and, for <see cref="FlowSlot.Auto"/>, the size its content asks for.</summary>
public readonly record struct FlowEntry(FlowSlot Slot, double ContentWidth = 0, double ContentHeight = 0);

/// <summary>
/// The bar's grid: items in reading order, wrapping to the next row when the next one does not fit, like a wrap
/// panel with a fixed cell size. Dividers and captions are narrower than a cell and stay in the row; a divider never
/// wraps on its own, it takes what is left of its row, and when that is less than <see cref="MinimumHitWidth"/> it
/// takes that much at the row's end, over the end of the last cell, so the next row starts at the edge as it would
/// without it. When the width holds a single cell (a bar docked to the left or right edge), dividers and captions lie
/// across the column instead.
/// </summary>
public static class FlowLayout
{
    /// <summary>The thickness of a divider's slot.</summary>
    public const double NarrowSize = 9;

    /// <summary>
    /// The least room a divider at the end of a full row gets: with none it could not be seen, right-clicked or
    /// dragged, though Tab still landed on it. It is taken inside the row, over the last cell's padding (10 units by
    /// default, at least 2), not past its end: the grid's scroll viewer clips there. Keeping that room free at the
    /// end of every row instead would cost a column wherever the width holds its cells exactly, as the first-run bar
    /// and a group's popup do.
    /// </summary>
    public const double MinimumHitWidth = 6;

    private const double Tolerance = 0.5;

    /// <summary>Places <paramref name="entries"/> in a row width of <paramref name="width"/> (infinite: one row).</summary>
    public static IReadOnlyList<FlowBox> Arrange(IReadOnlyList<FlowEntry> entries, double width, double cellWidth, double cellHeight)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var boxes = new List<FlowBox>(entries.Count);
        var singleColumn = width < 2 * cellWidth - Tolerance;
        var across = double.IsInfinity(width) || double.IsNaN(width) ? cellWidth : Math.Max(width, cellWidth);
        double x = 0;
        double y = 0;
        foreach (var entry in entries)
        {
            if (singleColumn)
            {
                var box = entry.Slot switch
                {
                    FlowSlot.Narrow => new FlowBox(0, y, across, NarrowSize),
                    FlowSlot.Auto => new FlowBox(0, y, across, Math.Max(NarrowSize, entry.ContentHeight)),
                    _ => new FlowBox(0, y, cellWidth, cellHeight),
                };
                boxes.Add(box);
                y = box.Bottom;
                continue;
            }

            var itemWidth = entry.Slot switch
            {
                FlowSlot.Narrow => NarrowSize,
                FlowSlot.Auto => Math.Min(Math.Max(NarrowSize, entry.ContentWidth), width),
                _ => cellWidth,
            };
            if (x > 0 && x + itemWidth > width + Tolerance)
            {
                if (entry.Slot == FlowSlot.Narrow)
                {
                    // At the start of the next row it would divide nothing and push that row, and every one after, out of
                    // line. It stays at the row's end, inside it, with room to be used: drawn after the last cell, it is
                    // over it where they meet. Dividers after it stack there.
                    x = Math.Max(0, Math.Min(x, width - MinimumHitWidth));
                    itemWidth = Math.Max(MinimumHitWidth, width - x);
                }
                else
                {
                    x = 0;
                    y += cellHeight;
                }
            }

            boxes.Add(new FlowBox(x, y, itemWidth, cellHeight));
            x += itemWidth;
        }

        return boxes;
    }

    /// <summary>The size the boxes take together.</summary>
    public static (double Width, double Height) Extent(IReadOnlyList<FlowBox> boxes)
    {
        ArgumentNullException.ThrowIfNull(boxes);
        return boxes.Count == 0 ? (0, 0) : (boxes.Max(b => b.Right), boxes.Max(b => b.Bottom));
    }
}
