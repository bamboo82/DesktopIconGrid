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

    /// <summary>
    /// Who reads the filled command line: cmd.exe for a batch file (the shell runs it as <c>cmd /c</c>, and so does the
    /// log capture) and for cmd.exe itself; PowerShell as script text for powershell.exe without <c>-File</c> and for pwsh
    /// with <c>-Command</c>/<c>-EncodedCommand</c>; otherwise the program's own parser, which takes the quoted value as
    /// one argument.
    /// </summary>
    public static ArgumentReader ReaderOf(string? location, string? parameters)
    {
        var path = Environment.ExpandEnvironmentVariables(location?.Trim().Trim('"') ?? string.Empty);
        var file = path[(path.LastIndexOfAny(['\\', '/']) + 1)..].ToLowerInvariant();
        if (file.EndsWith(".bat", StringComparison.Ordinal) || file.EndsWith(".cmd", StringComparison.Ordinal) || file is "cmd" or "cmd.exe")
        {
            return ArgumentReader.Cmd;
        }

        var switches = (parameters ?? string.Empty)
            .Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries)
            .Where(token => token.Length > 1 && token[0] is '-' or '/')
            .Select(token => token[1..].ToLowerInvariant())
            .ToList();
        bool Any(string name, int shortest) => switches.Any(s => s.Length >= shortest && name.StartsWith(s, StringComparison.Ordinal));
        var runsCommand = Any("command", 1) || Any("commandwithargs", 8) || switches.Contains("cwa") || switches.Contains("e") || switches.Contains("ec") || Any("encodedcommand", 2);
        return file switch
        {
            "powershell" or "powershell.exe" => Any("file", 1) && !runsCommand ? ArgumentReader.Plain : ArgumentReader.PowerShellCommand,
            "pwsh" or "pwsh.exe" => runsCommand ? ArgumentReader.PowerShellCommand : ArgumentReader.Plain,
            _ => ArgumentReader.Plain,
        };
    }

    /// <summary>
    /// The first character of <paramref name="value"/> that <paramref name="reader"/> could act on, or <see langword="null"/>
    /// when the value is safe to pass. Escaping for cmd.exe is not reliable, so such values are refused, not escaped:
    /// for cmd.exe <c>" % ! ^ &amp; | &lt; &gt;</c>, for PowerShell script text <c>$ ` ; ' "</c>, and for every program a
    /// line break.
    /// </summary>
    public static char? Refused(string value, ArgumentReader reader)
    {
        ArgumentNullException.ThrowIfNull(value);
        var refused = reader switch
        {
            ArgumentReader.Cmd => CmdSpecial,
            ArgumentReader.PowerShellCommand => PowerShellSpecial,
            _ => LineBreaks,
        };
        var index = value.AsSpan().IndexOfAny(refused);
        return index < 0 ? null : value[index];
    }

    private static readonly System.Buffers.SearchValues<char> LineBreaks = System.Buffers.SearchValues.Create("\r\n");
    private static readonly System.Buffers.SearchValues<char> CmdSpecial = System.Buffers.SearchValues.Create("\"%!^&|<>\r\n");
    private static readonly System.Buffers.SearchValues<char> PowerShellSpecial = System.Buffers.SearchValues.Create("$`;'\"\r\n");

    [GeneratedRegex("\"?\\{ask:(?<prompt>[^}]*)\\}\"?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AskPattern();

    [GeneratedRegex("\"?\\{clipboard\\}\"?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex QuotedClipboard();
}

/// <summary>How the program that receives a shortcut's filled parameters reads them (<see cref="LaunchPlaceholders.ReaderOf"/>).</summary>
public enum ArgumentReader
{
    /// <summary>The program's own argument parser: a quoted value is one argument, whatever it holds.</summary>
    Plain,

    /// <summary>cmd.exe, where quotes, <c>%</c>, <c>&amp;</c> and the like can end the argument and start a command.</summary>
    Cmd,

    /// <summary>PowerShell reading its arguments as script text, where <c>$(…)</c> runs.</summary>
    PowerShellCommand,
}

