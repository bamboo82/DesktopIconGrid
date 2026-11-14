using DesktopIconGrid.Core.Diagnostics;
using DesktopIconGrid.Core.Launching;

namespace DesktopIconGrid.Services;

/// <summary>Brings a running program's window forward instead of starting it again.</summary>
public interface IWindowSwitcher
{
    /// <summary>
    /// Activates, cycles or minimises a window of <paramref name="processIds"/>, or a Store app frame of
    /// <paramref name="packageFamily"/> (see <see cref="WindowSwitching.WindowsOf"/> and <see cref="WindowSwitching.Decide"/>).
    /// False when there is no window to switch to, so the caller starts the program.
    /// </summary>
    bool TrySwitch(IReadOnlySet<int> processIds, string? packageFamily = null);

    /// <summary>
    /// The top-level windows now, topmost first, for a workspace to tell its members' windows apart
    /// (<see cref="WorkspaceWindows"/>); <paramref name="withAppIds"/> also reads each Store app frame's AppUserModelID.
    /// </summary>
    IReadOnlyList<WindowInfo> Windows(bool withAppIds = false) => [];

    /// <summary>Where <paramref name="window"/> goes back to (its normal rectangle) and whether it is maximised; <see langword="null"/> when it is gone.</summary>
    SavedWindow? Capture(IntPtr window) => null;

    /// <summary>Moves <paramref name="window"/> to <paramref name="target"/>; false when it is gone.</summary>
    bool Place(IntPtr window, SavedWindow target) => false;
}

/// <summary>
/// The Win32 side of <see cref="WindowSwitching"/>: a snapshot of the top-level windows, the window that was in
/// front before the bar took the click (an out-of-context foreground event hook that skips this process, so the
/// bar's own activation does not count), and the restore, activate and minimise calls. Lives on the UI thread,
/// whose message loop delivers the hook's events.
/// </summary>
public sealed class WindowSwitcher : IWindowSwitcher, IDisposable
{
    private readonly NativeMethods.WinEventProc _onForeground; // kept alive for as long as the hook exists
    private IntPtr _hook;
    private IntPtr _lastForeground;

