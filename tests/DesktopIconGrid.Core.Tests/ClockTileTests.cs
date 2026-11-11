using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Core.SpecialIcons;

namespace DesktopIconGrid.Core.Tests;

/// <summary>The clock tile: its settings and its timer.</summary>
public class ClockTileTests
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Settings_round_trip_and_an_unknown_zone_is_this_PCs()
    {
        var item = new SpecialIconItem { Kind = SpecialIconKinds.Clock };
        new ClockTileSettings("UTC", ClockTimerKind.Pomodoro, 0, 99999).ApplyTo(item);

        var read = ClockTileSettings.From(item);

        Assert.Equal(("UTC", ClockTimerKind.Pomodoro, 1, ClockTileSettings.MaxMinutes), (read.TimeZoneId, read.Timer, read.Minutes, read.BreakMinutes));
        Assert.Equal(TimeSpan.Zero, read.Zone().BaseUtcOffset);
        Assert.Equal(TimeZoneInfo.Local, (read with { TimeZoneId = "No/Such_Zone" }).Zone());
        Assert.Equal(TimeZoneInfo.Local, ClockTileSettings.Default.Zone());
        Assert.Equal(ClockTimerKind.None, ClockTileSettings.From(new SpecialIconItem()).Timer);
    }

    [Fact]
    public void A_tile_without_a_timer_shows_the_time_in_its_zone_and_ignores_clicks()
    {
        var timer = new ClockTimer(new ClockTileSettings("UTC", ClockTimerKind.None, 25, 5));

        timer.Click(Noon);

        Assert.Equal(ClockPhase.Idle, timer.Phase);
        Assert.Equal("12:00", timer.Label(Noon));
        Assert.Equal("09:05", timer.Label(new DateTimeOffset(2026, 11, 10, 11, 5, 0, TimeSpan.FromHours(2)))); // 09:05 UTC
    }

    [Fact]
    public void A_countdown_runs_to_zero_once_and_a_click_brings_the_time_back()
    {
        var timer = new ClockTimer(new ClockTileSettings("UTC", ClockTimerKind.Countdown, 90, 5));

        timer.Click(Noon);
        Assert.Equal("1:30:00", timer.Label(Noon));
        Assert.Null(timer.Tick(Noon.AddMinutes(89)));
        Assert.Equal("0:01", timer.Label(Noon.AddMinutes(90).AddSeconds(-0.5)));
        Assert.Equal(ClockEvent.CountdownEnded, timer.Tick(Noon.AddMinutes(90)));
        Assert.Equal(ClockPhase.Done, timer.Phase);
        Assert.Equal("0:00", timer.Label(Noon.AddHours(5)));
        Assert.Null(timer.Tick(Noon.AddHours(5))); // told once

        timer.Click(Noon.AddHours(5));
        Assert.Equal(ClockPhase.Idle, timer.Phase);
        Assert.Equal("17:00", timer.Label(Noon.AddHours(5)));
    }

    [Fact]
    public void A_pomodoro_alternates_until_clicked_and_does_not_race_after_a_sleep()
    {
        var timer = new ClockTimer(new ClockTileSettings(null, ClockTimerKind.Pomodoro, 25, 5));

        timer.Click(Noon);
        Assert.Equal("25:00", timer.Label(Noon));
        Assert.Equal(ClockEvent.BreakStarted, timer.Tick(Noon.AddMinutes(25)));
        Assert.Equal("5:00", timer.Label(Noon.AddMinutes(25)));

        var woke = Noon.AddHours(3); // the PC slept through several phases: one step, counted from now
        Assert.Equal(ClockEvent.WorkStarted, timer.Tick(woke));
        Assert.Null(timer.Tick(woke.AddMinutes(1)));
        Assert.Equal("24:00", timer.Label(woke.AddMinutes(1)));

        timer.Click(woke.AddMinutes(2)); // stop
        Assert.Equal(ClockPhase.Idle, timer.Phase);
        Assert.Null(timer.Tick(woke.AddHours(1)));
    }

    [Theory]
    [InlineData("\\")] // throws NullReferenceException inside the runtime on .NET 10 (review 24 R2)
    [InlineData("\\\\")]
    [InlineData("../../etc/passwd")]
    [InlineData("\0")]
    public void A_zone_id_the_runtime_chokes_on_is_this_PCs_time_and_never_an_exception(string id)
    {
        var settings = new ClockTileSettings(id, ClockTimerKind.None, 25, 5);

        Assert.Equal(TimeZoneInfo.Local, settings.Zone());
        Assert.Matches(@"^\d\d:\d\d$", new ClockTimer(settings).Label(Noon));
    }
}
