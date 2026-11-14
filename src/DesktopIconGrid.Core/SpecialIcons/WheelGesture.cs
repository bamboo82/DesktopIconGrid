namespace DesktopIconGrid.Core.SpecialIcons;

/// <summary>
/// The mouse wheel over a tile that takes it (the volume tile), for one bar (review 27 B6, G1).
/// <list type="bullet">
/// <item>Whole notches only: a precision touchpad sends many small deltas, and one step per event ran the volume to 100
/// in a short swipe. Each <see cref="Notch"/> of delta is one notch; what is left over waits for the next event, and is
/// dropped when the pointer leaves the tile or the direction changes.</item>
/// <item>A gesture belongs to where it began: over the tile, an event is the tile's only when the last wheel event on the
/// bar, less than <see cref="Gap"/> ago, was over the same tile, or there was none that recent. Scrolling a long bar
/// does not turn into volume when the tile slides under the pointer.</item>
/// </list>
/// </summary>
public sealed class WheelGesture
{
    /// <summary>Windows' delta for one notch of a wheel.</summary>
    public const int Notch = 120;

    /// <summary>A pause this long between wheel events ends a gesture.</summary>
    public static readonly TimeSpan Gap = TimeSpan.FromMilliseconds(400);

    private Guid? _last;
    private DateTime _lastAt = DateTime.MinValue;
    private Guid? _tile;
    private int _remainder;

    /// <summary>A wheel event on the bar that is not over a tile taking the wheel: it scrolls the bar, and ends a tile's gesture.</summary>
    public void Elsewhere(DateTime now)
    {
        _last = null;
        _lastAt = now;
        _tile = null;
        _remainder = 0;
    }

    /// <summary>The pointer left <paramref name="tile"/>: what was left of a notch over it is dropped.</summary>
    public void Left(Guid tile)
    {
        if (_tile == tile)
        {
            _tile = null;
            _remainder = 0;
        }
    }

    /// <summary>
    /// A wheel event of <paramref name="delta"/> over <paramref name="tile"/>: how many notches it turns the tile (0 for
    /// less than a whole one so far), or <see langword="null"/> when the event is not the tile's: a gesture that began
    /// elsewhere on the bar, which goes on scrolling it.
    /// </summary>
    public int? OverTile(Guid tile, int delta, DateTime now)
    {
        if (now - _lastAt < Gap && _last != tile)
        {
            _lastAt = now; // the gesture goes on, still the bar's
            return null;
        }

        _last = tile;
        _lastAt = now;
        if (_tile != tile || (_remainder != 0 && Math.Sign(_remainder) != Math.Sign(delta)))
        {
            _tile = tile;
            _remainder = 0;
        }

        _remainder += delta;
        var notches = _remainder / Notch;
        _remainder -= notches * Notch;
        return notches;
    }
}
