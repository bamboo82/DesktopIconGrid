namespace DesktopIconGrid.Core.Launching;

/// <summary>What a workspace knows about one member when it looks for the member's window.</summary>
/// <param name="Id">The shortcut.</param>
/// <param name="Own">The processes the bar started for this shortcut that still run (launcher stubs included).</param>
/// <param name="Program">The processes of its program (by path, or in its Store package), whoever started them.</param>
/// <param name="PackageFamily">The Store app it starts, whose frame stays while the app is suspended.</param>
/// <param name="HasParameters">
/// It opens one thing with its program (a file, a server): another window of the same program is not its window unless
/// the bar started it for this shortcut.
/// </param>
public sealed record WorkspaceMember(
    Guid Id,
    IReadOnlySet<int> Own,
    IReadOnlySet<int> Program,
    string? PackageFamily = null,
    bool HasParameters = false);

/// <summary>
/// Tells the windows of a workspace's members apart, for one start or one capture of the workspace (review 24 W3):
/// <list type="bullet">
/// <item>a window belongs to one member at most, so two members on one program never share "the first window";</item>
/// <item>a member's own processes (what the bar started for it) come before the rest of its program;</item>
/// <item>a member the bar has just started takes only a window that was not there before it was started;</item>
/// <item>a window with neither a title bar nor a sizing border (a splash screen, as a rule) is not taken for a program
/// that is still starting until the wait is over, and a member whose window closes (the splash made way for the main
/// window) takes the next one.</item>
/// </list>
/// Windows are told apart by their handles in one snapshot after another; nothing here calls Windows.
/// </summary>
public sealed class WorkspaceWindows
{
    private readonly HashSet<IntPtr> _before;
    private readonly Dictionary<IntPtr, int> _firstSeen = [];
    private readonly Dictionary<Guid, IntPtr> _taken = [];
    private List<WindowInfo> _windows;
    private int _looks;

    /// <param name="windows">The top-level windows before anything is started, topmost first.</param>
    public WorkspaceWindows(IEnumerable<WindowInfo> windows)
    {
        ArgumentNullException.ThrowIfNull(windows);
        _windows = windows.Where(WindowSwitching.HasTaskbarButton).ToList();
        _before = _windows.Select(window => window.Handle).ToHashSet();
    }

    /// <summary>The window taken for <paramref name="member"/>, if it has one.</summary>
    public IntPtr? WindowOf(Guid member) => _taken.TryGetValue(member, out var window) ? window : null;

    /// <summary>
    /// A newer snapshot of the windows, topmost first. A window that is gone is no member's any more, so a member whose
    /// window closed can take another one.
    /// </summary>
    public void Look(IEnumerable<WindowInfo> windows)
    {
        ArgumentNullException.ThrowIfNull(windows);
        _looks++;
        _windows = windows.Where(WindowSwitching.HasTaskbarButton).ToList();
        var present = new HashSet<IntPtr>();
        foreach (var window in _windows)
        {
            present.Add(window.Handle);
            if (!_before.Contains(window.Handle))
            {
                _firstSeen.TryAdd(window.Handle, _looks);
            }
        }

        foreach (var (member, window) in _taken.ToList())
        {
            if (!present.Contains(window))
            {
                _taken.Remove(member);
            }
        }
    }

    /// <summary>
    /// Takes the windows of members that run already, in two rounds so that no member takes another one's own window:
    /// first every member's own processes' windows, then the rest of its program's, for a member without parameters or,
    /// with <paramref name="anyOfTheProgram"/> (a capture), for every member. Topmost first, and a window with a title
    /// bar or a sizing border before one with neither. Returns the members that have a window.
    /// </summary>
    public IReadOnlySet<Guid> TakeRunning(IReadOnlyList<WorkspaceMember> members, bool anyOfTheProgram)
    {
        ArgumentNullException.ThrowIfNull(members);
        foreach (var member in members)
        {
            Take(member, Free(member.Own, packageFamily: null), bareToo: true);
        }

        foreach (var member in members)
        {
            if (anyOfTheProgram || !member.HasParameters)
            {
                Take(member, Free(member.Program, member.PackageFamily), bareToo: true);
            }
        }

        return members.Where(member => _taken.ContainsKey(member.Id)).Select(member => member.Id).ToHashSet();
    }

    /// <summary>
    /// Takes windows for members the bar has just started, given in the order they were started: windows that were not
    /// there before, the member's own processes' first, then its program's, the oldest first. A window with neither a
    /// title bar nor a sizing border is taken only when <paramref name="waitIsOver"/>: until then it may be a splash
    /// screen.
    /// </summary>
    public void TakeNew(IReadOnlyList<WorkspaceMember> members, bool waitIsOver)
    {
        ArgumentNullException.ThrowIfNull(members);
        foreach (var member in members)
        {
            Take(member, Newest(Free(member.Own, packageFamily: null)), waitIsOver);
        }

        foreach (var member in members)
        {
            Take(member, Newest(Free(member.Program, member.PackageFamily)), waitIsOver);
        }
    }

    /// <summary>The windows of these processes (or that Store app) that no member has taken, topmost first.</summary>
    private List<WindowInfo> Free(IReadOnlySet<int> processIds, string? packageFamily)
    {
        var taken = _taken.Values.ToHashSet();
        return WindowSwitching.WindowsOf(_windows, processIds, packageFamily).Where(window => !taken.Contains(window.Handle)).ToList();
    }

    /// <summary>
    /// The windows that were not there before, the oldest first: by the look that first saw them, then from the bottom of
    /// the z-order up (a window that has just opened comes to the top).
    /// </summary>
    private List<WindowInfo> Newest(List<WindowInfo> windows) =>
        windows.Select((window, index) => (Window: window, Index: index))
               .Where(entry => _firstSeen.ContainsKey(entry.Window.Handle))
               .OrderBy(entry => _firstSeen[entry.Window.Handle])
               .ThenByDescending(entry => entry.Index)
               .Select(entry => entry.Window)
               .ToList();

    private void Take(WorkspaceMember member, List<WindowInfo> candidates, bool bareToo)
    {
        if (_taken.ContainsKey(member.Id))
        {
            return;
        }

        var window = candidates.FirstOrDefault(candidate => !candidate.IsBare) ?? (bareToo ? candidates.FirstOrDefault() : null);
        if (window is not null)
        {
            _taken[member.Id] = window.Handle;
        }
    }
}
