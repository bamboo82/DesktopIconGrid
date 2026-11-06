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

    /// <summary>The extensions Windows tries for a name without one when <c>PATHEXT</c> is not set.</summary>
    private const string DefaultPathExtensions = ".COM;.EXE;.BAT;.CMD;.VBS;.VBE;.JS;.JSE;.WSF;.WSH;.MSC";

    /// <summary>
    /// U+FF02, the fullwidth quotation mark. Windows' best-fit conversion turns it into <c>"</c> for a program that reads
    /// its command line in an ANSI code page, which ends the argument it stood in (the "WorstFit" research, 2025).
    /// </summary>
    public const char FullwidthQuotationMark = '\uFF02';

    /// <summary>
    /// Who reads the filled command line, the strictest of what follows.
    /// <list type="bullet">
    /// <item>The program Windows runs for the location. cmd.exe reads it for a batch file (the shell runs it as
    /// <c>cmd /c</c>, and so does the log capture) and for cmd.exe itself. PowerShell reads it as script text for
    /// powershell.exe without <c>-File</c> and for pwsh with <c>-Command</c>/<c>-EncodedCommand</c>. A Linux shell
    /// reads it for wsl.exe without <c>-e</c>/<c>--exec</c> and for System32's bash.exe, the old WSL launcher. ssh
    /// sends the words after its destination to the remote shell as a command. Any other program parses it as
    /// arguments.</item>
    /// <item>A location without an extension is looked up as the shell does: under App Paths, in the start-in folder,
    /// then along PATH, each with every PATHEXT extension. Commands such as npm, npx, code or az are batch files there.
    /// It counts as cmd.exe when any match is a batch file, or when nothing is found. A shortcut file (<c>.lnk</c>) hands
    /// its arguments to a target that is not known here, so it counts as cmd.exe too.</item>
    /// <item>cmd.exe, PowerShell, WSL or ssh named in the parameters. Programs such as <c>wt.exe</c>,
    /// <c>conhost.exe</c> and <c>runas.exe</c> hand the rest of their command line to it.</item>
    /// </list>
    /// </summary>
    /// <param name="startIn">The folder the program starts in (the shell looks there for a name without a path).</param>
    /// <param name="registeredPathOf">The <c>App Paths</c> entry for a file name such as <c>code.exe</c>, or <see langword="null"/>.</param>
    /// <param name="searchPath">PATH (for tests); the process's own when <see langword="null"/>.</param>
    /// <param name="pathExtensions">PATHEXT (for tests); the process's own when <see langword="null"/>.</param>
    /// <param name="systemDirectory">System32 (for tests); Windows' own when <see langword="null"/>.</param>
    public static ArgumentReader ReaderOf(
        string? location,
        string? parameters,
        string? startIn = null,
        Func<string, string?>? registeredPathOf = null,
        string? searchPath = null,
        string? pathExtensions = null,
        string? systemDirectory = null)
    {
        var system = systemDirectory ?? Environment.SystemDirectory;
        var tokens = (parameters ?? string.Empty).Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        var reader = ReaderOfLocation(location, tokens, startIn, registeredPathOf, searchPath, pathExtensions, system);
        for (var i = 0; i < tokens.Length && reader != ArgumentReader.PowerShellCommand; i++)
        {
            var named = FileNameOf(tokens[i]);
            if (named is "cmd" or "cmd.exe")
            {
                reader = Stricter(reader, ArgumentReader.Cmd);
            }
            else if (named is "powershell" or "powershell.exe" or "pwsh" or "pwsh.exe")
            {
                reader = Stricter(reader, PowerShellReader(named, tokens[(i + 1)..]));
            }
            else if (named is "wsl" or "wsl.exe")
            {
                reader = Stricter(reader, WslReader(tokens[(i + 1)..]));
            }
            else if (named is "bash" or "bash.exe" && IsWslBash(tokens[i].Trim('"', '\''), system))
            {
                reader = Stricter(reader, ArgumentReader.PosixShell);
            }
            else if (named is "ssh" or "ssh.exe")
            {
                reader = Stricter(reader, SshReader(tokens[(i + 1)..]));
            }
        }

        return reader;
    }

    private static ArgumentReader ReaderOfLocation(
        string? location, string[] tokens, string? startIn, Func<string, string?>? registeredPathOf, string? searchPath, string? pathExtensions, string system)
    {
        var path = Environment.ExpandEnvironmentVariables(location?.Trim().Trim('"') ?? string.Empty);
        if (path.Length == 0 || LaunchPlanner.IsShellNamespace(path) || LaunchPlanner.IsSpecial(path) || LaunchPlanner.IsUri(path))
        {
            return ArgumentReader.Plain; // opened by the shell, which passes no command line to cmd.exe or PowerShell
        }

        var name = FileNameOf(path);
        if (name is "bash" && IsWslBash(path, system))
        {
            return ArgumentReader.PosixShell; // Windows looks in System32 before PATH
        }

        if (name is "cmd" or "powershell" or "pwsh" or "wsl" || Path.GetExtension(name).Length > 0)
        {
            return ReaderOfFile(path, tokens, system);
        }

        if (Directory.Exists(path))
        {
            return ArgumentReader.Plain; // a folder opens in Explorer
        }

        var found = Candidates(path, startIn, registeredPathOf, searchPath, pathExtensions).ToList();
        return found.Count == 0
            ? ArgumentReader.Cmd // unknown here: it may be a batch file where it runs
            : found.Select(file => ReaderOfFile(file, tokens, system)).Aggregate(Stricter);
    }

    /// <summary>
    /// The files the shell may run for <paramref name="path"/>, which has no extension: the App Paths entry of the name,
    /// then the name with each PATHEXT extension in the start-in folder and along PATH (a path is only tried where it
    /// points).
    /// </summary>
    private static IEnumerable<string> Candidates(
        string path, string? startIn, Func<string, string?>? registeredPathOf, string? searchPath, string? pathExtensions)
    {
        var extensions = (pathExtensions ?? Environment.GetEnvironmentVariable("PATHEXT") ?? DefaultPathExtensions)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(extension => extension.StartsWith('.'))
            .ToList();
        IEnumerable<string> Existing(string folderAndName)
        {
            foreach (var extension in extensions)
            {
                var candidate = folderAndName + extension;
                if (File.Exists(candidate))
                {
                    yield return candidate;
                }
                else if (File.Exists(folderAndName + extension.ToLowerInvariant()))
                {
                    yield return folderAndName + extension.ToLowerInvariant(); // a case-sensitive file system (tests)
                }
            }
        }

        var startFolder = string.IsNullOrWhiteSpace(startIn) ? null : Environment.ExpandEnvironmentVariables(startIn.Trim().Trim('"'));
        if (path.IndexOfAny(['\\', '/']) >= 0)
        {
            var full = Path.IsPathRooted(path) || startFolder is null ? path : Path.Combine(startFolder, path);
            foreach (var file in Existing(full))
            {
                yield return file;
            }

            yield break;
        }

        if (registeredPathOf?.Invoke(path + ".exe") is { } registered)
        {
            yield return registered;
        }

        var folders = (startFolder is null ? [] : new[] { startFolder })
            .Concat((searchPath ?? Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        foreach (var folder in folders)
        {
            foreach (var file in Existing(Path.Combine(folder, path)))
            {
                yield return file;
            }
        }
    }

    /// <summary>Who reads the command line of the file at <paramref name="path"/> (with its extension, or a name judged by itself).</summary>
    private static ArgumentReader ReaderOfFile(string path, string[] tokens, string system)
    {
        var name = FileNameOf(path);
        return name switch
        {
            _ when name.EndsWith(".bat", StringComparison.Ordinal) || name.EndsWith(".cmd", StringComparison.Ordinal) => ArgumentReader.Cmd,
            _ when name.EndsWith(".lnk", StringComparison.Ordinal) || name.EndsWith(".pif", StringComparison.Ordinal) => ArgumentReader.Cmd,
            "cmd" or "cmd.exe" => ArgumentReader.Cmd,
            "powershell" or "powershell.exe" or "pwsh" or "pwsh.exe" => PowerShellReader(name, tokens),
            "wsl" or "wsl.exe" => WslReader(tokens),
            "bash.exe" when IsWslBash(path, system) => ArgumentReader.PosixShell,
            "ssh.exe" => SshReader(tokens),
            _ => ArgumentReader.Plain,
        };
    }

    /// <summary>
    /// wsl.exe hands its command line, quotes and all, to the Linux shell: a quoted value stays one word there, but
    /// <c>$(…)</c>, <c>`…`</c> and <c>$NAME</c> are expanded inside the quotes. Not with <c>-e</c>/<c>--exec</c> among
    /// its own options (before the command starts): then it runs the program itself, the value one argument of it.
    /// </summary>
    private static ArgumentReader WslReader(IEnumerable<string> tokens)
    {
        using var token = tokens.GetEnumerator();
        while (token.MoveNext())
        {
            switch (token.Current.Trim('"').ToLowerInvariant())
            {
                case "-e" or "--exec":
                    return ArgumentReader.Plain;
                case "-d" or "--distribution" or "--distribution-id" or "-u" or "--user" or "--cd" or "--shell-type":
                    token.MoveNext(); // the option's value
                    break;
                case var option when option.StartsWith('-') && option != "--":
                    break; // another option of wsl.exe itself
                default:
                    return ArgumentReader.PosixShell; // the command line starts here ("--" passes the rest to the shell)
            }
        }

        return ArgumentReader.PosixShell;
    }

    /// <summary>
    /// ssh joins the words after the destination into one command for the remote shell, without the quotes, so a value
    /// there is read as bare words, as PowerShell reads script text (<c>ssh host cat {clipboard}</c>). A value up to the
    /// destination (the host, a login name) is an argument of ssh itself. A dropped file without <c>{file}</c> goes at the
    /// end, after the destination.
    /// </summary>
    private static ArgumentReader SshReader(IEnumerable<string> tokens)
    {
        const string optionsWithValue = "BbcDEeFIiJLlmOoPpQRSWw";
        var afterDestination = false;
        using var token = tokens.GetEnumerator();
        while (token.MoveNext())
        {
            var word = token.Current.Trim('"');
            if (afterDestination)
            {
                if (HoldsPlaceholder(word))
                {
                    return ArgumentReader.RemoteShell;
                }
            }
            else if (word == "--")
            {
                afterDestination = token.MoveNext(); // the next word is the destination
            }
            else if (word.Length > 1 && word[0] == '-')
            {
                var letter = word.IndexOfAny(optionsWithValue.ToCharArray(), 1);
                if (letter == word.Length - 1)
                {
                    token.MoveNext(); // -p 22, -vp 22: the value is the next word (-p22 holds its own)
                }
            }
            else
            {
                afterDestination = true; // this word is the destination
            }
        }

        return ArgumentReader.Plain;
    }

    private static bool HoldsPlaceholder(string word) =>
        word.Contains(ClipboardPlaceholder, StringComparison.OrdinalIgnoreCase)
        || word.Contains("{ask:", StringComparison.OrdinalIgnoreCase)
        || word.Contains(DropArguments.FilePlaceholder, StringComparison.OrdinalIgnoreCase)
        || word.Contains(DropArguments.FilesPlaceholder, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether <paramref name="path"/> is System32's bash.exe, the old WSL launcher: that folder, or no folder when
    /// System32 has one (Windows looks there before PATH, so a plain <c>bash</c> is WSL's even with Git Bash on PATH).
    /// </summary>
    private static bool IsWslBash(string path, string system)
    {
        if (string.IsNullOrEmpty(system))
        {
            return false;
        }

        static string Folder(string value) => value.Replace('/', '\\').TrimEnd('\\');
        var cut = path.LastIndexOfAny(['\\', '/']);
        return cut < 0
            ? File.Exists(Path.Combine(system, "bash.exe"))
            : string.Equals(Folder(path[..cut]), Folder(system), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Script text for powershell.exe unless it runs a script with <c>-File</c>; for pwsh only with a command switch.</summary>
    private static ArgumentReader PowerShellReader(string name, IEnumerable<string> tokens)
    {
        var switches = tokens
            .Where(token => token.Length > 1 && token[0] is '-' or '/')
            .Select(token => token[1..].Trim('"').ToLowerInvariant())
            .ToList();
        bool Any(string switchName, int shortest) => switches.Any(s => s.Length >= shortest && switchName.StartsWith(s, StringComparison.Ordinal));
        var runsCommand = Any("command", 1) || Any("commandwithargs", 8) || switches.Contains("cwa") || switches.Contains("e") || switches.Contains("ec") || Any("encodedcommand", 2);
        return name.StartsWith("pwsh", StringComparison.Ordinal)
            ? runsCommand ? ArgumentReader.PowerShellCommand : ArgumentReader.Plain
            : Any("file", 1) && !runsCommand ? ArgumentReader.Plain : ArgumentReader.PowerShellCommand;
    }

    private static string FileNameOf(string pathOrToken)
    {
        var trimmed = pathOrToken.Trim().Trim('"', '\'');
        return trimmed[(trimmed.LastIndexOfAny(['\\', '/']) + 1)..].ToLowerInvariant();
    }

    /// <summary>The stricter of two readers: <see cref="ArgumentReader"/> is ordered from the most to the least lenient.</summary>
    private static ArgumentReader Stricter(ArgumentReader a, ArgumentReader b) => a > b ? a : b;

    /// <summary>
    /// The first character (a surrogate pair counts as one) of <paramref name="value"/> that the program could act on,
    /// or <see langword="null"/> when the value is safe to pass. Such values are refused, never escaped.
    /// <list type="bullet">
    /// <item>Every program: a line break, and <see cref="FullwidthQuotationMark"/>.</item>
    /// <item>cmd.exe: also <c>" % ! ^ &amp; | &lt; &gt;</c>.</item>
    /// <item>A Linux shell through WSL: also <c>$</c> and <c>`</c>, which are expanded inside the quotes it keeps
    /// (<c>wsl.exe echo "$(echo hi)"</c> prints hi).</item>
    /// <item>PowerShell script text: everything but letters, digits, the space and <c>. _ - \ / :</c>, and a <c>-</c> that
    /// starts a word after a space. powershell.exe and pwsh take the quotes off their arguments before joining them into
    /// the script, so the value is read as bare words. There <c>(…)</c>, <c>|</c>, <c>&amp;</c>, <c>;</c>, <c>{…}</c>,
    /// <c>@</c>, quotes of every kind (also U+2018–U+201E) and more have a meaning, and every word that starts with
    /// <c>-</c> is a parameter (<c>Remove-Item x -Recurse</c>). At the start of the value the option rule decides, by
    /// where the placeholder stands (<see cref="TakesOptionAt"/>). The same for the command ssh sends to the remote
    /// shell, which also gets bare words.</item>
    /// </list>
    /// </summary>
    public static string? Refused(string value, ArgumentReader reader)
    {
        ArgumentNullException.ThrowIfNull(value);
        return FirstNotAllowed(value, reader switch
        {
            ArgumentReader.Cmd => IsAllowedForCmd,
            ArgumentReader.PosixShell => IsAllowedForPosixShell,
            ArgumentReader.RemoteShell or ArgumentReader.PowerShellCommand => IsAllowedInScriptText,
            _ => IsAllowedForAny,
        });
    }

    /// <summary>
    /// What a dropped file's path holds that the program would act on even inside the quotes the path is given, or
    /// <see langword="null"/>. A file name cannot hold <c>"</c>, so it never leaves its quotes, but cmd.exe still expands
    /// <c>%NAME%</c> there, and a Linux shell through WSL expands <c>$</c> and <c>`</c> there. PowerShell script text gets
    /// the path without its quotes (see <see cref="Refused"/>):
    /// <c>a;Write-Output x;.txt</c> is a legal file name. <see cref="FullwidthQuotationMark"/> is refused for every
    /// program.
    /// </summary>
    public static string? RefusedInPath(string path, ArgumentReader reader)
    {
        ArgumentNullException.ThrowIfNull(path);
        return FirstNotAllowed(path, reader switch
        {
            ArgumentReader.Cmd => (text, index) => text[index] is not ('%' or FullwidthQuotationMark),
            ArgumentReader.PosixShell => (text, index) => text[index] is not ('%' or '$' or '`' or FullwidthQuotationMark),
            ArgumentReader.RemoteShell or ArgumentReader.PowerShellCommand => IsAllowedInScriptText,
            _ => (text, index) => text[index] != FullwidthQuotationMark,
        });
    }

    /// <summary>
    /// Whether a value that starts with <c>-</c> would reach the program as an option where
    /// <paramref name="placeholderIndex"/> stands in <paramref name="template"/>: the placeholder starts a word (nothing
    /// before it but a quote), and no <c>--</c> word comes before it. Most programs read such a word as an option, for
    /// example <c>python "-cprint(1+1)"</c> runs the code, and <c>git clone "--upload-pack=…"</c> runs a command.
    /// </summary>
    public static bool TakesOptionAt(string template, int placeholderIndex)
    {
        ArgumentNullException.ThrowIfNull(template);
        var before = template[..placeholderIndex];
        if (before.Length > 0 && !char.IsWhiteSpace(before[^1]))
        {
            return false; // part of a word, such as --name={clipboard}
        }

        return !before.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries).Any(word => word.Trim('"') == "--");
    }

    /// <summary>Where the <c>{clipboard}</c> placeholders stand in <paramref name="template"/> (each with the quote written before it, if any).</summary>
    public static IReadOnlyList<int> ClipboardPositions(string template) =>
        QuotedClipboard().Matches(template ?? string.Empty).Select(m => m.Index).ToList();

    /// <summary>Where the <c>{ask:…}</c> placeholders stand in <paramref name="template"/>, in order.</summary>
    public static IReadOnlyList<int> AskPositions(string template) =>
        AskPattern().Matches(template ?? string.Empty).Select(m => m.Index).ToList();

    private static bool IsAllowedForAny(string text, int index) => text[index] is not ('\r' or '\n' or FullwidthQuotationMark);

    private static bool IsAllowedForCmd(string text, int index) => IsAllowedForAny(text, index) && "\"%!^&|<>".IndexOf(text[index]) < 0;

    /// <summary>What cmd.exe takes, less <c>$</c> and <c>`</c>, so the readers stay in order of strictness (<c>cmd /c wsl …</c>).</summary>
    private static bool IsAllowedForPosixShell(string text, int index) => IsAllowedForCmd(text, index) && text[index] is not ('$' or '`');

    private static bool IsAllowedInScriptText(string text, int index) => text[index] == '-'
        ? index == 0 || text[index - 1] != ' ' // a word of its own that starts with "-" is a parameter
        : char.IsLetterOrDigit(text, index) || text[index] is ' ' or '.' or '_' or '\\' or '/' or ':';

    private static string? FirstNotAllowed(string text, Func<string, int, bool> allowed)
    {
        for (var i = 0; i < text.Length; i += char.IsSurrogatePair(text, i) ? 2 : 1)
        {
            if (!allowed(text, i))
            {
                return char.IsSurrogatePair(text, i) ? text.Substring(i, 2) : text[i].ToString();
            }
        }

        return null;
    }


    [GeneratedRegex("\"?\\{ask:(?<prompt>[^}]*)\\}\"?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AskPattern();

    [GeneratedRegex("\"?\\{clipboard\\}\"?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex QuotedClipboard();
}

/// <summary>
/// How the program that receives a shortcut's filled parameters reads them (<see cref="LaunchPlaceholders.ReaderOf"/>),
/// from the most lenient to the strictest.
/// </summary>
public enum ArgumentReader
{
    /// <summary>The program's own argument parser: a quoted value is one argument, whatever it holds.</summary>
    Plain,

    /// <summary>cmd.exe, where quotes, <c>%</c>, <c>&amp;</c> and the like can end the argument and start a command.</summary>
    Cmd,

    /// <summary>
    /// A Linux shell through WSL (wsl.exe without <c>-e</c>, System32's bash.exe). It gets the command line with its
    /// quotes, so a value stays one word, but expands <c>$(…)</c>, <c>`…`</c> and <c>$NAME</c> inside them. What cmd.exe
    /// refuses is refused too, so each reader refuses at least what the one before it does.
    /// </summary>
    PosixShell,

    /// <summary>
    /// The remote shell ssh sends its command to: the words after the destination, joined without their quotes. A value
    /// there is checked as PowerShell script text is.
    /// </summary>
    RemoteShell,

    /// <summary>PowerShell reading its arguments, without their quotes, as script text, where <c>(…)</c> and <c>$(…)</c> run.</summary>
    PowerShellCommand,
}

