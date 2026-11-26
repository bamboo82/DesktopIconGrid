using System.Security;
using System.Security.Principal;
using DesktopIconGrid.Core.Diagnostics;

namespace DesktopIconGrid.Services;

/// <summary>
/// Whether the program runs as administrator, and what that costs (0.17.0 WP2). Started from an elevated terminal it runs
/// elevated. Explorer's messages to its windows are let in (<see cref="ShellMessages"/>); dropping files from Explorer on
/// an elevated window Windows does not allow at all, so that is said once in the log at start, with no dialog.
/// </summary>
internal static class Elevation
{
    /// <summary>The log's line at start when the program runs as administrator.</summary>
    internal const string Note = "Running as administrator: files cannot be dropped on the bar from Explorer, and programs started from it run as administrator.";

    /// <summary>Whether this process runs elevated: its token has the Administrators group to use, which a filtered token does not.</summary>
    public static bool IsElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch (SecurityException)
        {
            return false;
        }
    }

    /// <summary>Writes <see cref="Note"/> to <paramref name="log"/> when <paramref name="elevated"/> says the program runs as administrator.</summary>
    public static void NoteAtStart(AppLog log, Func<bool> elevated)
    {
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(elevated);
        if (elevated())
        {
            log.Info("App", Note);
        }
    }
}
