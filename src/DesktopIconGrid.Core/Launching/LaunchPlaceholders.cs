using System.Text.RegularExpressions;

namespace DesktopIconGrid.Core.Launching;

/// <summary>
/// <c>{clipboard}</c> and <c>{ask:Prompt}</c> in a shortcut's parameters, filled in when it is launched: the clipboard
/// text, and what the user types in answer to the prompt. Each value is quoted the way dropped paths are
/// (<see cref="DropArguments.Quote"/>), so spaces and quotes reach the program as one argument.
/// </summary>
public static partial class LaunchPlaceholders
{
    public const string ClipboardPlaceholder = "{clipboard}";

    /// <summary>True when the parameters ask for the clipboard or for an answer.</summary>
    public static bool HasAny(string? parameters) =>
        !string.IsNullOrEmpty(parameters)
        && (parameters.Contains(ClipboardPlaceholder, StringComparison.OrdinalIgnoreCase) || AskPattern().IsMatch(parameters));

    public static bool UsesClipboard(string? parameters) =>
        parameters?.Contains(ClipboardPlaceholder, StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>The prompts of the <c>{ask:…}</c> placeholders, in order (an empty prompt is allowed).</summary>
    public static IReadOnlyList<string> Prompts(string? parameters) =>
        string.IsNullOrEmpty(parameters)
            ? []
            : AskPattern().Matches(parameters).Select(m => m.Groups["prompt"].Value.Trim()).ToList();

    /// <summary>
    /// Fills in the placeholders: <paramref name="clipboard"/> for every <c>{clipboard}</c>, and the answers for the
    /// <c>{ask:…}</c> placeholders in order. Quotes written around a placeholder are dropped, since the value is quoted
    /// here. <paramref name="parameters"/> should already have its environment variables expanded, so a <c>%</c> in a
    /// value is never expanded.
    /// </summary>
    public static string Fill(string parameters, string? clipboard, IReadOnlyList<string> answers)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(answers);
        var next = 0;
        var filled = AskPattern().Replace(parameters, _ =>
        {
            if (next >= answers.Count)
            {
                throw new ArgumentException("Fewer answers than {ask:…} placeholders.", nameof(answers));
            }

            return DropArguments.Quote(answers[next++]);
        });
        return QuotedClipboard().Replace(filled, _ => DropArguments.Quote(clipboard ?? string.Empty));
    }

    [GeneratedRegex("\"?\\{ask:(?<prompt>[^}]*)\\}\"?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AskPattern();

    [GeneratedRegex("\"?\\{clipboard\\}\"?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex QuotedClipboard();
}
