namespace DesktopIconGrid.Core.Models;

/// <summary>The window a hold shows and hides (the window at the pointer), as <see cref="HoldToShow"/> sees it.</summary>
public interface IHoldWindow
{
    /// <summary>The window is shown now.</summary>
    bool IsOpen { get; }

    /// <summary>A menu of the window is open, or a drag from it is under way: a hide waits until that ends.</summary>
    bool IsBusy { get; }

    void Show();

    void Hide();
}

/// <summary>
/// The key held to show the bar at the pointer (0.15.0 WP2, the owner's request): its press shows the window at the
/// pointer, and the first of its keys to come up (the main key or any of its modifiers) hides it. The keys are looked at
/// by <see cref="Tick"/>, 20 times a second, as the voice key's are; there is no keyboard hook. A short tap shows and
/// hides: the show-at-pointer key is the one that stays.
/// </summary>
/// <param name="window">The window at the pointer.</param>
/// <param name="keysHeld">Whether every key of the combination is down now.</param>
public sealed class HoldToShow(IHoldWindow window, Func<bool> keysHeld)
{
    /// <summary>How often the keys are looked at while a hold lasts.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(50);

    private enum Phase
    {
        /// <summary>No hold.</summary>
        Idle,

        /// <summary>The keys are down and the window is shown by this hold.</summary>
        Holding,

        /// <summary>The keys came up while a menu or a drag kept the window: it hides when that ends.</summary>
        LetGo,
    }

    private Phase _phase;

    /// <summary>A hold is under way: the window is this hold's, and a click in it is a plain one.</summary>
    public bool IsHolding => _phase == Phase.Holding;

    /// <summary>
    /// The combination was pressed. A press while a hold runs (a repeat) does nothing more; a press while the window is
    /// already open, by the other key, does nothing, and nor does its release. Returns whether the keys need looking at.
    /// </summary>
    public bool Pressed()
    {
        if (_phase != Phase.Idle)
        {
            return true;
        }

        if (window.IsOpen)
        {
            return false;
        }

        window.Show();
        if (!window.IsOpen)
        {
            return false; // it could not be shown
        }

        _phase = Phase.Holding;
        return true;
    }

    /// <summary>One look at the keys. Returns whether to look again.</summary>
    public bool Tick()
    {
        switch (_phase)
        {
            case Phase.Holding:
                if (!window.IsOpen)
                {
                    // A launch, the focus elsewhere, Esc: the window went away, and the rest of the hold is nothing.
                    _phase = Phase.Idle;
                    return false;
                }

                if (keysHeld())
                {
                    return true;
                }

                if (window.IsBusy)
                {
                    _phase = Phase.LetGo;
                    return true;
                }

                return End();
            case Phase.LetGo:
                if (!window.IsOpen)
                {
                    _phase = Phase.Idle;
                    return false;
                }

                return window.IsBusy || End();
            default:
                return false;
        }
    }

    private bool End()
    {
        _phase = Phase.Idle;
        window.Hide();
        return false;
    }
}
