using DesktopIconGrid.Core.Models;

namespace DesktopIconGrid.Core.Launching;

/// <summary>
/// Builds the command-line arguments used when files are dropped onto a shortcut ("open with").
/// <c>{file}</c> in the shortcut's parameters is replaced per file (one launch each), <c>{files}</c>
/// by all files at once; without a placeholder the files are appended to the parameters.
/// </summary>
public static class DropArguments
{
    public const string FilePlaceholder = "{file}";
    public const string FilesPlaceholder = "{files}";

    /// <summary>Programs, scripts and documents can open dropped files; links, shell folders and special actions cannot.</summary>
    public static bool SupportsOpenWith(ShortcutItem shortcut)
    {
        var location = shortcut?.Location?.Trim();
        return !string.IsNullOrEmpty(location)
            && !LaunchPlanner.IsSpecial(location)
            && !LaunchPlanner.IsShellNamespace(location)
            && !LaunchPlanner.IsUri(location);
    }

    /// <summary>One argument string per launch.</summary>
    public static IReadOnlyList<string> BuildArgumentSets(string? parameters, IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var cleaned = paths.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()).ToList();
        if (cleaned.Count == 0)
        {
            return [];
        }

        var template = parameters?.Trim() ?? string.Empty;
        var all = string.Join(' ', cleaned.Select(Quote));

        if (template.Contains(FilesPlaceholder, StringComparison.OrdinalIgnoreCase))
        {
            return [template.Replace(FilesPlaceholder, all, StringComparison.OrdinalIgnoreCase)];
        }

        if (template.Contains(FilePlaceholder, StringComparison.OrdinalIgnoreCase))
        {
            return cleaned.Select(path => template.Replace(FilePlaceholder, Quote(path), StringComparison.OrdinalIgnoreCase)).ToList();
        }

        return [template.Length == 0 ? all : $"{template} {all}"];
    }

    public static string Quote(string path) => $"\"{path}\"";
}
