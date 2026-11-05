namespace DesktopIconGrid.Core.Launching;

/// <summary>A top-level window as the app's <c>EnumWindows</c> snapshot describes it (topmost first).</summary>
/// <param name="Handle">The window.</param>
/// <param name="ProcessId">The process that owns the window.</param>
/// <param name="ClassName">The window class, e.g. <see cref="WindowSwitching.FrameWindowClass"/> for a Store app's frame.</param>
/// <param name="IsVisible"><c>WS_VISIBLE</c>.</param>
/// <param name="IsCloaked">DWM keeps it out of sight (another virtual desktop, a suspended Store app).</param>
/// <param name="HasOwner">An owned window (a dialog or a tool palette of another window).</param>
/// <param name="IsToolWindow"><c>WS_EX_TOOLWINDOW</c>: never on the taskbar.</param>
/// <param name="IsAppWindow"><c>WS_EX_APPWINDOW</c>: on the taskbar even when owned.</param>
/// <param name="IsMinimized">Iconic.</param>
/// <param name="HostedProcessId">
/// For a Store app's <c>ApplicationFrameWindow</c> (owned by ApplicationFrameHost.exe): the process of its
/// <c>Windows.UI.Core.CoreWindow</c> child, which is the app itself.
/// </param>
public sealed record WindowInfo(
    IntPtr Handle,
    int ProcessId,
    string ClassName,
    bool IsVisible,
    bool IsCloaked,
    bool HasOwner,
    bool IsToolWindow,
    bool IsAppWindow,
    bool IsMinimized,
    int? HostedProcessId = null);

public enum SwitchKind
{
    /// <summary>The program has no window to switch to: start it.</summary>
    Launch,

    /// <summary>Bring <see cref="SwitchAction.Window"/> forward, restoring it when minimised.</summary>
    Activate,

    /// <summary>The program was already in front: minimise <see cref="SwitchAction.Window"/>.</summary>
    Minimize,
}

public readonly record struct SwitchAction(SwitchKind Kind, IntPtr Window = default)
{
    public static readonly SwitchAction Launch = new(SwitchKind.Launch);
}

/// <summary>
/// What clicking a running program's icon does, decided the way the taskbar does it: bring its window forward;
/// minimise it when it is already in front; with several windows, go to the next one on every click.
/// </summary>
public static class WindowSwitching
{
    /// <summary>The frame a Store (UWP) app is drawn in; it belongs to ApplicationFrameHost.exe.</summary>
    public const string FrameWindowClass = "ApplicationFrameWindow";

    /// <summary>The Store app's own window inside its frame; it belongs to the app's process.</summary>
    public const string CoreWindowClass = "Windows.UI.Core.CoreWindow";

    /// <summary>
    /// The taskbar's rule for a button: visible, not cloaked, not a tool window, and either unowned or marked as
    /// an app window.
    /// </summary>
    public static bool HasTaskbarButton(WindowInfo window)
    {
        ArgumentNullException.ThrowIfNull(window);
        return window.IsVisible
               && !window.IsCloaked
               && !window.IsToolWindow
               && (window.IsAppWindow || !window.HasOwner);
    }

    /// <summary>
    /// The windows with a taskbar button that belong to <paramref name="processIds"/>, in the snapshot's z-order
    /// (topmost first). A Store app's frame counts for the process of the app inside it.
    /// </summary>
    public static IReadOnlyList<WindowInfo> WindowsOf(IEnumerable<WindowInfo> windows, IReadOnlySet<int> processIds)
    {
        ArgumentNullException.ThrowIfNull(windows);
        ArgumentNullException.ThrowIfNull(processIds);
        return processIds.Count == 0
            ? []
            : windows.Where(w => HasTaskbarButton(w)
                                 && (processIds.Contains(w.ProcessId)
                                     || (w.HostedProcessId is { } hosted && processIds.Contains(hosted))))
                     .ToList();
    }

    /// <summary>
    /// The action for a click on a program whose windows are <paramref name="windows"/> (topmost first), given the
    /// last window outside the bar that had the foreground before the click.
    /// </summary>
    public static SwitchAction Decide(IReadOnlyList<WindowInfo> windows, IntPtr lastForeground)
    {
        ArgumentNullException.ThrowIfNull(windows);
        if (windows.Count == 0)
        {
            return SwitchAction.Launch;
        }

        // Cycle in a stable order (window handles grow with creation, like taskbar buttons); z-order would bounce
        // between the two topmost windows.
        var stable = windows.OrderBy(w => w.Handle.ToInt64()).ToList();
        var front = lastForeground == IntPtr.Zero ? -1 : stable.FindIndex(w => w.Handle == lastForeground);
        if (front < 0)
        {
            return new SwitchAction(SwitchKind.Activate, windows[0].Handle); // the one used last comes forward
        }

        return stable.Count == 1
            ? new SwitchAction(SwitchKind.Minimize, stable[0].Handle)
            : new SwitchAction(SwitchKind.Activate, stable[(front + 1) % stable.Count].Handle);
    }
}
