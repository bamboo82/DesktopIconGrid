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
        if (_samples.Count(sample => sample is not null) < 2)
        {
            return string.Empty; // nothing to draw (a battery gauge on a desktop PC): no line of blanks
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

/// <summary>The volume tile's wheel steps.</summary>
public static class VolumeSteps
{
    /// <summary>Percentage points per wheel notch, as Windows' own volume keys use.</summary>
    public const int StepSize = 2;

    /// <summary>The volume (0–100) after <paramref name="notches"/> wheel notches (up is positive) from <paramref name="level"/>.</summary>
    public static double After(double level, int notches) =>
        Math.Clamp(Math.Round(double.IsFinite(level) ? level : 0) + (notches * StepSize), 0, 100);
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

    /// <summary>
    /// Windows lists an adapter's filter layers as adapters of their own with the parent's counters
    /// ("…-WFP Native MAC Layer LightWeight Filter-0000", "…-QoS Packet Scheduler-0000"); counting them counts the same
    /// traffic several times (review 24 R4).
    /// </summary>
    public static bool IsFilterLayer(string? description) =>
        description is { Length: > 5 } text
        && text[^5] == '-'
        && text[^4..].All(char.IsAsciiDigit)
        && (text.Contains("Filter", StringComparison.OrdinalIgnoreCase) || text.Contains("Scheduler", StringComparison.OrdinalIgnoreCase)
            || text.Contains("WFP", StringComparison.Ordinal) || text.Contains("Layer", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Bytes received and sent between two readings, adapter by adapter: only adapters in both readings whose counters
    /// went forward count, so one that just came up does not add its whole lifetime in one interval.
    /// </summary>
    public static (long Received, long Sent) Delta(
        IReadOnlyDictionary<string, (long Received, long Sent)> before,
        IReadOnlyDictionary<string, (long Received, long Sent)> now)
    {
        long received = 0, sent = 0;
        foreach (var (id, current) in now)
        {
            if (before.TryGetValue(id, out var earlier) && current.Received >= earlier.Received && current.Sent >= earlier.Sent)
            {
                received += current.Received - earlier.Received;
                sent += current.Sent - earlier.Sent;
            }
        }

        return (received, sent);
    }

    /// <summary>A rate short enough for a tile: <c>0</c>, <c>850K</c>, <c>1.2M</c>, <c>12M</c>, <c>1.1G</c> (bytes per second).</summary>
    public static string Short(double bytesPerSecond)
    {
        const double k = 1024, m = k * 1024, g = m * 1024;
        return bytesPerSecond switch
        {
            < 1 => "0",
            < k => "1K", // something moves: not "0"
            < m - k / 2 => Number(bytesPerSecond / k, "K", decimals: false), // up to 1023K; the half K below 1M rounds to 1M
            < g => Number(bytesPerSecond / m, "M", decimals: bytesPerSecond < 10 * m),
            _ => Number(bytesPerSecond / g, "G", decimals: bytesPerSecond < 10 * g),
        };
    }

    private static string Number(double value, string unit, bool decimals) =>
        value.ToString(decimals ? "0.#" : "0", CultureInfo.InvariantCulture) + unit;
}
