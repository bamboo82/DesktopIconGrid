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
            AppUserModelId: isFrame && withAppIds ? NativeMethods.AppUserModelIdOf(hwnd) : null);
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
