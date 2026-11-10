using System.Globalization;
using DesktopIconGrid.Core.Models;

namespace DesktopIconGrid.Core.SpecialIcons;

/// <summary>What a click on a clock tile starts.</summary>
public enum ClockTimerKind
{
    None,

    /// <summary>One countdown of <see cref="ClockTileSettings.Minutes"/>.</summary>
    Countdown,

    /// <summary>Work for <see cref="ClockTileSettings.Minutes"/>, a break of <see cref="ClockTileSettings.BreakMinutes"/>, and again, until clicked.</summary>
    Pomodoro,
}

/// <summary>Where a clock tile's timer is.</summary>
public enum ClockPhase
{
    /// <summary>Shows the time.</summary>
    Idle,
    Running,
    Break,

    /// <summary>A countdown ended; shown until the next click.</summary>
    Done,
}

/// <summary>What a tick of the timer reached, for a notification.</summary>
public enum ClockEvent
{
    CountdownEnded,
    BreakStarted,
    WorkStarted,
}

/// <summary>
/// A clock tile (<see cref="SpecialIconKinds.Clock"/>): the time here or in another zone, and a countdown or a
/// Pomodoro started by a click. Kept in <see cref="SpecialIconItem.Settings"/>.
/// </summary>
/// <param name="TimeZoneId">A system time zone id; <see langword="null"/> is this PC's time.</param>
public sealed record ClockTileSettings(string? TimeZoneId, ClockTimerKind Timer, int Minutes, int BreakMinutes)
{
    public const string ZoneKey = "timeZone";
    public const string TimerKey = "timer";
    public const string MinutesKey = "minutes";
    public const string BreakKey = "breakMinutes";

    public const int MaxMinutes = 24 * 60;

    public static ClockTileSettings Default { get; } = new(null, ClockTimerKind.None, 25, 5);

    public static ClockTileSettings From(SpecialIconItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return new ClockTileSettings(
            item.GetSetting(ZoneKey),
            Enum.TryParse<ClockTimerKind>(item.GetSetting(TimerKey), ignoreCase: true, out var timer) && Enum.IsDefined(timer) ? timer : ClockTimerKind.None,
            ReadMinutes(item.GetSetting(MinutesKey), Default.Minutes),
            ReadMinutes(item.GetSetting(BreakKey), Default.BreakMinutes));
    }

    public void ApplyTo(SpecialIconItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        item.SetSetting(ZoneKey, TimeZoneId);
        item.SetSetting(TimerKey, Timer.ToString().ToLowerInvariant());
        item.SetSetting(MinutesKey, Math.Clamp(Minutes, 1, MaxMinutes).ToString(CultureInfo.InvariantCulture));
        item.SetSetting(BreakKey, Math.Clamp(BreakMinutes, 1, MaxMinutes).ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>The zone, or this PC's when none is set or the id is not known on this PC (ids differ between systems).</summary>
    public TimeZoneInfo Zone()
    {
        if (string.IsNullOrWhiteSpace(TimeZoneId))
        {
            return TimeZoneInfo.Local;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId.Trim());
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.Local;
        }
    }

    private static int ReadMinutes(string? text, int fallback) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? Math.Clamp(value, 1, MaxMinutes) : fallback;
}

/// <summary>The timer of one clock tile: started and stopped by clicks, moved on by ticks. Time is passed in, so it can be tested.</summary>
public sealed class ClockTimer(ClockTileSettings settings)
{
    private DateTimeOffset _end;

    public ClockPhase Phase { get; private set; }

    /// <summary>A click: starts the timer when idle (if the tile has one), otherwise stops it and shows the time again.</summary>
    public void Click(DateTimeOffset now)
    {
        if (Phase != ClockPhase.Idle)
        {
            Phase = ClockPhase.Idle;
            return;
        }

        if (settings.Timer != ClockTimerKind.None)
        {
            Phase = ClockPhase.Running;
            _end = now.AddMinutes(settings.Minutes);
        }
    }

    /// <summary>Moves the timer on to <paramref name="now"/>; what it reached, if anything.</summary>
    public ClockEvent? Tick(DateTimeOffset now)
    {
        if (Phase is not (ClockPhase.Running or ClockPhase.Break) || now < _end)
        {
            return null;
        }

        if (settings.Timer == ClockTimerKind.Countdown)
        {
            Phase = ClockPhase.Done;
            return ClockEvent.CountdownEnded;
        }

        var wasWork = Phase == ClockPhase.Running;
        Phase = wasWork ? ClockPhase.Break : ClockPhase.Running;
        _end = now.AddMinutes(wasWork ? settings.BreakMinutes : settings.Minutes); // from now: a PC that slept does not race through phases
        return wasWork ? ClockEvent.BreakStarted : ClockEvent.WorkStarted;
    }

    /// <summary>The tile's text: the time in the zone (<c>14:05</c>), what is left (<c>24:59</c>, <c>1:05:00</c>), or <c>0:00</c> when done.</summary>
    public string Label(DateTimeOffset now)
    {
        switch (Phase)
        {
            case ClockPhase.Idle:
                return TimeZoneInfo.ConvertTime(now, settings.Zone()).ToString("HH:mm", CultureInfo.InvariantCulture);
            case ClockPhase.Done:
                return "0:00";
            default:
                var left = _end - now;
                if (left < TimeSpan.Zero)
                {
                    left = TimeSpan.Zero;
                }

                var seconds = (long)Math.Ceiling(left.TotalSeconds);
                return seconds >= 3600
                    ? $"{seconds / 3600}:{seconds % 3600 / 60:00}:{seconds % 60:00}"
                    : $"{seconds / 60}:{seconds % 60:00}";
        }
    }
}
