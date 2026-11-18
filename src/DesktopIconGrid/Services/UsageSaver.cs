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
    private DateTime _lastWrite = DateTime.MinValue;
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
            wait = _lastWrite + Interval - DateTime.UtcNow;
        }

        _ = WriteAfterAsync(wait > TimeSpan.Zero ? wait : TimeSpan.Zero);
    }

    private async Task WriteAfterAsync(TimeSpan wait)
    {
        await Task.Delay(wait).ConfigureAwait(false);
        lock (_gate)
        {
            _scheduled = false;
            _lastWrite = DateTime.UtcNow;
        }

        await FlushAsync().ConfigureAwait(false);
    }
}
