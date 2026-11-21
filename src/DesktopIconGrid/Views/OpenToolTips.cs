using System.Windows;
using System.Windows.Controls;

namespace DesktopIconGrid.Views;

/// <summary>
/// The tooltips open now, so that a window can close its own when it loses the focus (review 41 U3). A tooltip stays up
/// while the pointer rests on its element, and a dialog that comes up over the bar does not move the pointer, so a tooltip
/// of the bar stayed in front of Settings for minutes. The bar closes its own when it is deactivated
/// (<see cref="MainWindow"/>); the pointer moving back over an icon shows it again.
/// </summary>
public static class OpenToolTips
{
    private static readonly List<WeakReference<ToolTip>> Tips = [];
    private static bool _registered;

    /// <summary>Follows every tooltip that opens and closes; called once at start, more calls change nothing.</summary>
    public static void Register()
    {
        if (_registered)
        {
            return;
        }

        _registered = true;
        EventManager.RegisterClassHandler(typeof(ToolTip), ToolTip.OpenedEvent, new RoutedEventHandler((sender, _) => Opened(sender as ToolTip)));
        EventManager.RegisterClassHandler(typeof(ToolTip), ToolTip.ClosedEvent, new RoutedEventHandler((sender, _) => Closed(sender as ToolTip)));
    }

    /// <summary>
    /// Closes the open tooltips of <paramref name="window"/>'s elements, those in its popups among them (a group's), and
    /// returns how many. A tooltip of another window stays.
    /// </summary>
    public static int CloseFor(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        var closed = 0;
        foreach (var tip in Current())
        {
            if (tip.PlacementTarget is DependencyObject target && Window.GetWindow(target) is { } owner && !ReferenceEquals(owner, window))
            {
                continue;
            }

            tip.IsOpen = false;
            closed++;
        }

        return closed;
    }

    private static void Opened(ToolTip? tip)
    {
        if (tip is null)
        {
            return;
        }

        Tips.RemoveAll(entry => !entry.TryGetTarget(out var kept) || ReferenceEquals(kept, tip));
        Tips.Add(new WeakReference<ToolTip>(tip));
    }

    private static void Closed(ToolTip? tip) =>
        Tips.RemoveAll(entry => !entry.TryGetTarget(out var kept) || ReferenceEquals(kept, tip));

    private static List<ToolTip> Current()
    {
        var open = new List<ToolTip>();
        foreach (var entry in Tips)
        {
            if (entry.TryGetTarget(out var tip) && tip.IsOpen)
            {
                open.Add(tip);
            }
        }

        return open;
    }
}
