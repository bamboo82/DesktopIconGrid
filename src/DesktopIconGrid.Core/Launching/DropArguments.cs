using System.Text;
using DesktopIconGrid.Core.Models;

namespace DesktopIconGrid.Core.Launching;

/// <summary>
/// Builds the command-line arguments used when files are dropped onto a shortcut ("open with").
/// <c>{file}</c> in the shortcut's parameters is replaced per file (one launch each), <c>{files}</c>
/// by all files at once; without a placeholder the files are appended to the parameters.
/// Paths are quoted the way Windows' command-line parser expects (so <c>C:\</c> stays <c>C:\</c>).
/// </summary>
public static class DropArguments
{
    public const string FilePlaceholder = "{file}";
    public const string FilesPlaceholder = "{files}";

    /// <summary>Programs, scripts and documents can open dropped files; links, shell folders and special actions cannot.</summary>
    public static bool SupportsOpenWith(ShortcutItem shortcut)
    {
        var location = shortcut?.EffectiveLocation?.Trim();
        return !string.IsNullOrEmpty(location)
            && !LaunchPlanner.IsSpecial(location)
            && !LaunchPlanner.IsShellNamespace(location)
            && !LaunchPlanner.IsUri(location);
    }

    /// <summary>One argument string per launch. <paramref name="parameters"/> should already have environment variables expanded.</summary>
    public static IReadOnlyList<string> BuildArgumentSets(string? parameters, IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var cleaned = paths.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()).ToList();
        if (cleaned.Count == 0)
        {
            return [];
        }

        // Users often write "{file}" with quotes; we add the quoting ourselves.
        var template = (parameters?.Trim() ?? string.Empty)
            .Replace("\"" + FilesPlaceholder + "\"", FilesPlaceholder, StringComparison.OrdinalIgnoreCase)
            .Replace("\"" + FilePlaceholder + "\"", FilePlaceholder, StringComparison.OrdinalIgnoreCase);
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

    /// <summary>Quotes one argument following the rules of the Windows C runtime parser (backslashes before quotes are doubled).</summary>
    public static string Quote(string argument)
    {
        ArgumentNullException.ThrowIfNull(argument);
        var builder = new StringBuilder(argument.Length + 2);
        builder.Append('"');
        var i = 0;
        while (i < argument.Length)
        {
            var backslashes = 0;
            while (i < argument.Length && argument[i] == '\\')
            {
                backslashes++;
                i++;
            }

            if (i == argument.Length)
            {
                builder.Append('\\', backslashes * 2); // before the closing quote
            }
            else if (argument[i] == '"')
            {
                builder.Append('\\', backslashes * 2 + 1).Append('"');
                i++;
            }
            else
            {
                builder.Append('\\', backslashes).Append(argument[i]);
                i++;
            }
        }

        builder.Append('"');
        return builder.ToString();
    }
}
