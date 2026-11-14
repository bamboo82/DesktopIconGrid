using System.Globalization;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Core.SpecialIcons;
using DesktopIconGrid.Infrastructure;
using DesktopIconGrid.Resources;

namespace DesktopIconGrid.ViewModels;

/// <param name="Id"><see langword="null"/> for this PC's time.</param>
public sealed record ZoneChoice(string? Id, string Label);

public sealed record ClockTimerChoice(ClockTimerKind Kind, string Label);

/// <summary>Backs the properties dialog of a clock tile.</summary>
public sealed class ClockTileViewModel : ObservableObject
{
    public static readonly IReadOnlyList<ClockTimerChoice> TimerChoices =
    [
        new(ClockTimerKind.None, Strings.Clock_TimerNone),
        new(ClockTimerKind.Countdown, Strings.Clock_TimerCountdown),
        new(ClockTimerKind.Pomodoro, Strings.Clock_TimerPomodoro),
    ];

    private string _name;
    private ZoneChoice _zone;
    private ClockTimerChoice _timer;
    private string _minutes;
    private string _breakMinutes;

    public ClockTileViewModel(SpecialIconItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var settings = ClockTileSettings.From(item);
        _name = item.Name;
        Zones = [new ZoneChoice(null, Strings.Clock_ThisPc), .. TimeZoneInfo.GetSystemTimeZones().Select(zone => new ZoneChoice(zone.Id, zone.DisplayName))];
        var id = settings.TimeZoneId?.Trim();
        if (!string.IsNullOrEmpty(id)
            && !Zones.Any(zone => string.Equals(zone.Id, id, StringComparison.OrdinalIgnoreCase))
            && CommandTileSettings.IsShowable(id)
            && id.Length <= 100)
        {
            // An id the list lacks, kept as a choice so that OK here does not overwrite it: a zone another PC knows and
            // this one does not (the tile shows this PC's time meanwhile), or one this PC resolves without listing it, such
            // as an IANA id ("Asia/Tokyo", review 27).
            var known = settings.KnownZone();
            Zones = [.. Zones, new ZoneChoice(id, known is null ? string.Format(Strings.Clock_UnknownZone, id) : $"{known.DisplayName} ({id})")];
        }

        _zone = Zones.FirstOrDefault(zone => string.Equals(zone.Id, id, StringComparison.OrdinalIgnoreCase)) ?? Zones[0];
        _timer = TimerChoices.First(choice => choice.Kind == settings.Timer);
        _minutes = settings.Minutes.ToString(CultureInfo.CurrentCulture);
        _breakMinutes = settings.BreakMinutes.ToString(CultureInfo.CurrentCulture);
    }

    public IReadOnlyList<ZoneChoice> Zones { get; }

    public IReadOnlyList<ClockTimerChoice> Timers => TimerChoices;

    public string Name { get => _name; set => Set(ref _name, value); }

    public string Minutes { get => _minutes; set => Set(ref _minutes, value); }

    public string BreakMinutes { get => _breakMinutes; set => Set(ref _breakMinutes, value); }

    public ZoneChoice SelectedZone
    {
        get => _zone;
        set
        {
            if (value is not null)
            {
                SetProperty(ref _zone, value);
            }
        }
    }

    public ClockTimerChoice SelectedTimer
    {
        get => _timer;
        set
        {
            if (value is not null && SetProperty(ref _timer, value))
            {
                OnPropertyChanged(nameof(HasTimer));
                OnPropertyChanged(nameof(IsPomodoro));
                OnPropertyChanged(nameof(CanSave));
            }
        }
    }

    public bool HasTimer => _timer.Kind != ClockTimerKind.None;

    public bool IsPomodoro => _timer.Kind == ClockTimerKind.Pomodoro;

    public bool CanSave => _name.Trim().Length > 0 && (!HasTimer || Read(_minutes) is not null) && (!IsPomodoro || Read(_breakMinutes) is not null);

    public void Apply(SpecialIconItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        item.Name = _name.Trim();
        new ClockTileSettings(_zone.Id, _timer.Kind, Read(_minutes) ?? ClockTileSettings.Default.Minutes, Read(_breakMinutes) ?? ClockTileSettings.Default.BreakMinutes).ApplyTo(item);
    }

    private static int? Read(string text) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var value) && value is >= 1 and <= ClockTileSettings.MaxMinutes ? value : null;

    private void Set(ref string field, string? value, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (SetProperty(ref field, value ?? string.Empty, name))
        {
            OnPropertyChanged(nameof(CanSave));
        }
    }
}
