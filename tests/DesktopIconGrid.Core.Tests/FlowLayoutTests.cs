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

        Assert.Equal(new FlowBox(3 * W, 0, FlowLayout.MinimumHitWidth, H), boxes[3]); // what is left of the row, at least the hit width
        Assert.Equal([new FlowBox(0, H, W, H), new FlowBox(W, H, W, H), new FlowBox(2 * W, H, W, H)], boxes.Skip(4).Take(3));
        Assert.Equal(new FlowBox(0, 2 * H, W, H), boxes[7]);
        Assert.Equal((3 * W + FlowLayout.MinimumHitWidth, 3 * H), FlowLayout.Extent(boxes)); // the panel caps it at its width
    }

    [Fact]
    public void A_divider_in_a_row_with_no_room_left_reaches_into_the_margin_so_it_can_still_be_used()
    {
        // The review: with no width it could not be seen, right-clicked or dragged, and Tab landed on it unseen.
        var boxes = FlowLayout.Arrange([Cell, Cell, Divider, Divider, Cell], 2 * W, W, H);

        Assert.Equal(new FlowBox(2 * W, 0, FlowLayout.MinimumHitWidth, H), boxes[2]);
        Assert.Equal(new FlowBox(2 * W, 0, FlowLayout.MinimumHitWidth, H), boxes[3]); // stacked there, never further out
        Assert.Equal(new FlowBox(0, H, W, H), boxes[4]);
        Assert.Equal((2 * W + FlowLayout.MinimumHitWidth, 2 * H), FlowLayout.Extent(boxes));
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