    public WindowSwitcher()
    {
        _onForeground = OnForeground;
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        _lastForeground = NativeMethods.GetForegroundWindow();
        _hook = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_SYSTEM_FOREGROUND,
            NativeMethods.EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero,
            _onForeground,
            0,
            0,
            NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS);
        if (_hook == IntPtr.Zero)
        {
            AppLog.Default.Warning("Launch", "Foreground changes cannot be followed; a click on a program in front will not minimise it");
        }
    }

    public bool TrySwitch(IReadOnlySet<int> processIds, string? packageFamily = null)
    {
        ArgumentNullException.ThrowIfNull(processIds);
        if ((processIds.Count == 0 && packageFamily is null) || !OperatingSystem.IsWindows())
        {
            return false;
        }

        var windows = WindowSwitching.WindowsOf(Snapshot(withAppIds: packageFamily is not null), processIds, packageFamily);
        var action = WindowSwitching.Decide(windows, _lastForeground);
        switch (action.Kind)
        {
            case SwitchKind.Activate:
                if (NativeMethods.IsIconic(action.Window))
                {
                    NativeMethods.ShowWindowAsync(action.Window, NativeMethods.SW_RESTORE);
                }

                // The bar just received the click, so Windows lets it hand the foreground on.
                if (!NativeMethods.SetForegroundWindow(action.Window))
                {
                    AppLog.Default.Debug("Launch", "Windows refused to bring the program's window forward");
                }

                return true;
            case SwitchKind.Minimize:
                NativeMethods.ShowWindowAsync(action.Window, NativeMethods.SW_MINIMIZE);
                _lastForeground = IntPtr.Zero; // until Windows reports the next window in front
                return true;
            default:
                return false;
        }
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            NativeMethods.UnhookWinEvent(_hook);
            _hook = IntPtr.Zero;
        }
    }

    public IReadOnlyList<WindowInfo> Windows(bool withAppIds = false) => OperatingSystem.IsWindows() ? Snapshot(withAppIds) : [];

    /// <summary>
    /// Where the window goes back to and whether it is maximised: its normal rectangle (workspace coordinates), or, for a
    /// window snapped to a half or into a snap layout, its rectangle on the screen (review 27 B5: the normal rectangle of
    /// a snapped window is the one from before the snap). A window minimised from maximised counts as maximised.
    /// </summary>
    public SavedWindow? Capture(IntPtr window)
    {
        if (window == IntPtr.Zero || !OperatingSystem.IsWindows())
        {
            return null;
        }

        // The placement's normal rectangle (workspace coordinates), whatever state the window is in: Place hands the same
        // rectangle back to SetWindowPlacement, so nothing is converted and nothing drifts (review 24 W1).
        var placement = new NativeMethods.WINDOWPLACEMENT { Length = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.WINDOWPLACEMENT>() };
        if (!NativeMethods.GetWindowPlacement(window, ref placement))
        {
            return null;
        }

        var minimized = NativeMethods.IsIconic(window);
        var maximized = NativeMethods.IsZoomed(window) || (minimized && (placement.Flags & NativeMethods.WPF_RESTORETOMAXIMIZED) != 0);
        if (!minimized && !maximized && IsArranged(window, placement.NormalPosition) && NativeMethods.GetWindowRect(window, out var onScreen))
        {
            var snapped = new SavedWindow(onScreen.Left, onScreen.Top, onScreen.Right - onScreen.Left, onScreen.Bottom - onScreen.Top, Maximized: false, Screen: true);
            return snapped is { Width: > 0, Height: > 0 } ? snapped : null;
        }

        var rect = placement.NormalPosition;
        var saved = new SavedWindow(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top, maximized);
        return saved is { Width: > 0, Height: > 0 } ? saved : null;
    }

    /// <summary>Whether user32 has IsWindowArranged (Windows 10 1903 and later); null until it was tried.</summary>
    private static bool? _hasIsWindowArranged;

    /// <summary>
    /// Whether the window is snapped. Without IsWindowArranged: whether it stands elsewhere than its normal rectangle,
    /// taken from workspace to screen coordinates by its monitor's work area.
    /// </summary>
    private static bool IsArranged(IntPtr window, NativeMethods.RECT normal)
    {
        if (_hasIsWindowArranged != false)
        {
            try
            {
                var arranged = NativeMethods.IsWindowArranged(window);
                _hasIsWindowArranged = true;
                return arranged;
            }
            catch (EntryPointNotFoundException)
            {
                _hasIsWindowArranged = false;
            }
        }

        if (!NativeMethods.GetWindowRect(window, out var actual))
        {
            return false;
        }

        var info = new NativeMethods.MONITORINFOEX { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFOEX>() };
        var monitor = NativeMethods.MonitorFromWindow(window, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var (dx, dy) = monitor != IntPtr.Zero && NativeMethods.GetMonitorInfo(monitor, ref info)
            ? (info.rcWork.Left - info.rcMonitor.Left, info.rcWork.Top - info.rcMonitor.Top)
            : (0, 0);
        return actual.Left != normal.Left + dx || actual.Top != normal.Top + dy
               || actual.Right - actual.Left != normal.Right - normal.Left || actual.Bottom - actual.Top != normal.Bottom - normal.Top;
    }

    public bool Place(IntPtr window, SavedWindow target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (window == IntPtr.Zero || !OperatingSystem.IsWindows())
        {
            return false;
        }

        // A monitor that is gone since the position was remembered: onto one that is there (W2).
        var monitors = ScreenGeometry.Monitors().Select(m => new Core.Models.PixelRect(m.Left, m.Top, m.Width, m.Height)).ToList();
        target = target.OnScreen(monitors);

        var placement = new NativeMethods.WINDOWPLACEMENT { Length = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.WINDOWPLACEMENT>() };
        if (!NativeMethods.GetWindowPlacement(window, ref placement))
        {
            return false;
        }

        // A window without a sizing border (an unlock dialog, a profile picker) is moved, not stretched (review 27 W1).
        var sizable = (NativeMethods.GetStyle(window) & NativeMethods.WS_THICKFRAME) != 0;
        if (target.Screen)
        {
            // A snapped position (B5): out of a minimised or maximised state first, then moved to its rectangle on the
            // screen. Both are posted to the window's thread in this order, so the restore cannot undo the move (W1 of
            // review 24), and a hung program does not block the bar.
            if (NativeMethods.IsIconic(window) || NativeMethods.IsZoomed(window))
            {
                placement.Flags = NativeMethods.WPF_ASYNCWINDOWPLACEMENT;
                placement.ShowCmd = NativeMethods.SW_SHOWNORMAL;
                if (!NativeMethods.SetWindowPlacement(window, ref placement))
                {
                    return false;
                }
            }

            var flags = NativeMethods.SWP_ASYNCWINDOWPOS | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE | (sizable ? 0 : NativeMethods.SWP_NOSIZE);
            return NativeMethods.SetWindowPos(window, IntPtr.Zero, target.Left, target.Top, target.Width, target.Height, flags);
        }

        // One call sets the normal rectangle and the state together, so a queued restore cannot put the old rectangle
        // back, and a window maximised on one monitor moves to the other before it is maximised there (W1). Asynchronous:
        // a hung program does not block the bar (W6).
        var normal = placement.NormalPosition;
        var (width, height) = sizable ? (target.Width, target.Height) : (normal.Right - normal.Left, normal.Bottom - normal.Top);
        placement.Flags = NativeMethods.WPF_ASYNCWINDOWPLACEMENT;
        placement.ShowCmd = target.Maximized ? NativeMethods.SW_MAXIMIZE : NativeMethods.SW_SHOWNORMAL;
        placement.NormalPosition = new NativeMethods.RECT { Left = target.Left, Top = target.Top, Right = target.Left + width, Bottom = target.Top + height };
        return NativeMethods.SetWindowPlacement(window, ref placement);
    }

    /// <summary>
    /// Every top-level window, topmost first, described for <see cref="WindowSwitching"/>; <paramref name="withAppIds"/>
    /// also reads the AppUserModelID of each Store app frame.
    /// </summary>
    internal static List<WindowInfo> Snapshot(bool withAppIds = false)
    {
        var windows = new List<WindowInfo>();
        NativeMethods.EnumWindowsProc callback = (hwnd, _) =>
        {
            windows.Add(Describe(hwnd, withAppIds));
            return true;
        };
        NativeMethods.EnumWindows(callback, IntPtr.Zero);
        GC.KeepAlive(callback);
        return windows;
    }

    private static WindowInfo Describe(IntPtr hwnd, bool withAppIds)
    {
        NativeMethods.GetWindowThreadProcessId(hwnd, out var processId);
        if (!NativeMethods.IsWindowVisible(hwnd))
        {
            return new WindowInfo(hwnd, (int)processId, string.Empty, false, false, false, false, false, false);
        }

        var className = ClassNameOf(hwnd);
        var isFrame = className == WindowSwitching.FrameWindowClass;
        var exStyle = NativeMethods.GetExStyle(hwnd);
        var style = NativeMethods.GetStyle(hwnd);
        var cloaked = NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DWMWA_CLOAKED, out var cloakedValue, sizeof(int)) == 0 && cloakedValue != 0;
        return new WindowInfo(
            hwnd,
            (int)processId,
            className,
            IsVisible: true,
            IsCloaked: cloaked,
            HasOwner: NativeMethods.GetWindow(hwnd, NativeMethods.GW_OWNER) != IntPtr.Zero,
            IsToolWindow: (exStyle & NativeMethods.WS_EX_TOOLWINDOW) != 0,
            IsAppWindow: (exStyle & NativeMethods.WS_EX_APPWINDOW) != 0,
            IsMinimized: NativeMethods.IsIconic(hwnd),
            HostedProcessId: isFrame ? HostedProcess(hwnd) : null,
            AppUserModelId: isFrame && withAppIds ? NativeMethods.AppUserModelIdOf(hwnd) : null,
            IsBare: (style & NativeMethods.WS_CAPTION) != NativeMethods.WS_CAPTION && (style & NativeMethods.WS_THICKFRAME) == 0);
    }

    /// <summary>The process of the Store app inside a frame: the owner of its <c>Windows.UI.Core.CoreWindow</c> child.</summary>
    private static int? HostedProcess(IntPtr frame)
    {
        int? hosted = null;
        NativeMethods.EnumWindowsProc callback = (child, _) =>
        {
            if (ClassNameOf(child) != WindowSwitching.CoreWindowClass)
            {
                return true;
            }

            NativeMethods.GetWindowThreadProcessId(child, out var processId);
            hosted = (int)processId;
            return false;
        };
        NativeMethods.EnumChildWindows(frame, callback, IntPtr.Zero);
        GC.KeepAlive(callback);
        return hosted;
    }

    private static string ClassNameOf(IntPtr hwnd)
    {
        var buffer = new char[256];
        var length = NativeMethods.GetClassName(hwnd, buffer, buffer.Length);
        return length > 0 ? new string(buffer, 0, length) : string.Empty;
    }

    private void OnForeground(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint eventThread, uint eventTime)
    {
        if (idObject == NativeMethods.OBJID_WINDOW && hwnd != IntPtr.Zero)
        {
            _lastForeground = hwnd;
        }
    }
}
