using DesktopIconGrid.Core.SpecialIcons;
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
}
