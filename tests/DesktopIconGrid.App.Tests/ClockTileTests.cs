using System.Windows.Threading;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Core.SpecialIcons;
using DesktopIconGrid.SpecialIcons;
using DesktopIconGrid.ViewModels;

namespace DesktopIconGrid.App.Tests;

/// <summary>The clock tile on the bar and its properties dialog.</summary>
public class ClockTileTests
{
    [Fact]
    public void A_click_starts_the_countdown_and_another_brings_the_time_back()
    {
        var handler = new ClockTileHandler((_, _) => { }, () => null, Dispatcher.CurrentDispatcher);
        var item = handler.CreateItem();
        (ClockTileSettings.Default with { Timer = ClockTimerKind.Countdown, Minutes = 10 }).ApplyTo(item);
        var state = handler.CreateState(item)!;
        try
        {
            Assert.Matches(@"^\d\d:\d\d$", state.LabelText!); // the time
            Assert.Equal(CommandLevel.Unknown, state.Level);

            handler.Activate(item, ActivationModifiers.None);
            Assert.Contains(state.LabelText, new[] { "10:00", "9:59" });
            Assert.Equal(CommandLevel.Ok, state.Level);

            handler.Activate(item, ActivationModifiers.None);
            Assert.Matches(@"^\d\d:\d\d$", state.LabelText!);
            Assert.Equal(CommandLevel.Unknown, state.Level);
        }
        finally
        {
            state.Detach();
        }
    }

    [Fact]
    public void The_dialog_keeps_a_stored_zone_and_one_this_PC_does_not_know()
    {
        var known = TimeZoneInfo.GetSystemTimeZones()[^1].Id;
        var item = new SpecialIconItem { Name = "Office", Kind = SpecialIconKinds.Clock };
        (ClockTileSettings.Default with { TimeZoneId = known }).ApplyTo(item);

        var viewModel = new ClockTileViewModel(item);
        Assert.Equal(known, viewModel.SelectedZone.Id);
        viewModel.Name = "Office 2"; // only the name changes
        viewModel.Apply(item);
        Assert.Equal(known, ClockTileSettings.From(item).TimeZoneId);

        // Review 24: a zone another PC knows and this one does not is offered as it is and kept on OK, not replaced by
        // "This PC".
        const string elsewhere = "Not/A_Zone_Here";
        (ClockTileSettings.Default with { TimeZoneId = elsewhere }).ApplyTo(item);
        viewModel = new ClockTileViewModel(item);
        Assert.Equal(elsewhere, viewModel.SelectedZone.Id);
        Assert.Equal(string.Format(DesktopIconGrid.Resources.Strings.Clock_UnknownZone, elsewhere), viewModel.SelectedZone.Label);
        Assert.Single(viewModel.Zones, zone => zone.Id == elsewhere);
        viewModel.Apply(item);
        Assert.Equal(elsewhere, ClockTileSettings.From(item).TimeZoneId);

        // Review 27: an id this PC resolves but does not list (an IANA id) is kept as well, under the zone's name.
        const string iana = "Asia/Tokyo";
        (ClockTileSettings.Default with { TimeZoneId = iana }).ApplyTo(item);
        viewModel = new ClockTileViewModel(item);
        Assert.Equal(iana, viewModel.SelectedZone.Id);
        Assert.EndsWith($"({iana})", viewModel.SelectedZone.Label);
        viewModel.Apply(item);
        Assert.Equal(iana, ClockTileSettings.From(item).TimeZoneId);

        // A choice made in the dialog replaces it.
        viewModel.SelectedZone = viewModel.Zones[0];
        viewModel.Apply(item);
        Assert.Null(ClockTileSettings.From(item).TimeZoneId);
    }

    [Fact]
    public void The_dialog_needs_minutes_only_for_a_timer_and_saves_a_picked_zone()
    {
        var item = new SpecialIconItem { Name = "Tokyo", Kind = SpecialIconKinds.Clock };
        ClockTileSettings.Default.ApplyTo(item);
        var viewModel = new ClockTileViewModel(item);
        Assert.Null(viewModel.SelectedZone.Id); // this PC's time
        Assert.True(viewModel.Zones.Count > 1);

        viewModel.Minutes = "soon";
        Assert.True(viewModel.CanSave); // no timer: the minutes do not matter
        viewModel.SelectedTimer = ClockTileViewModel.TimerChoices.Single(c => c.Kind == ClockTimerKind.Pomodoro);
        Assert.False(viewModel.CanSave);
        viewModel.Minutes = "50";
        viewModel.BreakMinutes = "10";
        viewModel.SelectedZone = viewModel.Zones[^1];
        Assert.True(viewModel.CanSave);

        viewModel.Apply(item);
        var saved = ClockTileSettings.From(item);

        Assert.Equal((ClockTimerKind.Pomodoro, 50, 10), (saved.Timer, saved.Minutes, saved.BreakMinutes));
        Assert.Equal(viewModel.Zones[^1].Id, saved.TimeZoneId);
    }
}
