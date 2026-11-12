using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

using DesktopIconGrid.Core.Resources;

namespace DesktopIconGrid.Core.Commands;

/// <summary>What the command line asks the bar to do.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<BarCommandKind>))]
public enum BarCommandKind
{
    /// <summary><c>--add &lt;path&gt;…</c>: a shortcut to each path, at the end of the bar or of <see cref="BarCommand.Group"/>.</summary>
    Add,

    /// <summary><c>--launch &lt;name&gt;</c>: starts the shortcut of that name, as a click does.</summary>
    Launch,

    /// <summary><c>--workspace &lt;name&gt;</c>: starts that workspace (0.10.0), as its menu entry does.</summary>
    Workspace,

    Show,

    Hide,

    Toggle,

    /// <summary><c>--voice</c>: listens for one spoken name (0.11.0), as the voice hotkey does.</summary>
    Voice,
}

/// <summary>One thing for the bar to do: <see cref="Value"/> is the path or the name, <see cref="Group"/> the group to add into.</summary>
public sealed record BarCommand(BarCommandKind Kind, string? Value = null, string? Group = null);

/// <summary>
/// The command line of <c>DesktopIconGrid.exe</c>, and the message a second start hands to the running bar:
/// <c>--add &lt;path&gt;… [--group &lt;name&gt;]</c> (what Explorer's <em>Send to</em> passes), <c>--launch &lt;name&gt;</c>,
/// <c>--show</c>, <c>--hide</c>, <c>--toggle</c>, <c>--voice</c>; <c>--hidden</c> belongs to the start itself and is passed over. A
/// message is checked as strictly as a command line (<see cref="Validate"/>): the pipe only lets this Windows user in,
/// but whatever it hands over reaches the layout.
/// </summary>
public static class BarCommands
{
    /// <summary>
    /// More than Windows' command line (32,767 characters) can hold as full paths in practice, so a large selection
    /// sent from Explorer is not refused where a drop of the same files is taken (review 21: it was 256).
    /// </summary>
    public const int MaxCommands = 4096;

    public const int MaxMessageBytes = 2 * 1024 * 1024;

    public const int MaxTextLength = 2048;

    public const int MaxGroupLength = 100;

    /// <summary>The argument of a start that stays in the tray (the Run entry's).</summary>
    public const string HiddenArgument = "--hidden";

    /// <summary>The option the <em>Send to</em> entry passes before the chosen paths.</summary>
    public const string AddArgument = "--add";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    /// <summary>
    /// The commands in <paramref name="args"/>, or why they cannot be read (and no commands). <c>--add</c> takes every
    /// argument up to the next one that starts with <c>--</c>; a <c>--group</c> right after <c>--add</c> or right after its
    /// paths names their group (before them, so a <em>Send to</em> shortcut can name a group: Explorer appends the files).
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
                case "--voice":
                    commands.Add(new BarCommand(BarCommandKind.Voice));
                    i++;
                    break;
                case "--launch":
                    if (i + 1 >= args.Count || IsOption(args[i + 1]))
                    {
                        return ([], CoreStrings.Commands_LaunchNeedsName);
                    }

                    commands.Add(new BarCommand(BarCommandKind.Launch, args[i + 1]));
                    i += 2;
                    break;
                case "--workspace":
                    if (i + 1 >= args.Count || IsOption(args[i + 1]))
                    {
                        return ([], CoreStrings.Commands_WorkspaceNeedsName);
                    }

                    commands.Add(new BarCommand(BarCommandKind.Workspace, args[i + 1]));
                    i += 2;
                    break;
                case AddArgument:
                    var paths = new List<string>();
                    i++;
                    string? group = null;
                    if (i < args.Count && args[i].Equals("--group", StringComparison.OrdinalIgnoreCase))
                    {
                        if (i + 1 >= args.Count || IsOption(args[i + 1]))
                        {
                            return ([], CoreStrings.Commands_GroupNeedsName);
                        }

                        group = args[i + 1];
                        i += 2;
                    }

                    while (i < args.Count && !IsOption(args[i]))
                    {
                        paths.Add(args[i++]);
                    }

                    if (i < args.Count && args[i].Equals("--group", StringComparison.OrdinalIgnoreCase))
                    {
                        if (group is not null)
                        {
                            return ([], CoreStrings.Commands_GroupTwice);
                        }

                        if (i + 1 >= args.Count || IsOption(args[i + 1]))
                        {
                            return ([], CoreStrings.Commands_GroupNeedsName);
                        }

                        group = args[i + 1];
                        i += 2;
                    }

                    if (paths.Count == 0)
                    {
                        return ([], CoreStrings.Commands_AddNeedsPath);
                    }

                    commands.AddRange(paths.Select(path => new BarCommand(BarCommandKind.Add, path, group)));
                    break;
                default:
                    return ([], string.Format(CultureInfo.CurrentCulture, CoreStrings.Commands_NotUnderstood, Shown(arg)));
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
            return string.Format(CultureInfo.CurrentCulture, CoreStrings.Commands_TooMany, MaxCommands);
        }

        foreach (var command in commands)
        {
            if (command is null || !Enum.IsDefined(command.Kind))
            {
                return CoreStrings.Commands_UnknownCommand;
            }

            switch (command.Kind)
            {
                case BarCommandKind.Add:
                    if (!IsText(command.Value, MaxTextLength) || !IsPath(command.Value!))
                    {
                        return string.Format(CultureInfo.CurrentCulture, CoreStrings.Commands_NoFullPath, Shown(command.Value));
                    }

                    if (command.Group is not null && !IsText(command.Group, MaxGroupLength))
                    {
                        return CoreStrings.Commands_BadGroup;
                    }

                    break;
                case BarCommandKind.Launch:
                    if (!IsText(command.Value, MaxTextLength) || command.Group is not null)
                    {
                        return CoreStrings.Commands_LaunchNeedsName;
                    }

                    break;
                case BarCommandKind.Workspace:
                    if (!IsText(command.Value, Launching.Workspaces.MaxNameLength) || command.Group is not null)
                    {
                        return CoreStrings.Commands_WorkspaceNeedsName;
                    }

                    break;
                default:
                    if (command.Value is not null || command.Group is not null)
                    {
                        return CoreStrings.Commands_TakesNothing;
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

    /// <summary>
    /// A full path on a drive (<c>C:\…</c>) or a share (<c>\\server\share\…</c>); nothing relative, no device path, and
    /// no server's pipes or mail slots (<c>\\localhost\pipe\x</c> is no share).
    /// </summary>
    private static bool IsPath(string path)
    {
        if (path.IndexOfAny(['"', '<', '>', '|', '*', '?']) >= 0)
        {
            return false;
        }

        var onDrive = path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] is '\\' or '/';
        var onShare = path.StartsWith(@"\\", StringComparison.Ordinal) && path.Length > 2 && path[2] is not ('?' or '.' or '\\')
                      && path[2..].Split('\\', StringSplitOptions.RemoveEmptyEntries) is { Length: >= 2 } parts
                      && !parts[1].Trim().Equals("pipe", StringComparison.OrdinalIgnoreCase)
                      && !parts[1].Trim().Equals("mailslot", StringComparison.OrdinalIgnoreCase);
        return onDrive || onShare;
    }

    private static string Shown(string? text) => text is null ? string.Empty : text.Length > 80 ? text[..80] + "…" : text;

    private sealed record Message(int Version, List<BarCommand> Commands);
}
