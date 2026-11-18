using System.Diagnostics;
using System.IO;
using DesktopIconGrid.Core.Launching;
using DesktopIconGrid.Resources;

namespace DesktopIconGrid.Services;

/// <summary>Small Explorer helpers used by menus.</summary>
public static class ShellHelper
{
    /// <summary>Opens Explorer with the file or folder selected. Returns a failure for locations that are not on disk.</summary>
    public static LaunchResult RevealInExplorer(string location)
    {
        var expanded = Environment.ExpandEnvironmentVariables(location?.Trim() ?? string.Empty);
        if (File.Exists(expanded) || Directory.Exists(expanded))
        {
            return ProcessLauncher.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{expanded}\"") { UseShellExecute = true });
        }

        var resolved = ExecutableLocator.Find(expanded);
        if (resolved is not null)
        {
            return ProcessLauncher.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{resolved}\"") { UseShellExecute = true });
        }

        return LaunchResult.Failure(Strings.Shell_NotLocal);
    }

    /// <summary>
    /// Opens Explorer with <paramref name="path"/> selected, taken as it is: a received file may be called
    /// <c>%USERNAME%.txt</c>, which <see cref="RevealInExplorer"/> would expand. A Windows path holds no <c>"</c>.
    /// </summary>
    public static LaunchResult SelectInExplorer(string path) =>
        ProcessLauncher.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });

    public static LaunchResult OpenFolder(string directory) =>
        ProcessLauncher.Start(new ProcessStartInfo("explorer.exe", $"\"{directory}\"") { UseShellExecute = true });
}
