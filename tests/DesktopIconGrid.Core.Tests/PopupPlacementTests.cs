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
}
