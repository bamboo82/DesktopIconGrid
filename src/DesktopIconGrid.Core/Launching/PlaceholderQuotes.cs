namespace DesktopIconGrid.Core.Launching;

/// <summary>Why a placeholder cannot stand where the template puts it (see <see cref="LaunchPlaceholders.PlacementAt"/>).</summary>
public enum PlacementProblem
{
    None,

    /// <summary>
    /// Inside quotes with other text, where the value's own quotes would end the span; or where the programs that read
    /// the line disagree about the quotes around it.
    /// </summary>
    InsideQuotes,

    /// <summary>
    /// After Windows Terminal, conhost, sudo or gsudo, which hand the value on without its quotes, where cmd.exe or a
    /// Linux shell reads it.
    /// </summary>
    HandedOnWithoutQuotes,

    /// <summary>A value before cmd.exe's <c>/c</c>, <c>/k</c> or <c>/r</c>, where cmd reads it as its own switches.</summary>
    CmdSwitches,
}

/// <summary>
/// How the programs that read a command line see the quotes around each placeholder. <see cref="SpotsOf"/> follows the C
/// runtime (the UCRT), which <see cref="Fill"/> fills for. A placeholder is only filled where the other readings that
/// apply agree with it (<see cref="SpotAt"/>):
/// <list type="bullet">
/// <item>CommandLineToArgvW, which Chromium, Electron, Windows Terminal and older C runtimes use: there <c>""</c> inside a
/// span is a quote character that ends the span.</item>
/// <item>cmd.exe, for a value that cmd reads: every <c>"</c> opens or closes a span (a backslash does not escape it), and
/// <c>^</c> outside a span escapes the next character.</item>
/// <item>A Linux shell, for a value that WSL's shell reads: <c>'…'</c> and <c>$'…'</c> are quoted spans too.</item>
/// </list>
/// </summary>
public static partial class LaunchPlaceholders
{
    /// <summary>
    /// How <paramref name="template"/>'s placeholder at <paramref name="index"/> (or right after a quote there) stands
    /// for <paramref name="reader"/>: as <see cref="SpotsOf"/> says, when every reading that applies to the reader agrees;
    /// <see cref="PlaceholderSpot.InsideQuotes"/> when one of them disagrees.
    /// </summary>
    public static PlaceholderSpot SpotAt(string template, int index, ArgumentReader reader)
    {
        ArgumentNullException.ThrowIfNull(template);
        var spot = SpotsOf(template).FirstOrDefault(s => s.Start == index || (s.Start == index + 1 && template[index] == '"'));
        if (spot.End == 0)
        {
            return PlaceholderSpot.Bare; // no placeholder there
        }

        if (spot.Kind == PlaceholderSpot.InsideQuotes)
        {
            return PlaceholderSpot.InsideQuotes;
        }

        var placeholders = PlaceholderRanges(template);
        var readings = new List<Dictionary<int, PlaceholderSpot>> { ArgvWSpots(template, placeholders) };
        if (reader >= ArgumentReader.Cmd)
        {
            readings.Add(CmdSpots(template, placeholders)); // cmd may read the line on the way (cmd /c wsl …)
        }

        if (reader >= ArgumentReader.PosixShell)
        {
            readings.Add(ShellSpots(template, placeholders));
        }

        return readings.All(reading => reading.TryGetValue(spot.Start, out var kind) && kind == spot.Kind) ? spot.Kind : PlaceholderSpot.InsideQuotes;
    }

