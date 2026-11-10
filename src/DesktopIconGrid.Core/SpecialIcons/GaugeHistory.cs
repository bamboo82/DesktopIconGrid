using System.Globalization;

namespace DesktopIconGrid.Core.SpecialIcons;

/// <summary>
/// The last samples of a gauge, drawn as a line of block characters (<c>▁▂▃▄▅▆▇█</c>) for its tooltip: a tooltip is
/// text, and this needs no drawing of its own.
/// </summary>
public sealed class GaugeHistory(int capacity = GaugeHistory.DefaultCapacity)
{
    public const int DefaultCapacity = 60;

    private const string Blocks = "▁▂▃▄▅▆▇█";

    private readonly Queue<double?> _samples = new();

    public int Count => _samples.Count;

    /// <summary>Adds a sample (<see langword="null"/>: no reading), dropping the oldest beyond the capacity.</summary>
    public void Add(double? value)
    {
        _samples.Enqueue(value is { } v && double.IsFinite(v) ? Math.Max(v, 0) : null);
        while (_samples.Count > capacity)
        {
            _samples.Dequeue();
        }
    }

    /// <summary>The largest sample kept, or 0.</summary>
    public double Peak => _samples.Max() ?? 0;

    /// <summary>
    /// The samples, oldest first, one character each: scaled to <paramref name="max"/> (100 for a percentage), or to
    /// their own peak when it is <see langword="null"/> (a rate); a missing reading is a space. Empty with fewer than
    /// two samples.
    /// </summary>
    public string Render(double? max)
    {
        if (_samples.Count < 2)
        {
            return string.Empty;
        }

        var top = max ?? Peak;
        return string.Create(CultureInfo.InvariantCulture, $"{string.Concat(_samples.Select(sample => Block(sample, top)))}");
    }

    private static char Block(double? sample, double top)
    {
        if (sample is not { } value)
        {
            return ' ';
        }

        if (top <= 0)
        {
            return Blocks[0];
        }

        var index = (int)Math.Round(Math.Clamp(value / top, 0, 1) * (Blocks.Length - 1));
        return Blocks[index];
    }
}

/// <summary>Network throughput from two readings of the adapters' byte counters.</summary>
public static class NetworkRate
{
    /// <summary>
    /// Bytes per second between two counter readings, or <see langword="null"/> when there is no earlier reading, no
    /// time passed, or the counter went back (an adapter left, or the counter was reset).
    /// </summary>
    public static double? PerSecond(long? before, long now, double seconds) =>
        before is { } earlier && seconds > 0 && now >= earlier ? (now - earlier) / seconds : null;

    /// <summary>A rate short enough for a tile: <c>0</c>, <c>850K</c>, <c>1.2M</c>, <c>12M</c>, <c>1.1G</c> (bytes per second).</summary>
    public static string Short(double bytesPerSecond)
    {
        const double k = 1024, m = k * 1024, g = m * 1024;
        return bytesPerSecond switch
        {
            < 1 => "0",
            < k => "1K", // something moves: not "0"
            < m => Number(bytesPerSecond / k, "K", decimals: false),
            < g => Number(bytesPerSecond / m, "M", decimals: bytesPerSecond < 10 * m),
            _ => Number(bytesPerSecond / g, "G", decimals: bytesPerSecond < 10 * g),
        };
    }

    private static string Number(double value, string unit, bool decimals) =>
        value.ToString(decimals ? "0.#" : "0", CultureInfo.InvariantCulture) + unit;
}
