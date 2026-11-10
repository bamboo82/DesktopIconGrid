using DesktopIconGrid.Core.SpecialIcons;

namespace DesktopIconGrid.Core.Tests;

/// <summary>The sparkline in a gauge's tooltip and the network gauge's rates.</summary>
public class GaugeHistoryTests
{
    [Fact]
    public void A_percentage_is_drawn_against_100_and_a_missing_reading_is_a_gap()
    {
        var history = new GaugeHistory();
        Assert.Equal(string.Empty, history.Render(100));
        history.Add(0);
        Assert.Equal(string.Empty, history.Render(100)); // one sample is no line
        history.Add(50);
        history.Add(null);
        history.Add(100);
        history.Add(250); // clamped
        history.Add(double.NaN);

        Assert.Equal("▁▅ ██ ", history.Render(100));
    }

    [Fact]
    public void A_rate_is_drawn_against_its_own_peak_and_only_the_newest_samples_are_kept()
    {
        var history = new GaugeHistory(capacity: 3);
        foreach (var value in new double[] { 9000, 0, 100, 200 })
        {
            history.Add(value);
        }

        Assert.Equal(3, history.Count);
        Assert.Equal(200, history.Peak); // 9000 fell out
        Assert.Equal("▁▅█", history.Render(null));

        var flat = new GaugeHistory();
        flat.Add(0);
        flat.Add(0);
        Assert.Equal("▁▁", flat.Render(null)); // nothing moved: no division by zero
    }

    [Theory]
    [InlineData(null, 1000L, 2d, null)] // the first reading
    [InlineData(1000L, 5000L, 2d, 2000d)]
    [InlineData(5000L, 1000L, 2d, null)] // the counter went back
    [InlineData(1000L, 5000L, 0d, null)]
    public void A_rate_needs_two_readings_that_go_forward(long? before, long now, double seconds, double? expected) =>
        Assert.Equal(expected, NetworkRate.PerSecond(before, now, seconds));

    [Theory]
    [InlineData(0d, "0")]
    [InlineData(300d, "1K")]
    [InlineData(870_400d, "850K")]
    [InlineData(1_258_291d, "1.2M")]
    [InlineData(12_582_912d, "12M")]
    [InlineData(1_181_116_006d, "1.1G")]
    public void A_rate_fits_a_tile(double bytesPerSecond, string text) =>
        Assert.Equal(text, NetworkRate.Short(bytesPerSecond));
}
