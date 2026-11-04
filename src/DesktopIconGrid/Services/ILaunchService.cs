using DesktopIconGrid.Core.Launching;
using DesktopIconGrid.Core.Models;

namespace DesktopIconGrid.Services;

public interface ILaunchService
{
    LaunchResult Launch(ShortcutItem shortcut);
}