    /// <summary>
    /// Whether the placeholder at <paramref name="index"/> in <paramref name="template"/> can be filled where it stands
    /// for <paramref name="reader"/>, and why not. A placeholder is refused, not modelled further:
    /// <list type="bullet">
    /// <item>inside quotes with other text, or where the readings disagree (<see cref="SpotAt"/>), unless its reader takes
    /// the value as bare words anyway (<see cref="ArgumentReader.RemoteShell"/>, <see cref="ArgumentReader.PowerShellCommand"/>);</item>
    /// <item>where cmd.exe or a Linux shell reads it after a program that hands it on without its quotes
    /// (<see cref="DropsQuotes"/>);</item>
    /// <item>for a value, before cmd.exe's <c>/c</c>, <c>/k</c> or <c>/r</c> (<see cref="CmdCommandLine.ReadsAsSwitches"/>).</item>
    /// </list>
    /// </summary>
    public static PlacementProblem PlacementAt(string? location, string template, int index, ArgumentReader reader)
    {
        ArgumentNullException.ThrowIfNull(template);
        if (SpotAt(template, index, reader) == PlaceholderSpot.InsideQuotes && !TakesBareWords(reader))
        {
            return PlacementProblem.InsideQuotes;
        }

        if (reader is ArgumentReader.Cmd or ArgumentReader.PosixShell && DropsQuotes(location, template) is not null)
        {
            return PlacementProblem.HandedOnWithoutQuotes;
        }

        var start = template[index] == '"' ? index + 1 : index;
        var isValue = AnyPlaceholder().Match(template, start) is { Success: true } match && match.Index == start
            && !match.Value.StartsWith("{file", StringComparison.OrdinalIgnoreCase);
        return isValue && CmdCommandLine.ReadsAsSwitches(location, template, start) ? PlacementProblem.CmdSwitches : PlacementProblem.None;
    }

    /// <summary>
    /// The first placeholder of <paramref name="template"/> that cannot stand where it does (<see cref="PlacementAt"/>), with
    /// its reader from <paramref name="readerAt"/>; <see langword="null"/> when every one can.
    /// </summary>
    public static (PlacementProblem Problem, ArgumentReader Reader)? FirstMisplaced(string? location, string template, Func<int, ArgumentReader> readerAt)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(readerAt);
        foreach (var spot in SpotsOf(template))
        {
            var reader = readerAt(spot.Start);
            if (PlacementAt(location, template, spot.Start, reader) is var problem and not PlacementProblem.None)
            {
                return (problem, reader);
            }
        }

