using DesktopIconGrid.Core.SpecialIcons;

namespace DesktopIconGrid.Core.Tests;

/// <summary>Review 27 B6 and G1: the wheel over the volume tile.</summary>
public class WheelGestureTests
{
    private static readonly Guid Volume = Guid.NewGuid();
    private static readonly DateTime Start = new(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_wheel_turns_a_notch_per_click_and_a_touchpad_only_per_whole_notch()
    {
        var wheel = new WheelGesture();

        Assert.Equal(1, wheel.OverTile(Volume, 120, Start));
        Assert.Equal(2, wheel.OverTile(Volume, 240, Start.AddMilliseconds(50)));
        Assert.Equal(-1, wheel.OverTile(Volume, -120, Start.AddMilliseconds(100)));

        // A swipe of small deltas: one notch per 120 of them, the rest kept for the next event.
        var touchpad = new WheelGesture();
        var notches = Enumerable.Range(0, 30).Sum(i => touchpad.OverTile(Volume, 13, Start.AddMilliseconds(i * 10)) ?? 0);
        Assert.Equal(3, notches); // 390 of delta
    }

    [Fact]
    public void The_rest_of_a_notch_is_dropped_when_the_pointer_leaves_or_the_direction_changes()
    {
        var wheel = new WheelGesture();
        Assert.Equal(0, wheel.OverTile(Volume, 100, Start));
        wheel.Left(Volume);
        Assert.Equal(0, wheel.OverTile(Volume, 100, Start.AddMilliseconds(10))); // not 200

        Assert.Equal(1, wheel.OverTile(Volume, 40, Start.AddMilliseconds(20))); // 100 + 40 = 140
        Assert.Equal(0, wheel.OverTile(Volume, -60, Start.AddMilliseconds(30))); // the other way: the 20 left over is dropped
        Assert.Equal(-1, wheel.OverTile(Volume, -60, Start.AddMilliseconds(40)));
    }

    [Fact]
    public void A_scroll_that_began_elsewhere_on_the_bar_goes_on_scrolling_over_the_tile()
    {
        var wheel = new WheelGesture();
        wheel.Elsewhere(Start); // the bar scrolls

        Assert.Null(wheel.OverTile(Volume, 120, Start.AddMilliseconds(100))); // the tile slid under the pointer
        Assert.Null(wheel.OverTile(Volume, 120, Start.AddMilliseconds(300))); // still the same gesture
        Assert.Equal(1, wheel.OverTile(Volume, 120, Start.AddMilliseconds(800))); // after a pause: the tile's
    }
}
