using System.Text.Json;
using System.Text.Json.Serialization;

namespace DesktopIconGrid.Core.Commands;

/// <summary>What the command line asks the bar to do.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<BarCommandKind>))]
public enum BarCommandKind
{
    /// <summary><c>--add &lt;path&gt;…</c>: a shortcut to each path, at the end of the bar or of <see cref="BarCommand.Group"/>.</summary>
    Add,

    /// <summary><c>--launch &lt;name&gt;</c>: starts the shortcut of that name, as a click does.</summary>
    Launch,

    Show,

    Hide,

    Toggle,
}

/// <summary>One thing for the bar to do: <see cref="Value"/> is the path or the name, <see cref="Group"/> the group to add into.</summary>
public sealed record BarCommand(BarCommandKind Kind, string? Value = null, string? Group = null);

/// <summary>
/// The command line of <c>DesktopIconGrid.exe</c>, and the message a second start hands to the running bar:
/// <c>--add &lt;path&gt;… [--group &lt;name&gt;]</c> (what Explorer's <em>Send to</em> passes), <c>--launch &lt;name&gt;</c>,
/// <c>--show</c>, <c>--hide</c>, <c>--toggle</c>; <c>--hidden</c> belongs to the start itself and is passed over. A
/// message is checked as strictly as a command line (<see cref="Validate"/>): the pipe only lets this Windows user in,
/// but whatever it hands over reaches the layout.
/// </summary>
public static class BarCommands
{
    public const int MaxCommands = 256;

    public const int MaxMessageBytes = 256 * 1024;

    public const int MaxTextLength = 2048;

    public const int MaxGroupLength = 100;

    /// <summary>The argument of a start that stays in the tray (the Run entry's).</summary>
    public const string HiddenArgument = "--hidden";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    /// <summary>
    /// The commands in <paramref name="args"/>, or why they cannot be read (and no commands). <c>--add</c> takes every
    /// argument up to the next one that starts with <c>--</c>; a <c>--group</c> right after its paths names their group.
    /// </summary>
    public static (IReadOnlyList<BarCommand> Commands, string? Error) Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var commands = new List<BarCommand>();
        var i = 0;
        while (i < args.Count)
        {
            var arg = args[i];
            switch (arg.ToLowerInvariant())
            {
                case HiddenArgument:
                    i++;
                    break;
                case "--show":
                    commands.Add(new BarCommand(BarCommandKind.Show));
                    i++;
                    break;
                case "--hide":
                    commands.Add(new BarCommand(BarCommandKind.Hide));
                    i++;
                    break;
                case "--toggle":
                    commands.Add(new BarCommand(BarCommandKind.Toggle));
                    i++;
                    break;
                case "--launch":
                    if (i + 1 >= args.Count || IsOption(args[i + 1]))
                    {
                        return ([], "--launch needs the name of a shortcut.");
                    }

                    commands.Add(new BarCommand(BarCommandKind.Launch, args[i + 1]));
                    i += 2;
                    break;
                case "--add":
                    var paths = new List<string>();
                    i++;
                    while (i < args.Count && !IsOption(args[i]))
                    {
                        paths.Add(args[i++]);
                    }

                    string? group = null;
                    if (i < args.Count && args[i].Equals("--group", StringComparison.OrdinalIgnoreCase))
                    {
                        if (i + 1 >= args.Count || IsOption(args[i + 1]))
                        {
                            return ([], "--group needs a name.");
                        }

                        group = args[i + 1];
                        i += 2;
                    }

                    if (paths.Count == 0)
                    {
                        return ([], "--add needs at least one path.");
                    }

                    commands.AddRange(paths.Select(path => new BarCommand(BarCommandKind.Add, path, group)));
                    break;
                default:
                    return ([], $"\"{arg}\" is not understood. Use --add <path>… [--group <name>], --launch <name>, --show, --hide or --toggle.");
            }
        }

        return Validate(commands) is { } error ? ([], error) : (commands, null);
    }

    /// <summary>Why <paramref name="commands"/> may not be carried out, or <see langword="null"/>.</summary>
    public static string? Validate(IReadOnlyList<BarCommand> commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        if (commands.Count > MaxCommands)
        {
            return $"More than {MaxCommands} commands at once.";
        }

        foreach (var command in commands)
        {
            if (command is null || !Enum.IsDefined(command.Kind))
            {
                return "A command is not understood.";
            }

            switch (command.Kind)
            {
                case BarCommandKind.Add:
                    if (!IsText(command.Value, MaxTextLength) || !IsPath(command.Value!))
                    {
                        return $"\"{Shown(command.Value)}\" is no full path of a file or folder.";
                    }

                    if (command.Group is not null && !IsText(command.Group, MaxGroupLength))
                    {
                        return "The group's name is empty, too long, or holds a line break.";
                    }

                    break;
                case BarCommandKind.Launch:
                    if (!IsText(command.Value, MaxTextLength) || command.Group is not null)
                    {
                        return "--launch needs the name of a shortcut.";
                    }

                    break;
                default:
                    if (command.Value is not null || command.Group is not null)
                    {
                        return "--show, --hide and --toggle take nothing.";
                    }

                    break;
            }
        }

        return null;
    }

    /// <summary>The message a second start sends to the running bar.</summary>
    public static byte[] Serialize(IReadOnlyList<BarCommand> commands) =>
        JsonSerializer.SerializeToUtf8Bytes(new Message(1, commands.ToList()), Json);

    /// <summary>The commands of a message, or <see langword="null"/> when it is not one or does not pass <see cref="Validate"/>.</summary>
    public static IReadOnlyList<BarCommand>? Deserialize(ReadOnlySpan<byte> message)
    {
        if (message.Length is 0 or > MaxMessageBytes)
        {
            return null;
        }

        try
        {
            var read = JsonSerializer.Deserialize<Message>(message, Json);
            return read is { Version: 1, Commands: { } commands } && Validate(commands) is null ? commands : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsOption(string arg) => arg.StartsWith("--", StringComparison.Ordinal);

    private static bool IsText(string? text, int maxLength) =>
        !string.IsNullOrWhiteSpace(text) && text.Length <= maxLength && !text.Any(char.IsControl);

    /// <summary>A full path on a drive (<c>C:\…</c>) or a share (<c>\\server\share\…</c>); nothing relative, no device path.</summary>
    private static bool IsPath(string path)
    {
        if (path.IndexOfAny(['"', '<', '>', '|', '*', '?']) >= 0)
        {
            return false;
        }

        var onDrive = path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] is '\\' or '/';
        var onShare = path.StartsWith(@"\\", StringComparison.Ordinal) && path.Length > 2 && path[2] is not ('?' or '.' or '\\')
                      && path[2..].Split('\\', StringSplitOptions.RemoveEmptyEntries).Length >= 2;
        return onDrive || onShare;
    }

    private static string Shown(string? text) => text is null ? string.Empty : text.Length > 80 ? text[..80] + "…" : text;

    private sealed record Message(int Version, List<BarCommand> Commands);
}