        return null;
    }

    /// <summary>Readers that check a value as bare words (no quotes of any kind), so the quotes around it change nothing.</summary>
    public static bool TakesBareWords(ArgumentReader reader) => reader is ArgumentReader.RemoteShell or ArgumentReader.PowerShellCommand;

    /// <summary>Where each placeholder starts, and where it ends.</summary>
    private static Dictionary<int, int> PlaceholderRanges(string template) =>
        AnyPlaceholder().Matches(template).ToDictionary(m => m.Index, m => m.Index + m.Length);

    /// <summary>Whether one quote follows the placeholder that ends at <paramref name="end"/>, and no second one.</summary>
    private static bool ClosesAlone(string template, int end) =>
        end < template.Length && template[end] == '"' && (end + 1 >= template.Length || template[end + 1] != '"');

    /// <summary>
    /// The placeholders as CommandLineToArgvW reads the line (Wine's implementation of it): a run of quotes counts up, a
    /// third quote in a row is a quote character and ends the span, and two leave it.
    /// </summary>
    private static Dictionary<int, PlaceholderSpot> ArgvWSpots(string template, Dictionary<int, int> placeholders)
    {
        var spots = new Dictionary<int, PlaceholderSpot>();
        var quotes = 0; // 1 inside a span
        var opener = -1; // the quote that opened the span, when it opened it alone
        var backslashes = 0;
        var i = 0;
        while (i < template.Length)
        {
            if (placeholders.TryGetValue(i, out var end))
            {
                spots[i] = quotes == 0 ? PlaceholderSpot.Bare
                    : opener == i - 1 && ClosesAlone(template, end) ? PlaceholderSpot.QuotedAlone
                    : PlaceholderSpot.InsideQuotes;
                backslashes = 0;
                i = end;
                continue;
            }

            if (template[i] == '\\')
            {
                backslashes++;
                i++;
                continue;
            }

            if (template[i] != '"')
            {
                backslashes = 0;
                i++;
                continue;
            }

            var opened = false;
            if (backslashes % 2 == 0)
            {
                quotes++;
                opened = quotes == 1;
            }

            backslashes = 0;
            i++;
            while (i < template.Length && template[i] == '"')
            {
                quotes++;
                opened = quotes == 1;
                if (quotes == 3)
                {
                    quotes = 0; // a quote character, and the span ends
                }

                i++;
            }

            if (quotes == 2)
            {
                quotes = 0;
            }

            opener = quotes == 1 && opened ? i - 1 : -1;
        }

        return spots;
    }

    /// <summary>
    /// The placeholders as cmd.exe reads the line: every quote opens or closes a span, and a <c>^</c> outside one escapes
    /// the next character. A value right after such a <c>^</c> would lose its opening quote to it, so that placeholder
    /// counts as inside quotes.
    /// </summary>
    private static Dictionary<int, PlaceholderSpot> CmdSpots(string template, Dictionary<int, int> placeholders)
    {
        var spots = new Dictionary<int, PlaceholderSpot>();
        var inQuotes = false;
        var opener = -1;
        var i = 0;
        while (i < template.Length)
        {
            if (placeholders.TryGetValue(i, out var end))
            {
                spots[i] = !inQuotes ? PlaceholderSpot.Bare
                    : opener == i - 1 && ClosesAlone(template, end) ? PlaceholderSpot.QuotedAlone
                    : PlaceholderSpot.InsideQuotes;
                i = end;
                continue;
            }

            if (template[i] == '^' && !inQuotes)
            {
                if (placeholders.TryGetValue(i + 1, out var after))
                {
                    spots[i + 1] = PlaceholderSpot.InsideQuotes;
                    i = after;
                }
                else
                {
                    i += 2;
                }

                continue;
            }

            if (template[i] == '"')
            {
                inQuotes = !inQuotes;
                opener = inQuotes ? i : -1;
            }

            i++;
        }

        return spots;
    }

    /// <summary>
    /// The placeholders as a Linux shell reads the line: <c>'…'</c> and <c>$'…'</c> are quoted spans too, and nothing of a
    /// value is safe in them (a <c>'</c> in it ends the span), so a placeholder there counts as inside quotes. A backslash
    /// escapes the next character outside quotes, and <c>$ ` " \</c> inside double quotes.
    /// </summary>
    private static Dictionary<int, PlaceholderSpot> ShellSpots(string template, Dictionary<int, int> placeholders)
    {
        var spots = new Dictionary<int, PlaceholderSpot>();
        var state = ' '; // ' ' outside quotes, '\'' in '…', '$' in $'…', '"' in "…"
        var opener = -1;
        var i = 0;
        while (i < template.Length)
        {
            if (placeholders.TryGetValue(i, out var end))
            {
                spots[i] = state switch
                {
                    ' ' => PlaceholderSpot.Bare,
                    '"' when opener == i - 1 && ClosesAlone(template, end) => PlaceholderSpot.QuotedAlone,
                    _ => PlaceholderSpot.InsideQuotes,
                };
                i = end;
                continue;
            }

            var c = template[i];
            var escapes = c == '\\' && i + 1 < template.Length && (state is ' ' or '$' || (state == '"' && template[i + 1] is '$' or '`' or '"' or '\\'));
            if (escapes)
            {
                i += placeholders.ContainsKey(i + 1) ? 1 : 2; // a placeholder's value is filled in after its backslashes are doubled
                continue;
            }

            switch (state)
            {
                case ' ' when c == '\'':
                    state = '\'';
                    break;
                case ' ' when c == '$' && i + 1 < template.Length && template[i + 1] == '\'':
                    state = '$';
                    i++;
                    break;
                case ' ' when c == '"':
                    state = '"';
                    opener = i;
                    break;
                case '\'' or '$' when c == '\'':
                case '"' when c == '"':
                    state = ' ';
                    opener = -1;
                    break;
            }

            i++;
        }

        return spots;
    }
}
