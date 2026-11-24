using DesktopIconGrid.Core.Models;

namespace DesktopIconGrid.Core.Tests;

/// <summary>The Show-at-pointer window: under the mouse, never off the screen.</summary>
public class PopupPlacementTests
{
    private static readonly PixelRect Work = new(0, 0, 1920, 1040);

    [Fact]
    public void Opens_centred_on_the_pointer_with_the_pointer_just_inside_the_top_edge()
    {
        Assert.Equal(new PixelRect(400, 388, 200, 100), PopupPlacement.AtPointer(500, 400, 200, 100, Work, grabOffset: 12));
    }

    [Fact]
    public void Moves_back_inside_the_work_area_at_every_edge()
    {
        Assert.Equal(new PixelRect(1720, 940, 200, 100), PopupPlacement.AtPointer(1910, 1030, 200, 100, Work, grabOffset: 12));
        Assert.Equal(new PixelRect(0, 0, 200, 100), PopupPlacement.AtPointer(5, 3, 200, 100, Work, grabOffset: 12));

        var leftMonitor = new PixelRect(-1280, -200, 1280, 1000); // left of and above the primary
        Assert.Equal(new PixelRect(-1280, -200, 200, 100), PopupPlacement.AtPointer(-1275, -195, 200, 100, leftMonitor, grabOffset: 12));
        Assert.Equal(new PixelRect(-200, 700, 200, 100), PopupPlacement.AtPointer(-2, 799, 200, 100, leftMonitor, grabOffset: 12));
    }

    [Fact]
    public void A_window_bigger_than_the_work_area_is_cut_to_it()
    {
        Assert.Equal(Work, PopupPlacement.AtPointer(10, 10, 3000, 2000, Work));
    }

    [Fact]
    public void Without_a_work_area_it_stays_on_the_pointer()
    {
        Assert.Equal(new PixelRect(400, 400, 200, 100), PopupPlacement.AtPointer(500, 400, 200, 100, default));
        Assert.Equal(new PixelRect(500, 400, 1, 1), PopupPlacement.AtPointer(500, 400, 0, -5, default));
    }

    [Theory]
    [InlineData(0, 0, 500, 400)] // the top left corner on the pointer
    [InlineData(50, 0, 400, 400)] // the middle of the top edge
    [InlineData(100, 0, 300, 400)]
    [InlineData(0, 50, 500, 350)]
    [InlineData(50, 50, 400, 350)] // the centre
    [InlineData(100, 50, 300, 350)]
    [InlineData(0, 100, 500, 300)]
    [InlineData(50, 100, 400, 300)]
    [InlineData(100, 100, 300, 300)] // the bottom right corner
    public void A_chosen_point_of_the_window_goes_under_the_pointer_and_the_grab_offset_is_not_used(int x, int y, int left, int top)
    {
        // 0.16.0 WP2: the nine named points of a 200 × 100 window with the pointer at (500, 400), well inside the work area.
        var anchor = new PointerAnchor(x, y);
        Assert.Equal(new PixelRect(left, top, 200, 100), PopupPlacement.AtPointer(500, 400, 200, 100, Work, grabOffset: 12, anchor: anchor));
        Assert.Equal(new PixelRect(left, top, 200, 100), PopupPlacement.AtPointer(500, 400, 200, 100, default, anchor: anchor)); // none known: as is
    }

    [Fact]
    public void An_anchor_in_whole_pixels_centres_an_odd_width_as_the_default_does()
    {
        Assert.Equal(PopupPlacement.AtPointer(500, 400, 201, 101, Work).Left, PopupPlacement.AtPointer(500, 400, 201, 101, Work, anchor: new PointerAnchor(50, 0)).Left);
        Assert.Equal(new PixelRect(494, 396, 21, 11), PopupPlacement.AtPointer(500, 400, 21, 11, Work, anchor: new PointerAnchor(33, 40))); // 21 × 33 / 100 = 6.93 → 6
    }

    [Fact]
    public void A_corner_anchor_near_a_corner_of_the_work_area_is_moved_back_inside()
    {
        // The top left corner on a pointer near the bottom right corner, and the reverse.
        Assert.Equal(new PixelRect(1720, 940, 200, 100), PopupPlacement.AtPointer(1910, 1030, 200, 100, Work, anchor: new PointerAnchor(0, 0)));
        Assert.Equal(new PixelRect(0, 0, 200, 100), PopupPlacement.AtPointer(5, 3, 200, 100, Work, anchor: new PointerAnchor(100, 100)));

        var leftMonitor = new PixelRect(-1280, -200, 1280, 1000);
        Assert.Equal(new PixelRect(-1280, -200, 200, 100), PopupPlacement.AtPointer(-1275, -195, 200, 100, leftMonitor, anchor: new PointerAnchor(100, 100)));
        Assert.Equal(new PixelRect(-200, 700, 200, 100), PopupPlacement.AtPointer(-2, 799, 200, 100, leftMonitor, anchor: new PointerAnchor(0, 0)));
    }

    [Fact]
    public void With_an_anchor_a_window_bigger_than_the_work_area_is_cut_to_it_too()
    {
        Assert.Equal(Work, PopupPlacement.AtPointer(10, 10, 3000, 2000, Work, anchor: new PointerAnchor(100, 100)));
        Assert.Equal(Work, PopupPlacement.AtPointer(1900, 1000, 3000, 2000, Work, anchor: new PointerAnchor(0, 0)));
    }

    [Fact]
    public void An_anchor_out_of_range_places_the_window_as_without_one()
    {
        Assert.Equal(PopupPlacement.AtPointer(500, 400, 200, 100, Work, grabOffset: 12), PopupPlacement.AtPointer(500, 400, 200, 100, Work, grabOffset: 12, anchor: new PointerAnchor(150, 0)));
        Assert.Equal(PopupPlacement.AtPointer(500, 400, 200, 100, Work, grabOffset: 12), PopupPlacement.AtPointer(500, 400, 200, 100, Work, grabOffset: 12, anchor: new PointerAnchor(50, -1)));
    }
}
