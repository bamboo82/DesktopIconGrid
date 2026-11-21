using DesktopIconGrid.Core.Models;

namespace DesktopIconGrid.Core.Tests;

/// <summary>The bar's grid with dividers and captions among the icons.</summary>
public class FlowLayoutTests
{
    private const double W = 50;
    private const double H = 60;

    private static readonly FlowEntry Cell = new(FlowSlot.Cell);
    private static readonly FlowEntry Divider = new(FlowSlot.Narrow);

    private static FlowEntry Caption(double width, double height = 14) => new(FlowSlot.Auto, width, height);

    private static readonly FlowEntry RowLine = new(FlowSlot.Row);

    private static readonly FlowEntry RowHead = new(FlowSlot.RowHead);

    private const double N = FlowLayout.NarrowSize;

    // ----- 0.14.0: a line that starts a new row, and one that is the first thing of a new row -----------------------

    [Fact]
    public void A_row_line_ends_its_row_where_it_is_and_lies_across_the_panel()
    {
        // Plan 35: [C C Row C] at a width of 5 cells; the third cell starts a new row under the line.
        var boxes = FlowLayout.Arrange([Cell, Cell, RowLine, Cell], 5 * W, W, H);

        Assert.Equal([new FlowBox(0, 0, W, H), new FlowBox(W, 0, W, H), new FlowBox(0, H, 5 * W, N), new FlowBox(0, H + N, W, H)], boxes);
        Assert.Equal((5 * W, (2 * H) + N), FlowLayout.Extent(boxes));
    }

    [Fact]
    public void A_row_line_first_after_a_full_row_and_twice_makes_no_empty_row()
    {
        Assert.Equal([new FlowBox(0, 0, 3 * W, N), new FlowBox(0, N, W, H)], FlowLayout.Arrange([RowLine, Cell], 3 * W, W, H));

        // After an exactly full row: right under it, not a row lower.
        var full = FlowLayout.Arrange([Cell, Cell, Cell, RowLine, Cell], 3 * W, W, H);
        Assert.Equal(new FlowBox(0, H, 3 * W, N), full[3]);
        Assert.Equal(new FlowBox(0, H + N, W, H), full[4]);

        // Two in a row: two strips, one under the other.
        var two = FlowLayout.Arrange([Cell, RowLine, RowLine, Cell], 3 * W, W, H);
        Assert.Equal([new FlowBox(0, H, 3 * W, N), new FlowBox(0, H + N, 3 * W, N)], two.Skip(1).Take(2));
        Assert.Equal(new FlowBox(0, H + (2 * N), W, H), two[3]);
    }

    [Fact]
    public void A_row_line_with_a_caption_is_as_tall_as_its_caption()
    {
        var boxes = FlowLayout.Arrange([Cell, new FlowEntry(FlowSlot.Row, ContentHeight: 20), Cell], 3 * W, W, H);

        Assert.Equal(new FlowBox(0, H, 3 * W, 20), boxes[1]);
        Assert.Equal(new FlowBox(0, H + 20, W, H), boxes[2]);
        Assert.Equal(new FlowBox(0, H, 3 * W, N), FlowLayout.Arrange([Cell, new FlowEntry(FlowSlot.Row, ContentHeight: 4), Cell], 3 * W, W, H)[1]); // never under 9
    }

    [Fact]
    public void Measured_in_one_row_a_row_line_still_breaks_and_is_as_wide_as_the_widest_row()
    {
        var boxes = FlowLayout.Arrange([Cell, Cell, Cell, RowLine, Cell, RowHead, Cell], double.PositiveInfinity, W, H);

        Assert.Equal(new FlowBox(0, H, 3 * W, N), boxes[3]); // finite: the widest row's extent
        Assert.Equal(new FlowBox(0, H + N, W, H), boxes[4]);
        Assert.Equal(new FlowBox(0, (2 * H) + N, N, H), boxes[5]); // a row-start line breaks too
        Assert.Equal(new FlowBox(N, (2 * H) + N, W, H), boxes[6]);
        Assert.Equal((3 * W, (3 * H) + N), FlowLayout.Extent(boxes));
        Assert.Equal(W, FlowLayout.Arrange([RowLine], double.PositiveInfinity, W, H)[0].Width); // nothing else: a cell's width
    }

    [Fact]
    public void In_a_single_column_both_lie_across_it_as_a_divider_does()
    {
        var boxes = FlowLayout.Arrange([Cell, RowLine, RowHead, new FlowEntry(FlowSlot.Row, ContentHeight: 20), Cell], W, W, H);

        Assert.Equal([new FlowBox(0, H, W, N), new FlowBox(0, H + N, W, N), new FlowBox(0, H + (2 * N), W, 20)], boxes.Skip(1).Take(3));
        Assert.Equal(new FlowBox(0, H + (2 * N) + 20, W, H), boxes[4]);
    }

    [Fact]
    public void A_row_start_line_ends_its_row_and_the_items_after_it_follow_it()
    {
        // Plan 35 A2: [C C RowHead C] → the line at (0, cellH), the cell at (9, cellH).
        var boxes = FlowLayout.Arrange([Cell, Cell, RowHead, Cell], 5 * W, W, H);
        Assert.Equal([new FlowBox(0, H, N, H), new FlowBox(N, H, W, H)], boxes.Skip(2));

        // First: no empty row. After an exactly full row: the next row, not one lower.
        Assert.Equal([new FlowBox(0, 0, N, H), new FlowBox(N, 0, W, H)], FlowLayout.Arrange([RowHead, Cell], 3 * W, W, H));
        var full = FlowLayout.Arrange([Cell, Cell, Cell, RowHead, Cell], 3 * W, W, H);
        Assert.Equal([new FlowBox(0, H, N, H), new FlowBox(N, H, W, H)], full.Skip(3));

        // Its row wraps as usual, and the wrapped row starts at the edge.
        var wrapped = FlowLayout.Arrange([RowHead, Cell, Cell, Cell], 3 * W, W, H);
        Assert.Equal([new FlowBox(N, 0, W, H), new FlowBox(N + W, 0, W, H), new FlowBox(0, H, W, H)], wrapped.Skip(1));
    }

