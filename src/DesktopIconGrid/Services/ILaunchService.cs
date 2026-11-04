using DesktopIconGrid.Core.Launching;
using DesktopIconGrid.Core.Models;

namespace DesktopIconGrid.Services;

public interface ILaunchService
{
    LaunchResult Launch(ShortcutItem shortcut);

    /// <summary>Opens the dropped paths with the shortcut's program (see <see cref="DropArguments"/>).</summary>
    LaunchResult LaunchWith(ShortcutItem shortcut, IReadOnlyList<string> paths);
}
