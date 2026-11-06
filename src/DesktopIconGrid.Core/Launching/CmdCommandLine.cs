namespace DesktopIconGrid.Core.Launching;

/// <summary>
/// cmd.exe's <c>/c</c>, <c>/k</c> and <c>/r</c> take the rest of the line as a command. When it starts with a quote, cmd
/// takes off its first quote and the line's last one, unless exactly two quotes surround an executable's name
/// (<c>cmd /?</c>, the "old behavior"). A value's quotes then lose their pair: <c>cmd /c "C:\a&amp;calc&amp;.txt"</c>
/// runs calc. Windows wraps a batch file's command in one more pair for this (<c>cmd /c ""C:\x.bat" …"</c>), and a
/// template for cmd.exe with placeholders gets the same here: <see cref="Unwrapped"/> before the placeholders are read,
/// <see cref="Wrapped"/> once they are filled.
/// </summary>
public static class CmdCommandLine
{
    /// <summary>
    /// Where the command after cmd.exe's <c>/c</c>, <c>/k</c> or <c>/r</c> starts in <paramref name="arguments"/> (after
    /// the spaces that follow the switch), or -1 when <paramref name="location"/> is not cmd.exe or there is no such switch.
    /// cmd's own switches (<c>/d /q /s /v:on</c> …) may come before it.
    /// </summary>
    public static int CommandStart(string? location, string? arguments)
    {
        var path = Environment.ExpandEnvironmentVariables(location ?? string.Empty).Trim().Trim('"');
        var name = path[(path.LastIndexOfAny(['\\', '/']) + 1)..].ToLowerInvariant();
        if (name is not ("cmd" or "cmd.exe") || string.IsNullOrEmpty(arguments))
        {
            return -1;
        }

        var i = 0;
        while (i < arguments.Length)
        {
            while (i < arguments.Length && char.IsWhiteSpace(arguments[i]))
            {
                i++;
            }

            if (i + 1 >= arguments.Length || arguments[i] != '/')
            {
                return -1; // a word that is no switch of cmd's: no command switch before it
            }

            if (char.ToLowerInvariant(arguments[i + 1]) is 'c' or 'k' or 'r')
            {
                i += 2;
                while (i < arguments.Length && char.IsWhiteSpace(arguments[i]))
                {
                    i++;
                }

                return i;
            }

            while (i < arguments.Length && !char.IsWhiteSpace(arguments[i]))
            {
                i++; // another switch of cmd's
            }
        }

        return -1;
    }

    /// <summary>
    /// <paramref name="template"/> without a pair of quotes written around the whole command
    /// (<c>/c ""C:\Program Files\x.exe" "{file}""</c>), which cmd would take off: the placeholders inside it are then read
    /// as they stand there, and <see cref="Wrapped"/> puts the pair back.
    /// </summary>
    public static string Unwrapped(string? location, string template)
    {
        ArgumentNullException.ThrowIfNull(template);
        var start = CommandStart(location, template);
        var end = template.TrimEnd().Length - 1;
        return start >= 0 && end > start + 1 && template[start] == '"' && template[start + 1] == '"' && template[end] == '"'
            ? template[..start] + template[(start + 1)..end] + template[(end + 1)..]
            : template;
    }

    /// <summary>The filled command in one more pair of quotes when it starts with one, so that cmd takes off that pair and nothing of the values'.</summary>
    public static string Wrapped(string? location, string arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var start = CommandStart(location, arguments);
        return start >= 0 && start < arguments.Length && arguments[start] == '"'
            ? arguments[..start] + "\"" + arguments[start..].TrimEnd() + "\""
            : arguments;
    }
}
