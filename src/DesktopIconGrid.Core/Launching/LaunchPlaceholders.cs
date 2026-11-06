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
    /// Fills in the placeholders in one pass over <paramref name="parameters"/>: <paramref name="clipboard"/> for every
    /// <c>{clipboard}</c>, the answers for the <c>{ask:…}</c> placeholders in order, and, when files were dropped,
    /// <paramref name="file"/> for <c>{file}</c> and <paramref name="files"/> for <c>{files}</c> (left as written when
    /// <see langword="null"/>). Each value is quoted as one argument (<see cref="DropArguments.Quote"/>). Nothing put in is
    /// read again: a value that holds <c>{file}</c> or <c>{clipboard}</c> keeps it as text, where it was checked. Where a
    /// placeholder stands is read as the C runtime reads a command line (<see cref="SpotsOf"/>): quotes written as a pair
    /// around the placeholder alone (<c>"{x}"</c>) give way to the value's own, and a run of backslashes right before a
    /// placeholder is doubled, since the value's quote now follows it. A placeholder inside quotes with other text
    /// (<c>"--in={file}"</c>) cannot be filled: its value's quotes would end the span (see <see cref="InsideQuotes"/>).
    /// <paramref name="parameters"/> should already have its environment variables expanded, so a <c>%</c> in a value
    /// is never expanded.
    /// </summary>
    public static string Fill(string parameters, string? clipboard, IReadOnlyList<string> answers, string? file = null, IReadOnlyList<string>? files = null)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(answers);
        var next = 0;
        var builder = new System.Text.StringBuilder(parameters.Length + 64);
        var last = 0;
        foreach (var spot in SpotsOf(parameters))
        {
            var (start, end) = spot.Kind == PlaceholderSpot.QuotedAlone ? (spot.Start - 1, spot.End + 1) : (spot.Start, spot.End);
            builder.Append(parameters, last, start - last);
            last = end;
            var name = parameters[spot.Start..spot.End];
            string? value;
            if (name.Equals(ClipboardPlaceholder, StringComparison.OrdinalIgnoreCase))
            {
                value = DropArguments.Quote(clipboard ?? string.Empty);
            }
            else if (name.Equals(DropArguments.FilesPlaceholder, StringComparison.OrdinalIgnoreCase))
            {
                value = files is null ? null : string.Join(' ', files.Select(DropArguments.Quote));
            }
            else if (name.Equals(DropArguments.FilePlaceholder, StringComparison.OrdinalIgnoreCase))
            {
                value = file is null ? null : DropArguments.Quote(file);
            }
            else
            {
                if (next >= answers.Count)
                {
                    throw new ArgumentException("Fewer answers than {ask:…} placeholders.", nameof(answers));
                }

                value = DropArguments.Quote(answers[next++]);
            }

            if (value is null)
            {
                builder.Append(parameters, start, end - start); // no files: as written
                continue;
            }

            if (spot.Kind == PlaceholderSpot.InsideQuotes)
            {
                throw new ArgumentException("A placeholder inside quotes with other text cannot be filled; such a template is refused first.", nameof(parameters));
            }

            builder.Append('\\', spot.Backslashes).Append(value);
        }

        return builder.Append(parameters, last, parameters.Length - last).ToString();
    }

    /// <summary>Where a placeholder stands among the template's quotes (see <see cref="SpotsOf"/>).</summary>
    public enum PlaceholderSpot
    {
        /// <summary>Outside quotes, an argument of its own or part of one (<c>--name={x}</c>): the value goes in quoted.</summary>
        Bare,

        /// <summary>Written <c>"{x}"</c>: the pair gives way to the value's own quotes.</summary>
        QuotedAlone,

        /// <summary>Inside quotes with other text (<c>"--in={file}"</c>, <c>"WIP {clipboard}"</c>): refused.</summary>
        InsideQuotes,
    }

    /// <summary>A placeholder of the template: where it is, how it stands, and the backslashes written right before it.</summary>
    public readonly record struct Spot(int Start, int End, PlaceholderSpot Kind, int Backslashes);

    /// <summary>
    /// The placeholders of <paramref name="template"/> in order, read as the C runtime (CommandLineToArgvW) reads a command
    /// line: a <c>"</c> opens or closes a quoted span, unless an odd run of backslashes comes before it, and <c>""</c>
    /// inside a span is a quote character. A placeholder's own text (a prompt may hold quotes) is passed over.
    /// </summary>
    public static IReadOnlyList<Spot> SpotsOf(string template)
    {
        ArgumentNullException.ThrowIfNull(template);
        var spots = new List<Spot>();
        var placeholders = AnyPlaceholder().Matches(template).ToDictionary(m => m.Index, m => m.Index + m.Length);
        var inQuotes = false;
        var opened = -1; // where the quoted span we are in opened
        var i = 0;
        while (i < template.Length)
        {
            if (placeholders.TryGetValue(i, out var end))
            {
                var backslashes = 0;
                while (!inQuotes && i - backslashes - 1 >= 0 && template[i - backslashes - 1] == '\\')
                {
                    backslashes++;
                }

                var kind = !inQuotes ? PlaceholderSpot.Bare
                    : opened == i - 1 && end < template.Length && template[end] == '"' && (end + 1 >= template.Length || template[end + 1] != '"')
                        ? PlaceholderSpot.QuotedAlone
                        : PlaceholderSpot.InsideQuotes;
                spots.Add(new Spot(i, end, kind, kind == PlaceholderSpot.Bare ? backslashes : 0));
                if (kind == PlaceholderSpot.QuotedAlone)
                {
                    inQuotes = false; // the closing quote of the pair
                    i = end + 1;
                }
                else
                {
                    i = end;
                }

                continue;
            }

            if (template[i] == '\\')
            {
                var run = 0;
                while (i + run < template.Length && template[i + run] == '\\')
                {
                    run++;
                }

                i += run;
                if (i < template.Length && template[i] == '"' && run % 2 == 1)
                {
                    i++; // an escaped quote: a character, not a quote
                }

                continue;
            }

            if (template[i] == '"')
            {
                if (inQuotes && i + 1 < template.Length && template[i + 1] == '"')
                {
                    i += 2; // "" inside a span: a quote character
                    continue;
                }

                inQuotes = !inQuotes;
                opened = inQuotes ? i : -1;
            }

            i++;
        }

        return spots;
    }

    /// <summary>Whether a placeholder stands inside quotes with other text, where its value's own quotes would end the span.</summary>
    public static bool InsideQuotes(string? template) =>
        !string.IsNullOrEmpty(template) && SpotsOf(template).Any(spot => spot.Kind == PlaceholderSpot.InsideQuotes);

    /// <summary>
    /// A program that starts the program named in its own arguments: Windows Terminal, conhost, runas and Windows'
    /// sudo. What it starts may be a batch file, which cmd.exe reads: named so (<c>build.cmd</c>), or a bare name that
    /// Windows finds as one on PATH (<see cref="StartsABatchFile"/>).
    /// </summary>
    private static bool IsRelay(string name) =>
        name is "wt" or "wt.exe" or "windowsterminal" or "windowsterminal.exe" or "conhost" or "conhost.exe" or "runas" or "runas.exe" or "sudo" or "sudo.exe";

    /// <summary>Whether a word a relay may start is a bare name that Windows finds as a batch file (PATHEXT, PATH).</summary>
    private static bool StartsABatchFile(string word, string? startIn, Func<string, string?>? registeredPathOf, string? searchPath, string? pathExtensions)
    {
        if (word.Length == 0 || word[0] is '-' or '/' || Path.HasExtension(word) || AnyPlaceholder().IsMatch(word))
        {
            return false;
        }

        return Candidates(word, startIn, registeredPathOf, searchPath, pathExtensions)
            .Any(file => FileNameOf(file) is var name && (name.EndsWith(".bat", StringComparison.Ordinal) || name.EndsWith(".cmd", StringComparison.Ordinal)));
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

        var offset = at < words.Count ? placeholderIndex - words[at].Start : 0;
        var reader = ReaderOfLocation(location, words, at, offset, startIn, registeredPathOf, searchPath, pathExtensions, system);
        var relayed = IsRelay(FileNameOf(Environment.ExpandEnvironmentVariables(location ?? string.Empty).Trim().Trim('"')));
        for (var i = 0; i < words.Count && reader != ArgumentReader.PowerShellCommand; i++)
        {
            var named = FileNameOf(words[i].Text);
            if (IsRelay(named))
            {
                relayed = true;
                continue;
            }

            var rest = words.GetRange(i + 1, words.Count - i - 1);
            var restAt = at - i - 1; // the placeholder's place among the words after this one; negative when it comes before
            reader = Stricter(reader, named switch
            {
                "cmd" or "cmd.exe" => ArgumentReader.Cmd,
                _ when named.EndsWith(".bat", StringComparison.Ordinal) || named.EndsWith(".cmd", StringComparison.Ordinal) => ArgumentReader.Cmd,
                _ when relayed && StartsABatchFile(words[i].Bare, startIn, registeredPathOf, searchPath, pathExtensions) => ArgumentReader.Cmd,
                "powershell" or "powershell.exe" or "pwsh" or "pwsh.exe" => PowerShellReader(named, rest.Select(word => word.Text)),
                "wsl" or "wsl.exe" => restAt >= 0 ? WslReader(rest, restAt) : ArgumentReader.PosixShell,
                "bash" or "bash.exe" when IsWslBash(words[i].Bare.Trim('\''), system) => ArgumentReader.PosixShell,
                "ssh" or "ssh.exe" => restAt >= 0 ? SshReader(rest, restAt, offset) : ArgumentReader.RemoteShell,
                "plink" or "plink.exe" or "putty" or "putty.exe" => restAt >= 0 ? PlinkReader(rest, restAt) : ArgumentReader.RemoteShell,
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
    /// Whether Windows Terminal reads the command line: <c>wt.exe</c> (or <c>WindowsTerminal.exe</c>) as the location or
    /// named in the parameters. It splits its arguments at every <c>;</c>, inside quotes too, and runs what follows as a
    /// command of its own (<c>wt ssh "host;calc"</c> opens a second tab with calc), so no value or dropped path may hold
    /// one there.
    /// </summary>
    public static bool SplitsAtSemicolons(string? location, string? parameters) =>
        Names(location, parameters).Any(name => name is "wt" or "wt.exe" or "windowsterminal" or "windowsterminal.exe");

    /// <summary>
    /// The program that rebuilds the command line it hands on, and quotes an argument only when it holds a space:
    /// Windows Terminal ("Windows Terminal") or conhost ("conhost.exe"), as the location or named in the parameters;
    /// <see langword="null"/> otherwise. A value or a dropped path without a space then reaches the next program without
    /// its quotes, so what cmd.exe would act on there (<c>&amp; | &lt; &gt; ^ ( )</c>) is refused in it.
    /// </summary>
    public static string? DropsQuotes(string? location, string? parameters)
    {
        foreach (var name in Names(location, parameters))
        {
            if (name is "wt" or "wt.exe" or "windowsterminal" or "windowsterminal.exe")
            {
                return "Windows Terminal";
            }

            if (name is "conhost" or "conhost.exe")
            {
                return "conhost.exe";
            }
        }

        return null;
    }

    /// <summary>What cmd.exe acts on outside quotes, refused where a program hands a value on without them (<see cref="DropsQuotes"/>).</summary>
    public static string? RefusedWithoutQuotes(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var index = value.IndexOfAny(['&', '|', '<', '>', '^', '(', ')']);
        return index < 0 ? null : value[index].ToString();
    }

    private static IEnumerable<string> Names(string? location, string? parameters) =>
        new[] { FileNameOf(Environment.ExpandEnvironmentVariables(location ?? string.Empty)) }
            .Concat((parameters ?? string.Empty).Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries).Select(FileNameOf));

    private static ArgumentReader ReaderOfLocation(
        string? location, List<Word> words, int at, int offset, string? startIn, Func<string, string?>? registeredPathOf, string? searchPath, string? pathExtensions, string system)
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
            return ReaderOfFile(path, words, at, offset, system);
        }

        if (Directory.Exists(path))
        {
            return ArgumentReader.Plain; // a folder opens in Explorer
        }

        var found = Candidates(path, startIn, registeredPathOf, searchPath, pathExtensions).ToList();
        return found.Count == 0
            ? ArgumentReader.Cmd // unknown here: it may be a batch file where it runs
            : found.Select(file => ReaderOfFile(file, words, at, offset, system)).Aggregate(Stricter);
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
    private static ArgumentReader ReaderOfFile(string path, List<Word> words, int at, int offset, string system)
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
            "ssh.exe" => SshReader(words, at, offset),
            "plink.exe" or "putty.exe" => PlinkReader(words, at), // PuTTY reads the same command line as plink
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

    /// <summary>ssh_config keywords whose value runs a program or loads code or more settings: no placeholder goes there.</summary>
    private static readonly HashSet<string> SshKeywordsThatRun = new(StringComparer.OrdinalIgnoreCase)
    {
        "ProxyCommand", "LocalCommand", "PermitLocalCommand", "KnownHostsCommand", "PKCS11Provider", "SecurityKeyProvider",
        "Include", "XAuthLocation", "Match",
    };

    /// <summary>ssh_config keywords whose value is a host or a login name, as for <c>-J</c> and <c>-l</c>.</summary>
    private static readonly HashSet<string> SshKeywordsWithHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "HostName", "User", "ProxyJump",
    };

    /// <summary>Placeholders in a word, masked so their names are not read as option letters.</summary>
    private static string Masked(string text) => AnyPlaceholder().Replace(text, "\u0001");

    /// <summary>
    /// The last word of a value that starts at <paramref name="first"/>: the same word, or, for a value opened by a quote
    /// and not closed in it (<c>-o "ProxyCommand {clipboard}"</c>), the word that closes it. Windows hands such a
    /// span to the program as one argument.
    /// </summary>
    private static int ValueEnd(IReadOnlyList<Word> words, int first)
    {
        if (first >= words.Count || !words[first].Text.StartsWith('"') || (words[first].Text.Length > 1 && words[first].Text.EndsWith('"')))
        {
            return first;
        }

        for (var k = first + 1; k < words.Count; k++)
        {
            if (words[k].Text.EndsWith('"'))
            {
                return k;
            }
        }

        return words.Count - 1;
    }

    /// <summary>
    /// Who reads a placeholder in the value of ssh's option <paramref name="letter"/>, with <paramref name="before"/>
    /// the part of the value the template writes before it. <c>-o</c> takes a line of ssh_config, where a space or
    /// <c>=</c> separates the keyword from its value: a placeholder at the start of it, or inside the keyword, could
    /// name any keyword (<c>ProxyCommand calc</c>), and so could one after a keyword that runs or loads something.
    /// </summary>
    private static ArgumentReader SshValueReader(char letter, string before)
    {
        switch (letter)
        {
            case 'J' or 'l':
                return ArgumentReader.SshDestination;
            case 'o':
                var line = Masked(before).TrimStart('"').TrimStart();
                var end = line.IndexOfAny(['=', ' ', '\t']);
                if (end <= 0 || line[..end].Contains('\u0001', StringComparison.Ordinal))
                {
                    return ArgumentReader.SshOption; // the value starts the line, or is part of the keyword
                }

                var keyword = line[..end];
                return SshKeywordsThatRun.Contains(keyword) ? ArgumentReader.SshOption
                    : SshKeywordsWithHosts.Contains(keyword) ? ArgumentReader.SshDestination
                    : ArgumentReader.RemoteShell;
            default:
                return ArgumentReader.Plain; // -p 22, -i key, -F file: one argument, as the option's value
        }
    }

    /// <summary>
    /// ssh as OpenSSH reads its command line: options (getopt), the destination, then options again unless <c>--</c>
    /// came first, then the command. The words of the command are joined for the remote shell without their quotes, so
    /// a value there is read as bare words, as PowerShell reads script text (<c>ssh host cat {clipboard}</c>). The
    /// destination is one argument of ssh, but ssh can put it into a command of its own
    /// (<see cref="ArgumentReader.SshDestination"/>), and so for <c>-J</c> and <c>-l</c>. A placeholder in <c>-o</c>'s
    /// value is judged by its keyword (<see cref="SshValueReader"/>), and one among an option's letters would add options
    /// (<see cref="ArgumentReader.SshOption"/>); other values (<c>-p 22</c>, <c>-i key</c>, <c>-F file</c>) are plain.
    /// </summary>
    /// <param name="words">The words after <c>ssh</c>.</param>
    /// <param name="at">Which of them the placeholder is in (their count: after all of them).</param>
    /// <param name="offset">Where the placeholder starts in that word.</param>
    private static ArgumentReader SshReader(IReadOnlyList<Word> words, int at, int offset)
    {
        var options = true; // getopt is reading options
        var terminated = false; // "--" came before the destination: ssh reads no options after it either
        var destination = false;
        for (var j = 0; j < words.Count && j <= at; j++)
        {
            var word = Masked(words[j].Bare);
            if (options && word == "--")
            {
                options = false;
                terminated |= !destination;
                continue;
            }

            if (options && word.Length > 1 && word[0] == '-')
            {
                if (j == at)
                {
                    // An option word with the placeholder in it: in a value attached to an option letter, or among the letters.
                    var text = words[j].Text;
                    var lead = text.StartsWith('"') ? 1 : 0;
                    var prefix = Masked(text[lead..Math.Max(lead, offset)]);
                    var letter = prefix.IndexOfAny(SshOptionsWithValue.ToCharArray(), 1);
                    return letter < 0 ? ArgumentReader.SshOption : SshValueReader(prefix[letter], prefix[(letter + 1)..]);
                }

                var valueLetter = word.IndexOfAny(SshOptionsWithValue.ToCharArray(), 1);
                if (valueLetter == word.Length - 1)
                {
                    var last = ValueEnd(words, j + 1);
                    if (at > j && at <= last)
                    {
                        var before = string.Join(' ', words.Skip(j + 1).Take(at - j - 1).Select(w => w.Text));
                        var inWord = at < words.Count ? words[at].Text[..Math.Min(offset, words[at].Text.Length)] : string.Empty;
                        return SshValueReader(word[valueLetter], before.Length > 0 ? before + " " + inWord : inWord);
                    }

                    j = last; // -p 22, -vp 22: the value is the next word (-p22 holds its own)
                }

                continue;
            }

            if (!destination)
            {
                if (j == at)
                {
                    return ArgumentReader.SshDestination;
                }

                destination = true;
                options = !terminated; // OpenSSH reads options again after the destination
                continue;
            }

            return ArgumentReader.RemoteShell; // the command starts here, at or before the placeholder
        }

        return destination ? ArgumentReader.RemoteShell : ArgumentReader.SshDestination; // a file dropped at the end
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
            ArgumentReader.SshOption => (_, _) => false,
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
            ArgumentReader.SshOption => (_, _) => false,
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
    /// <c>. - _ @ : [ ] /</c>, but not a <c>-</c> to start with. Nothing a shell, or ssh's own <c>%h</c>/<c>%u</c> in a
    /// <c>ProxyCommand</c>, could act on (CVE-2023-51385, where OpenSSH before 9.6 passed such names on), and no name
    /// that the proxy program would take as an option (<c>ssh -- {clipboard}</c> holding <c>-x</c>; OpenSSH 9.6 refuses
    /// such a host name too).
    /// </summary>
    private static bool IsAllowedInSshDestination(string text, int index) =>
        char.IsAsciiLetterOrDigit(text[index]) || text[index] is '.' or '_' or '@' or ':' or '[' or ']' or '/' || (text[index] == '-' && index > 0);

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

    /// <summary>Any placeholder: <c>{clipboard}</c>, <c>{file}</c>, <c>{files}</c> or <c>{ask:…}</c>.</summary>
    [GeneratedRegex(@"\{(?:clipboard|files?|ask:[^}]*)\}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AnyPlaceholder();

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

    /// <summary>
    /// ssh reading the value as its own settings: a placeholder that starts <c>-o</c>'s value or stands in its keyword
    /// (ssh_config takes a space or <c>=</c> after the keyword, so <c>ProxyCommand calc</c> runs calc), one after a
    /// keyword that runs or loads something (<c>ProxyCommand</c>, <c>PKCS11Provider</c>, <c>Include</c>), or one among
    /// an option's letters. Every value is refused.
    /// </summary>
    SshOption,
}

