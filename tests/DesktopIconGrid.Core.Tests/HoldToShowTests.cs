using DesktopIconGrid.Core.Models;

namespace DesktopIconGrid.Core.Tests;

/// <summary>0.15.0 WP2: the key held to show the bar at the pointer.</summary>
public class HoldToShowTests
{
    /// <summary>The window at the pointer, as the hold sees it; its keys are a field the test sets.</summary>
    private sealed class FakeWindow : IHoldWindow
    {
        public bool IsOpen { get; set; }

        public bool IsBusy { get; set; }

        public int Shown { get; private set; }

        public int Hidden { get; private set; }

        public void Show()
        {
            Shown++;
            IsOpen = true;
        }

        public void Hide()
        {
            Hidden++;
            IsOpen = false;
        }
    }

    [Fact]
    public void The_press_shows_and_the_first_key_up_hides()
    {
        var window = new FakeWindow();
        var held = true;
        var hold = new HoldToShow(window, () => held);

        Assert.True(hold.Pressed());
        Assert.True(window.IsOpen);
        Assert.True(hold.IsHolding);
        Assert.True(hold.Tick()); // still held
        Assert.True(hold.Tick());

        held = false; // the main key or one of its modifiers came up
        Assert.False(hold.Tick());
        Assert.False(window.IsOpen);
        Assert.False(hold.IsHolding);
        Assert.Equal((1, 1), (window.Shown, window.Hidden));
        Assert.False(hold.Tick()); // nothing after the end
    }

    [Fact]
    public void A_repeat_while_held_does_nothing_more_and_a_tap_shows_and_hides()
    {
        var window = new FakeWindow();
        var held = true;
        var hold = new HoldToShow(window, () => held);

        hold.Pressed();
        Assert.True(hold.Pressed()); // a repeat
        Assert.Equal(1, window.Shown);

        held = false; // a short tap: the next look hides it
        hold.Tick();
        Assert.False(window.IsOpen);
    }

    [Fact]
    public void A_window_already_open_is_left_alone_by_the_press_and_its_release()
    {
        var window = new FakeWindow { IsOpen = true }; // opened by the show-at-pointer key
        var held = true;
        var hold = new HoldToShow(window, () => held);

        Assert.False(hold.Pressed());
        held = false;
        Assert.False(hold.Tick());
        Assert.True(window.IsOpen);
        Assert.Equal((0, 0), (window.Shown, window.Hidden));
    }

    [Fact]
    public void A_menu_or_a_drag_at_the_release_hides_the_window_when_it_ends()
    {
        var window = new FakeWindow();
        var held = true;
        var hold = new HoldToShow(window, () => held);
        hold.Pressed();

        window.IsBusy = true; // a menu of the window is open
        held = false;
        Assert.True(hold.Tick());
        Assert.True(window.IsOpen);
        Assert.False(hold.IsHolding); // let go: a click now is an ordinary one

        Assert.True(hold.Tick());
        window.IsBusy = false; // the menu closed
        Assert.False(hold.Tick());
        Assert.False(window.IsOpen);
    }

    [Fact]
    public void A_launch_or_the_focus_elsewhere_ends_the_hold_without_a_second_hide()
    {
        var window = new FakeWindow();
        var hold = new HoldToShow(window, () => true);
        hold.Pressed();

        window.IsOpen = false; // a launch hid it, or another window took the focus
        Assert.False(hold.Tick());
        Assert.Equal(0, window.Hidden);

        Assert.True(hold.Pressed()); // the next press is a hold of its own
        Assert.Equal(2, window.Shown);
    }

    [Fact]
    public void The_setting_reads_back_and_is_trimmed_and_none_when_empty()
    {
        var settings = new LauncherSettings { HoldAtPointerHotkey = "  Ctrl+Alt+Q " };
        settings.Normalize();
        Assert.Equal("Ctrl+Alt+Q", settings.HoldAtPointerHotkey);

        var json = System.Text.Json.JsonSerializer.Serialize(settings, Storage.LayoutStorage.JsonOptions);
        using (var document = System.Text.Json.JsonDocument.Parse(json))
        {
            Assert.Equal("Ctrl+Alt+Q", document.RootElement.GetProperty("holdAtPointerHotkey").GetString());
        }

        var copy = new LauncherSettings();
        copy.CopyFrom(System.Text.Json.JsonSerializer.Deserialize<LauncherSettings>(json, Storage.LayoutStorage.JsonOptions)!);
        Assert.Equal("Ctrl+Alt+Q", copy.HoldAtPointerHotkey);

        var empty = new LauncherSettings { HoldAtPointerHotkey = "   " };
        empty.Normalize();
        Assert.Null(empty.HoldAtPointerHotkey);
        Assert.DoesNotContain("holdAtPointerHotkey", System.Text.Json.JsonSerializer.Serialize(new LauncherSettings(), Storage.LayoutStorage.JsonOptions), StringComparison.Ordinal);
    }
}
