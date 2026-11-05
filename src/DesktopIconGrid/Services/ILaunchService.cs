using DesktopIconGrid.Core.Launching;
using DesktopIconGrid.Core.Models;

namespace DesktopIconGrid.Services;

public interface ILaunchService
{
    /// <summary>Starts the shortcut; <paramref name="asAdministrator"/> asks for elevation (an ordinary UAC prompt).</summary>
    LaunchResult Launch(ShortcutItem shortcut, bool asAdministrator = false);

    /// <summary>Opens the dropped paths with the shortcut's program (see <see cref="DropArguments"/>).</summary>
    LaunchResult LaunchWith(ShortcutItem shortcut, IReadOnlyList<string> paths);
}
