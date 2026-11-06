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
    /// <returns>The strictest reader of any placeholder in <paramref name="parameters"/> (see <see cref="ReaderAt"/>).</returns>
    public static ArgumentReader ReaderOf(
        string? location,
        string? parameters,
        string? startIn = null,
        Func<string, string?>? registeredPathOf = null,
        string? searchPath = null,
        string? pathExtensions = null,
        string? systemDirectory = null)
    {
        var positions = ClipboardPositions(parameters ?? string.Empty).Concat(AskPositions(parameters ?? string.Empty)).Concat(FilePositions(parameters)).ToList();
        if (positions.Count == 0)
        {
            positions.Add((parameters ?? string.Empty).Length); // a dropped file goes at the end
        }

        return positions
            .Select(index => ReaderAt(location, parameters, index, startIn, registeredPathOf, searchPath, pathExtensions, systemDirectory))
            .Aggregate(Stricter);
    }

    /// <summary>
    /// Who reads the value of the placeholder that starts at <paramref name="placeholderIndex"/> in
    /// <paramref name="parameters"/> (their length for files dropped at the end), by the rules of <see cref="ReaderOf"/>.
    /// ssh, plink and wsl.exe tell their own options, the destination and the command apart, so a placeholder is judged
    /// where it stands there: <c>ssh {ask:Host} tail {clipboard}</c> takes <c>admin@host</c> for the host and bare words
    /// after it. Where the parse cannot tell, the stricter reader is taken.
    /// </summary>
    public static ArgumentReader ReaderAt(
        string? location,
        string? parameters,
        int placeholderIndex,
        string? startIn = null,
        Func<string, string?>? registeredPathOf = null,
        string? searchPath = null,
        string? pathExtensions = null,
        string? systemDirectory = null)
    {
        var system = systemDirectory ?? Environment.SystemDirectory;
        var words = Words(parameters);
        var at = words.FindIndex(word => word.Start <= placeholderIndex && placeholderIndex < word.End);
        if (at < 0)
        {
            at = words.Count(word => word.Start < placeholderIndex); // at the end: after every word
        }

        var reader = ReaderOfLocation(location, words, at, startIn, registeredPathOf, searchPath, pathExtensions, system);
        for (var i = 0; i < words.Count && reader != ArgumentReader.PowerShellCommand; i++)
        {
            var named = FileNameOf(words[i].Text);
            var rest = words.GetRange(i + 1, words.Count - i - 1);
            var restAt = at - i - 1; // the placeholder's place among the words after this one; negative when it comes before
            reader = Stricter(reader, named switch
            {
                "cmd" or "cmd.exe" => ArgumentReader.Cmd,
                "powershell" or "powershell.exe" or "pwsh" or "pwsh.exe" => PowerShellReader(named, rest.Select(word => word.Text)),
                "wsl" or "wsl.exe" => restAt >= 0 ? WslReader(rest, restAt) : ArgumentReader.PosixShell,
                "bash" or "bash.exe" when IsWslBash(words[i].Bare.Trim('\''), system) => ArgumentReader.PosixShell,
                "ssh" or "ssh.exe" => restAt >= 0 ? SshReader(rest, restAt) : ArgumentReader.RemoteShell,
                "plink" or "plink.exe" => restAt >= 0 ? PlinkReader(rest, restAt) : ArgumentReader.RemoteShell,
                _ => ArgumentReader.Plain,
            });
        }

        return reader;
    }

    /// <summary>Where the <c>{file}</c> and <c>{files}</c> placeholders stand in <paramref name="template"/> (each with the quote written before it, if any).</summary>
    public static IReadOnlyList<int> FilePositions(string? template) =>
        QuotedFiles().Matches(template ?? string.Empty).Select(m => m.Index).ToList();

    /// <summary>A word of the parameters and where it starts. A prompt with spaces in it (<c>{ask:Which host}</c>) is one word.</summary>
    private readonly record struct Word(string Text, int Start)
    {
        public int End => Start + Text.Length;

        public string Bare => Text.Trim('"');
    }

    private static List<Word> Words(string? parameters) =>
        WordPattern().Matches(parameters ?? string.Empty).Select(m => new Word(m.Value, m.Index)).ToList();

    /// <summary>
    /// Whether Windows Terminal reads the command line: <c>wt.exe</c> as the location or named in the parameters. It
    /// splits its arguments at every <c>;</c>, inside quotes too, and runs what follows as a command of its own
    /// (<c>wt ssh "host;calc"</c> opens a second tab with calc), so no value or dropped path may hold one there.
    /// </summary>
    public static bool SplitsAtSemicolons(string? location, string? parameters)
    {
        static bool IsTerminal(string token) => FileNameOf(token) is "wt" or "wt.exe";
        return IsTerminal(Environment.ExpandEnvironmentVariables(location ?? string.Empty))
               || (parameters ?? string.Empty).Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries).Any(IsTerminal);
    }

    private static ArgumentReader ReaderOfLocation(
        string? location, List<Word> words, int at, string? startIn, Func<string, string?>? registeredPathOf, string? searchPath, string? pathExtensions, string system)
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
            return ReaderOfFile(path, words, at, system);
        }

        if (Directory.Exists(path))
        {
            return ArgumentReader.Plain; // a folder opens in Explorer
        }

        var found = Candidates(path, startIn, registeredPathOf, searchPath, pathExtensions).ToList();
        return found.Count == 0
            ? ArgumentReader.Cmd // unknown here: it may be a batch file where it runs
            : found.Select(file => ReaderOfFile(file, words, at, system)).Aggregate(Stricter);
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
    private static ArgumentReader ReaderOfFile(string path, List<Word> words, int at, string system)
    {
        var name = FileNameOf(path);
        return name switch
        {
            _ when name.EndsWith(".bat", StringComparison.Ordinal) || name.EndsWith(".cmd", StringComparison.Ordinal) => ArgumentReader.Cmd,
            _ when name.EndsWith(".lnk", StringComparison.Ordinal) || name.EndsWith(".pif", StringComparison.Ordinal) => ArgumentReader.Cmd,
            "cmd" or "cmd.exe" => ArgumentReader.Cmd,
            "powershell" or "powershell.exe" or "pwsh" or "pwsh.exe" => PowerShellReader(name, words.Select(word => word.Text)),
            "wsl" or "wsl.exe" => WslReader(words, at),
            "bash.exe" when IsWslBash(path, system) => ArgumentReader.PosixShell,
            "ssh.exe" => SshReader(words, at),
            "plink.exe" => PlinkReader(words, at),
            _ => ArgumentReader.Plain,
        };
    }

    /// <summary>
    /// wsl.exe hands its command line, quotes and all, to the Linux shell: a quoted value stays one word there, but
    /// <c>$(…)</c>, <c>`…`</c> and <c>$NAME</c> are expanded inside the quotes. Not with <c>-e</c>/<c>--exec</c> or
    /// <c>--shell-type none</c> among its own options (before the command starts): then it runs the program itself, the
    /// value one argument of it. The value of one of its own options (<c>-d</c>, <c>-u</c>, <c>--cd</c>) is wsl's.
    /// </summary>
    /// <param name="words">The words after <c>wsl</c>.</param>
    /// <param name="at">Which of them the placeholder is in (their count: after all of them).</param>
    private static ArgumentReader WslReader(IReadOnlyList<Word> words, int at)
    {
        var noShell = false;
        for (var j = 0; j < words.Count && j < at; j++)
        {
            switch (words[j].Bare.ToLowerInvariant())
            {
                case "-e" or "--exec":
                    return ArgumentReader.Plain; // the program and its arguments follow
                case "-d" or "--distribution" or "--distribution-id" or "-u" or "--user" or "--cd" or "--shell-type" when j + 1 == at:
                    return ArgumentReader.Plain; // the placeholder is the option's value
                case "--shell-type":
                    noShell = words[j + 1].Bare.Equals("none", StringComparison.OrdinalIgnoreCase);
                    j++;
                    break;
                case "-d" or "--distribution" or "--distribution-id" or "-u" or "--user" or "--cd":
                    j++; // the option's value
                    break;
                case var option when option.StartsWith('-') && option != "--":
                    break; // another option of wsl.exe itself
                default:
                    return noShell ? ArgumentReader.Plain : ArgumentReader.PosixShell; // the command starts before the placeholder ("--" passes the rest on)
            }
        }

        return noShell ? ArgumentReader.Plain : ArgumentReader.PosixShell; // the placeholder starts the command
    }

    /// <summary>The options of ssh that take a value (OpenSSH's option string).</summary>
    private const string SshOptionsWithValue = "BbcDEeFIiJLlmOoPpQRSWw";

    /// <summary>
    /// ssh joins the words after the destination into one command for the remote shell, without the quotes, so a value
    /// there is read as bare words, as PowerShell reads script text (<c>ssh host cat {clipboard}</c>). The destination is
    /// one argument of ssh, but ssh can put it into a command of its own (<see cref="ArgumentReader.SshDestination"/>),
    /// and so for <c>-J</c> and <c>-l</c>. <c>-o</c> takes ssh_config text, which can be a command
    /// (<c>ProxyCommand=…</c>); other values (<c>-p 22</c>, <c>-i key</c>, <c>-F file</c>) are plain.
    /// </summary>
    /// <param name="words">The words after <c>ssh</c>.</param>
    /// <param name="at">Which of them the placeholder is in (their count: after all of them).</param>
    private static ArgumentReader SshReader(IReadOnlyList<Word> words, int at)
    {
        for (var j = 0; j < words.Count && j <= at; j++)
        {
            var word = words[j].Bare;
            if (j == at)
            {
                return word.StartsWith('-') ? ArgumentReader.RemoteShell : ArgumentReader.SshDestination; // an option with the value attached, or the destination
            }

            if (word == "--")
            {
                return at == j + 1 ? ArgumentReader.SshDestination : ArgumentReader.RemoteShell;
            }

            if (word.Length > 1 && word[0] == '-')
            {
                var letter = word.IndexOfAny(SshOptionsWithValue.ToCharArray(), 1);
                if (letter == word.Length - 1)
                {
                    if (j + 1 == at)
                    {
                        return word[letter] switch
                        {
                            'o' => ArgumentReader.RemoteShell,
                            'J' or 'l' => ArgumentReader.SshDestination,
                            _ => ArgumentReader.Plain,
                        };
                    }

                    j++; // -p 22, -vp 22: the value is the next word (-p22 holds its own)
                }

                continue;
            }

            return ArgumentReader.RemoteShell; // the destination comes before the placeholder: the command
        }

        return ArgumentReader.SshDestination; // a file dropped where the destination goes
    }

    /// <summary>plink's options that take a value (PuTTY 0.8x), and its flags.</summary>
    private static readonly HashSet<string> PlinkOptionsWithValue = new(StringComparer.Ordinal)
    {
        "-load", "-P", "-l", "-pw", "-pwfile", "-proxycmd", "-sercfg", "-D", "-L", "-R", "-i", "-hostkey", "-m", "-nc", "-sshlog", "-sshrawlog",
    };

    private static readonly HashSet<string> PlinkFlags = new(StringComparer.Ordinal)
    {
        "-V", "-pgpfp", "-v", "-ssh", "-telnet", "-rlogin", "-raw", "-serial", "-ssh-connection", "-batch", "-X", "-x", "-A", "-a", "-t", "-T",
        "-1", "-2", "-4", "-6", "-C", "-noagent", "-agent", "-no-trivial-auth", "-noshare", "-share", "-sanitise-stderr", "-sanitise-stdout",
        "-no-sanitise-stderr", "-no-sanitise-stdout", "-no-antispoof", "-s", "-N", "-logoverwrite", "-logappend", "-shareexists",
    };

    /// <summary>
    /// plink, PuTTY's command-line ssh, as ssh: the words after the host are the remote command, the host and
    /// <c>-l</c>/<c>-nc</c> are destinations, <c>-proxycmd</c> is a command line. After an option it does not know, where
    /// the host is cannot be told, so a placeholder is read as the command.
    /// </summary>
    private static ArgumentReader PlinkReader(IReadOnlyList<Word> words, int at)
    {
        var unsure = false;
        for (var j = 0; j < words.Count && j <= at; j++)
        {
            var word = words[j].Bare;
            if (j == at)
            {
                return unsure || word.StartsWith('-') ? ArgumentReader.RemoteShell : ArgumentReader.SshDestination;
            }

            if (word.Length > 1 && word[0] == '-')
            {
                if (PlinkOptionsWithValue.Contains(word))
                {
                    if (j + 1 == at)
                    {
                        return word switch
                        {
                            "-proxycmd" => ArgumentReader.RemoteShell,
                            "-l" or "-nc" => ArgumentReader.SshDestination,
                            _ => ArgumentReader.Plain,
                        };
                    }

                    j++;
                }
                else if (!PlinkFlags.Contains(word))
                {
                    unsure = true; // it may take a value
                }

                continue;
            }

            return ArgumentReader.RemoteShell; // the host comes before the placeholder: the command
        }

        return unsure ? ArgumentReader.RemoteShell : ArgumentReader.SshDestination;
    }

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
    /// <item>ssh's destination: only what <c>user@host</c>, <c>[v6]:port</c> and an <c>ssh://</c> URI need.</item>
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
            ArgumentReader.SshDestination => IsAllowedInSshDestination,
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
            ArgumentReader.SshDestination => IsAllowedInSshDestination,
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

    /// <summary>
    /// What <c>user@host</c>, <c>[v6]:port</c> and an <c>ssh://</c> URI need: ASCII letters and digits and
    /// <c>. - _ @ : [ ] /</c>. Nothing a shell, or ssh's own <c>%h</c>/<c>%u</c> in a <c>ProxyCommand</c>, could act on
    /// (CVE-2023-51385, where OpenSSH before 9.6 passed such names on).
    /// </summary>
    private static bool IsAllowedInSshDestination(string text, int index) =>
        char.IsAsciiLetterOrDigit(text[index]) || text[index] is '.' or '-' or '_' or '@' or ':' or '[' or ']' or '/';

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

    [GeneratedRegex("\"?\\{files?\\}\"?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex QuotedFiles();

    [GeneratedRegex("(?:\\{ask:[^}]*\\}|\\S)+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WordPattern();
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
    /// ssh's destination, a login name or a jump host: one argument of ssh, but ssh can put it into a command of its own
    /// (<c>%h</c> and <c>%u</c> in <c>ProxyCommand</c> and <c>Match exec</c>, CVE-2023-51385), so only what
    /// <c>user@host</c>, <c>[v6]:port</c> and an <c>ssh://</c> URI need passes. It refuses every character a reader
    /// before it does; the readers after it take a space or a backslash it does not, and refuse <c>@</c>, which neither
    /// lets a value out of its argument.
    /// </summary>
    SshDestination,

    /// <summary>
    /// The remote shell ssh sends its command to: the words after the destination, joined without their quotes. A value
    /// there is checked as PowerShell script text is.
    /// </summary>
    RemoteShell,

    /// <summary>PowerShell reading its arguments, without their quotes, as script text, where <c>(…)</c> and <c>$(…)</c> run.</summary>
    PowerShellCommand,
}

