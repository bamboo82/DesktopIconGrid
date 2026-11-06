using DesktopIconGrid.Core.Models;

namespace DesktopIconGrid.Core.Launching;

/// <summary>
/// Whether a shortcut's target is missing on this PC: a cheap existence check of local paths only. Links, shell and
/// special locations, UNC paths and bare program names (found through PATH or App Paths) are never called missing, nor
/// is anything on a network or removable drive, where a check could hang or the drive may simply not be plugged in.
/// </summary>
public static class LocationProbe
{
    /// <summary>
    /// The path to check for <paramref name="location"/> (environment variables expanded, quotes removed), or
    /// <see langword="null"/> when it is not a path on a drive letter.
    /// </summary>
    public static string? CheckablePath(string? location)
    {
        if (string.IsNullOrWhiteSpace(location))
        {
            return null;
        }

        var expanded = Environment.ExpandEnvironmentVariables(location.Trim().Trim('"')).Trim();
        if (LaunchPlanner.IsSpecial(expanded) || LaunchPlanner.IsShellNamespace(expanded) || LaunchPlanner.IsUri(expanded))
        {
            return null;
        }

        return expanded.Length >= 3 && char.IsAsciiLetter(expanded[0]) && expanded[1] == ':' && expanded[2] is '\\' or '/'
            ? expanded
            : null;
    }

    /// <summary>
    /// True when the shortcut's path on this PC (<see cref="ShortcutItem.EffectiveLocation"/>) is a local path that names
    /// neither a file nor a folder. <paramref name="isSlowStorage"/> tells network and removable drives apart (they are
    /// skipped); <paramref name="exists"/> defaults to the file system.
    /// </summary>
    public static bool IsMissing(ShortcutItem shortcut, Func<string, bool> isSlowStorage, Func<string, bool>? exists = null)
    {
        ArgumentNullException.ThrowIfNull(shortcut);
        ArgumentNullException.ThrowIfNull(isSlowStorage);
        var path = CheckablePath(shortcut.EffectiveLocation);
        if (path is null || isSlowStorage(path))
        {
            return false;
        }

        return !(exists ?? Exists)(path);
    }

    private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);
}
