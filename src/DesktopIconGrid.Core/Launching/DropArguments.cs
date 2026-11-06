using System.Text;
using DesktopIconGrid.Core.Models;

namespace DesktopIconGrid.Core.Launching;

/// <summary>
/// Builds the command-line arguments used when files are dropped onto a shortcut ("open with").
/// <c>{file}</c> in the shortcut's parameters is replaced per file (one launch each), <c>{files}</c>
/// by all files at once; without a placeholder the files are appended to the parameters.
/// Paths are quoted the way Windows' command-line parser expects (so <c>C:\</c> stays <c>C:\</c>).
/// </summary>
public static partial class DropArguments
{
    public const string FilePlaceholder = "{file}";
    public const string FilesPlaceholder = "{files}";

    /// <summary>
    /// The parameters for a click, which brings no files: <c>{file}</c> and <c>{files}</c> (with the quotes written around
    /// them, and the space before) taken out, so the program does not get them as words. <see langword="null"/> when
    /// nothing is left.
    /// </summary>
    public static string? WithoutFilePlaceholders(string? parameters)
    {
        if (parameters is null)
        {
            return null;
        }

        var stripped = FilePlaceholderPattern().Replace(parameters, string.Empty).Trim();
        return stripped.Length == 0 ? null : stripped;
    }

    /// <summary>Programs, scripts and documents can open dropped files; links, shell folders and special actions cannot.</summary>
    public static bool SupportsOpenWith(ShortcutItem shortcut)
    {
        var location = shortcut?.EffectiveLocation?.Trim();
        return !string.IsNullOrEmpty(location)
            && !LaunchPlanner.IsSpecial(location)
            && !LaunchPlanner.IsShellNamespace(location)
            && !LaunchPlanner.IsUri(location);
    }

    /// <summary>
    /// One argument string per launch, with the files and, when given, <paramref name="clipboard"/> and
    /// <paramref name="answers"/> put into <paramref name="parameters"/> in one pass (<see cref="LaunchPlaceholders.Fill"/>):
    /// a dropped path goes only where the template has <c>{file}</c> or <c>{files}</c>, never inside a value that holds
    /// one. <paramref name="parameters"/> should already have environment variables expanded.
    /// </summary>
    public static IReadOnlyList<string> BuildArgumentSets(string? parameters, IReadOnlyList<string> paths, string? clipboard = null, IReadOnlyList<string>? answers = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var cleaned = paths.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()).ToList();
        if (cleaned.Count == 0)
        {
            return [];
        }

        // Users often write "{file}" with quotes; Fill drops them and the path brings its own.
        var template = parameters?.Trim() ?? string.Empty;
        var all = string.Join(' ', cleaned.Select(Quote));
        answers ??= [];

        if (template.Contains(FilesPlaceholder, StringComparison.OrdinalIgnoreCase))
        {
            return [LaunchPlaceholders.Fill(template, clipboard, answers, files: all)];
        }

        if (template.Contains(FilePlaceholder, StringComparison.OrdinalIgnoreCase))
        {
            return cleaned.Select(path => LaunchPlaceholders.Fill(template, clipboard, answers, file: Quote(path))).ToList();
        }

        var filled = LaunchPlaceholders.Fill(template, clipboard, answers);
        return [filled.Length == 0 ? all : $"{filled} {all}"];
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

    [System.Text.RegularExpressions.GeneratedRegex("\\s*\"?\\{files?\\}\"?", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant)]
    private static partial System.Text.RegularExpressions.Regex FilePlaceholderPattern();
}
