namespace DesktopIconGrid.Core.Sync;

/// <summary>
/// When the next look at an inbox on Google Drive is due after looks that failed (review 27 S4): 1, 2, 5, then every 15
/// minutes, instead of a call and a warning in the log every minute while Drive cannot be reached. A look that works,
/// or the user opening a Send to PC icon's properties, ends the waiting. Only the first failure since then is worth a
/// warning in the log; the repeats are written at debug level.
/// </summary>
public sealed class InboxBackOff
{
    private static readonly TimeSpan[] Waits = [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15)];

    /// <summary>A look on the minute timer may come a little early: it counts as due within this much.</summary>
    private static readonly TimeSpan Slack = TimeSpan.FromSeconds(5);

    private int _failures;
    private DateTime _due = DateTime.MinValue;

    /// <summary>Failed looks since the last one that worked.</summary>
    public int Failures => _failures;

    /// <summary>Whether a look may be made at <paramref name="now"/>.</summary>
    public bool IsDue(DateTime now) => now + Slack >= _due;

    /// <summary>A look begun at <paramref name="startedAt"/> failed. True for the first failure since the last success or reset.</summary>
    public bool Failed(DateTime startedAt)
    {
        _failures++;
        _due = startedAt + Waits[Math.Min(_failures, Waits.Length) - 1];
        return _failures == 1;
    }

    /// <summary>A look worked, or the user opened the properties: the next look is due at once.</summary>
    public void Reset()
    {
        _failures = 0;
        _due = DateTime.MinValue;
    }
}
