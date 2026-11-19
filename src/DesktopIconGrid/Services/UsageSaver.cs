using DesktopIconGrid.Core.Diagnostics;
using DesktopIconGrid.Core.Storage;

namespace DesktopIconGrid.Services;

/// <summary>
/// Writes <c>usage.json</c> (0.12.0 WP4): at most once every <see cref="Interval"/> after a use was counted, and at
/// exit, always on a worker thread. The shortcuts on the bar, whose counts are kept whatever their age, are read on
/// the bar's thread first.
/// </summary>
public sealed class UsageSaver
{
    private readonly object _gate = new();
    private readonly UsageLog _log;
    private readonly Func<IReadOnlySet<Guid>> _shortcutsOnBar;
    private readonly Func<Func<IReadOnlySet<Guid>>, Task<IReadOnlySet<Guid>>> _onBarThread;
    /// <summary>When the last write began, on <see cref="Environment.TickCount64"/>; none yet.</summary>
    private long? _lastWrite;
    private bool _scheduled;

    /// <param name="log">The counts.</param>
    /// <param name="shortcutsOnBar">The ids of the shortcuts on the bar.</param>
    /// <param name="onBarThread">Runs <paramref name="shortcutsOnBar"/> on the bar's thread (the app: <c>Dispatcher.InvokeAsync</c>).</param>
    public UsageSaver(UsageLog log, Func<IReadOnlySet<Guid>> shortcutsOnBar, Func<Func<IReadOnlySet<Guid>>, Task<IReadOnlySet<Guid>>> onBarThread)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _shortcutsOnBar = shortcutsOnBar ?? throw new ArgumentNullException(nameof(shortcutsOnBar));
        _onBarThread = onBarThread ?? throw new ArgumentNullException(nameof(onBarThread));
        _log.Changed += (_, _) => Schedule();
    }

    /// <summary>The shortest time between two writes.</summary>
    public static TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Writes what is not written yet, on a worker thread; the bar waits for it, bounded, before it exits.</summary>
    public async Task FlushAsync()
    {
        var onBar = await _onBarThread(_shortcutsOnBar).ConfigureAwait(false);
        await Task.Run(() => _log.Save(onBar.Contains)).ConfigureAwait(false);
    }

    private void Schedule()
    {
        TimeSpan wait;
        lock (_gate)
        {
            if (_scheduled)
            {
                return; // the write already waiting takes this use too
            }

            _scheduled = true;
            wait = WaitBefore(_lastWrite, Environment.TickCount64, Interval);
        }

        _ = WriteAfterAsync(wait);
    }

    /// <summary>
    /// How long the next write waits: the rest of the interval since the last one, never more than the interval and
    /// never less than nothing, on a clock that only goes forward. The wall clock set back by J made it wait J + 10 s,
    /// and past 49.7 days <see cref="Task.Delay(TimeSpan)"/> threw, so nothing more was written (0.12.1, review 28 U3).
    /// </summary>
    internal static TimeSpan WaitBefore(long? lastWriteMilliseconds, long nowMilliseconds, TimeSpan interval)
    {
        if (lastWriteMilliseconds is not { } last)
        {
            return TimeSpan.Zero;
        }

        var elapsed = TimeSpan.FromMilliseconds(Math.Max(0, nowMilliseconds - last));
        return elapsed >= interval ? TimeSpan.Zero : interval - elapsed;
    }

    private async Task WriteAfterAsync(TimeSpan wait)
    {
        try
        {
            await Task.Delay(wait).ConfigureAwait(false);
        }
        finally
        {
            lock (_gate)
            {
                _scheduled = false; // whatever happened, the next use schedules a write again
                _lastWrite = Environment.TickCount64;
            }
        }

        try
        {
            await FlushAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            AppLog.Default.Debug("Usage", $"The uses could not be written: {ex.Message}");
        }
    }
}
