using System.Runtime.InteropServices;
using DesktopIconGrid.Core.Diagnostics;

namespace DesktopIconGrid.Services;

/// <summary>
/// Lets the messages Explorer sends to this program's own windows through when the program runs as administrator
/// (0.17.0 WP2). Windows then drops a message from a process of lower integrity, as Explorer is, unless the window lets it
/// in: the tray icon's callback and the shell's TaskbarCreated on the tray's hidden window, the AppBar notifications on
/// the docked bar's. Exactly those are let in, each on its own window (<c>ChangeWindowMessageFilterEx</c> with
/// <c>MSGFLT_ALLOW</c>); nothing else is widened. It is called the same way when the program is not elevated, where it
/// changes nothing. A failure is logged once in a run and never stops the start.
/// </summary>
internal static class ShellMessages
{
    private static int _failureLogged;

    /// <summary>The call that lets <paramref name="message"/> reach <paramref name="window"/>; true when Windows took it.</summary>
    public static bool AllowOnWindow(IntPtr window, uint message) =>
        NativeMethods.ChangeWindowMessageFilterEx(window, message, NativeMethods.MSGFLT_ALLOW, IntPtr.Zero);

    /// <summary>
    /// Lets <paramref name="message"/> reach <paramref name="window"/> through <paramref name="allow"/> (the real call, or a
    /// test's recorder). The first failure of the run is logged, naming <paramref name="what"/>.
    /// </summary>
    public static void Allow(Func<IntPtr, uint, bool> allow, IntPtr window, uint message, string what)
    {
        ArgumentNullException.ThrowIfNull(allow);
        bool done;
        var error = 0;
        try
        {
            done = allow(window, message);
            if (!done)
            {
                error = Marshal.GetLastWin32Error();
            }
        }
        catch (EntryPointNotFoundException)
        {
            done = false;
        }

        if (!done && Interlocked.Exchange(ref _failureLogged, 1) == 0)
        {
            AppLog.Default.Warning("App", $"Messages from Explorer to the {what} could not be let in (error {error}); run as administrator, they may not arrive");
        }
    }
}