    [Fact]
    public void Icons_wrap_like_a_wrap_panel()
    {
        var boxes = FlowLayout.Arrange([Cell, Cell, Cell, Cell, Cell], 3 * W, W, H);

        Assert.Equal(
            [new FlowBox(0, 0, W, H), new FlowBox(W, 0, W, H), new FlowBox(2 * W, 0, W, H), new FlowBox(0, H, W, H), new FlowBox(W, H, W, H)],
            boxes);
        Assert.Equal((3 * W, 2 * H), FlowLayout.Extent(boxes));
    }

    [Fact]
    public void A_divider_and_a_caption_take_less_than_a_cell_and_stay_in_the_row()
    {
        var boxes = FlowLayout.Arrange([Cell, Divider, Caption(30), Cell, Cell], 3 * W, W, H);

        Assert.Equal(new FlowBox(W, 0, FlowLayout.NarrowSize, H), boxes[1]);
        Assert.Equal(new FlowBox(W + FlowLayout.NarrowSize, 0, 30, H), boxes[2]);
        Assert.Equal(new FlowBox(W + FlowLayout.NarrowSize + 30, 0, W, H), boxes[3]); // 109 + 50 still fits in 150
        Assert.Equal(new FlowBox(0, H, W, H), boxes[4]);
    }

    [Fact]
    public void A_divider_after_the_last_icon_of_a_full_row_stays_there_and_the_next_row_is_unchanged()
    {
        // 0.7.0 wrapped it like a cell: it divided nothing at the start of row 2 and pushed that row 9 units in,
        // so its last icon wrapped and every later row was off by one.
        var boxes = FlowLayout.Arrange([Cell, Cell, Cell, Divider, Cell, Cell, Cell, Cell], 3 * W + 4, W, H);

        // What is left of the row is 4 units: it takes the hit width at the row's end, over the last cell's padding.
        Assert.Equal(new FlowBox(3 * W + 4 - FlowLayout.MinimumHitWidth, 0, FlowLayout.MinimumHitWidth, H), boxes[3]);
        Assert.Equal([new FlowBox(0, H, W, H), new FlowBox(W, H, W, H), new FlowBox(2 * W, H, W, H)], boxes.Skip(4).Take(3));
        Assert.Equal(new FlowBox(0, 2 * H, W, H), boxes[7]);
        Assert.Equal((3 * W + 4, 3 * H), FlowLayout.Extent(boxes)); // nothing past the width, which the scroll viewer would clip
    }

    [Fact]
    public void A_divider_in_a_row_with_no_room_left_takes_the_end_of_the_row_so_it_can_still_be_used()
    {
        // Review 11: with no width it could not be seen, right-clicked or dragged, and Tab landed on it unseen. Review 15
        // UI2: the room it then took past the row's end was clipped by the scroll viewer; it is now inside the row.
        var boxes = FlowLayout.Arrange([Cell, Cell, Divider, Divider, Cell], 2 * W, W, H);

        Assert.Equal(new FlowBox(2 * W - FlowLayout.MinimumHitWidth, 0, FlowLayout.MinimumHitWidth, H), boxes[2]);
        Assert.Equal(boxes[2], boxes[3]); // stacked there
        Assert.Equal(new FlowBox(0, H, W, H), boxes[4]);
        Assert.Equal((2 * W, 2 * H), FlowLayout.Extent(boxes));
    }

    [Fact]
    public void A_caption_that_does_not_fit_still_wraps()
    {
        var boxes = FlowLayout.Arrange([Cell, Cell, Caption(30), Cell], 2 * W + 4, W, H);

        Assert.Equal(new FlowBox(0, H, 30, H), boxes[2]);
        Assert.Equal(new FlowBox(30, H, W, H), boxes[3]);
    }

    [Fact]
    public void A_caption_is_never_wider_than_the_row_nor_thinner_than_a_divider()
    {
        Assert.Equal(new FlowBox(0, 0, 3 * W, H), FlowLayout.Arrange([Caption(500)], 3 * W, W, H)[0]);
        Assert.Equal(new FlowBox(0, 0, FlowLayout.NarrowSize, H), FlowLayout.Arrange([Caption(0)], 3 * W, W, H)[0]);
    }

    [Fact]
    public void In_a_single_column_dividers_and_captions_lie_across_it()
    {
        var boxes = FlowLayout.Arrange([Cell, Divider, Caption(30, 16), Cell], W + 10, W, H);

        Assert.Equal(
            [new FlowBox(0, 0, W, H), new FlowBox(0, H, W + 10, FlowLayout.NarrowSize), new FlowBox(0, H + FlowLayout.NarrowSize, W + 10, 16), new FlowBox(0, H + FlowLayout.NarrowSize + 16, W, H)],
            boxes);
    }

    [Fact]
    public void Without_a_width_everything_is_one_row()
    {
        var boxes = FlowLayout.Arrange([Cell, Divider, Cell], double.PositiveInfinity, W, H);

        Assert.Equal([new FlowBox(0, 0, W, H), new FlowBox(W, 0, FlowLayout.NarrowSize, H), new FlowBox(W + FlowLayout.NarrowSize, 0, W, H)], boxes);
        Assert.Equal((0, 0), FlowLayout.Extent([]));
    }
}
