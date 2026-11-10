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
    public void The_dialog_needs_minutes_only_for_a_timer_and_keeps_the_zone()
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
