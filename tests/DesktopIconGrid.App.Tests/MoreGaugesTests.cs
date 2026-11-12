using DesktopIconGrid.Core.SpecialIcons;
using DesktopIconGrid.Services;
using DesktopIconGrid.SpecialIcons;

namespace DesktopIconGrid.App.Tests;

/// <summary>The memory and battery gauges (0.10.0).</summary>
public class MoreGaugesTests
{
    [Theory]
    [InlineData(80, 0, 0, 80d, true)] // on battery
    [InlineData(55, 8, 1, 55d, false)] // charging
    [InlineData(100, 0, 1, 100d, false)] // plugged in, full
    [InlineData(255, 128, 1, null, false)] // a desktop PC: no battery
    [InlineData(255, 0, 1, null, false)] // percentage unknown
    [InlineData(60, 255, 0, 60d, true)] // flag unknown (255 is not "no battery"), on battery
    [InlineData(60, 255, 255, 60d, false)] // nothing known but the percentage: no claim that it is plugged in
    public void The_power_status_bytes_are_read(byte percent, byte flag, byte line, double? expected, bool onBattery)
    {
        var reading = SystemMonitor.ReadBattery(percent, flag, line);

        Assert.Equal(expected, reading.Percent);
        Assert.Equal(onBattery, reading.OnBattery);
        Assert.NotEmpty(reading.Detail);
    }

    [Theory]
    [InlineData(null, false, CommandLevel.Unknown)]
    [InlineData(5d, true, CommandLevel.Failed)]
    [InlineData(15d, true, CommandLevel.Warning)]
    [InlineData(50d, true, CommandLevel.Ok)]
    [InlineData(5d, false, CommandLevel.Ok)] // low but charging: not a warning
    public void A_low_battery_is_the_bad_end_only_while_nothing_charges_it(double? percent, bool onBattery, CommandLevel level) =>
        Assert.Equal(level, SystemMonitor.BatteryLevel(percent, onBattery));

    [Fact]
    public void Memory_reads_a_percentage_on_Windows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var (percent, detail) = SystemMonitor.SampleMemory();

        Assert.InRange(percent!.Value, 1, 100);
        Assert.NotEmpty(detail);
    }

    [Fact]
    public void A_gauge_keeps_its_last_samples_as_a_line_under_the_tooltip_text()
    {
        var state = new GaugeState("CPU", GaugeStyle.Ring, 70, 90, "starting");
        Assert.Equal("starting", state.Detail); // nothing kept: no line

        state.Record(0, 100);
        state.Update(0, "idle");
        Assert.Equal("idle", state.Detail); // one sample is no line
        state.Record(100, 100);
        state.Update(100, "busy");

        Assert.Equal("busy\n▁█", state.Detail);
    }

    [Fact]
    public void The_network_counters_can_be_read_on_Windows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var counters = SystemMonitor.ReadNetworkCounters();

        Assert.All(counters, adapter => Assert.True(adapter.Value.Received >= 0 && adapter.Value.Sent >= 0));
        Assert.DoesNotContain(counters.Keys, string.IsNullOrEmpty);
    }

    [Fact]
    public void The_microphone_tile_is_red_while_muted_and_grey_without_a_microphone()
    {
        var state = new GaugeState("MIC", GaugeStyle.Number, 101, 101, string.Empty);

        var muted = MicrophoneMuteHandler.Show(state, (true, 80));
        Assert.Equal((CommandLevel.Failed, 80d), (state.Level!.Value, muted.Percent!.Value));
        var mutedLabel = state.LabelText;

        MicrophoneMuteHandler.Show(state, (false, 80));
        Assert.Equal(CommandLevel.Ok, state.Level);
        Assert.NotEqual(mutedLabel, state.LabelText);

        var none = MicrophoneMuteHandler.Show(state, null);
        Assert.Equal(CommandLevel.Unknown, state.Level);
        Assert.Null(none.Percent);
    }

    [Fact]
    public void Reading_the_microphone_never_throws_with_or_without_one()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var reading = MicrophoneControl.Read(); // a build machine has none: null, no exception

        Assert.True(reading is null || reading.Value.Volume is >= 0 and <= 100);
    }
}
