using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using DesktopIconGrid.Core.Models;

namespace DesktopIconGrid.Core.SpecialIcons;

/// <summary>What a command tile shows: the first line of output, or a number from it drawn as a gauge.</summary>
public enum CommandTileDisplay
{
    Text,
    Ring,
    Bar,
    Number,
}

/// <summary>What a click on a command tile does.</summary>
public enum CommandTileClick
{
    /// <summary>Runs the command again now.</summary>
    RunNow,

    /// <summary>Opens <see cref="CommandTileSettings.OpenTarget"/> (a folder, a file, a program or a web address).</summary>
    Open,
}

/// <summary>How a run of a command tile ended, from its exit code: 0 fine, 1 a warning, anything else (or no end) a failure.</summary>
public enum CommandLevel
{
    Unknown,
    Ok,
    Warning,
    Failed,
}

/// <summary>
/// A command tile (<see cref="SpecialIconKinds.CommandTile"/>): a program run on a timer or on a click without a window,
/// whose output the tile shows. Kept in <see cref="SpecialIconItem.Settings"/>. Whether it may run on this PC is not
/// stored here but in <see cref="CommandTrust"/>, so a copy from sync never carries its own approval.
/// </summary>
public sealed record CommandTileSettings(
    string Program,
    string? Arguments,
    string? StartIn,
    int IntervalSeconds,
    int TimeoutSeconds,
    CommandTileDisplay Display,
    double WarningAt,
    double CriticalAt,
    CommandTileClick Click,
    string? OpenTarget)
{
    public const string ProgramKey = "program";
    public const string ArgumentsKey = "arguments";
    public const string StartInKey = "startIn";
    public const string IntervalKey = "interval";
    public const string TimeoutKey = "timeout";
    public const string DisplayKey = "display";
    public const string WarningKey = "warningAt";
    public const string CriticalKey = "criticalAt";
    public const string ClickKey = "click";
    public const string OpenKey = "open";

    /// <summary>The shortest timer: a command more often than this would mostly keep a process starting.</summary>
    public const int MinIntervalSeconds = 10;

    public const int MaxIntervalSeconds = 24 * 60 * 60;

    public const int DefaultTimeoutSeconds = 30;

    public const int MaxTimeoutSeconds = 10 * 60;

    public const int MaxTextLength = 4096;

    public static CommandTileSettings Default { get; } =
        new(string.Empty, null, null, 60, DefaultTimeoutSeconds, CommandTileDisplay.Text, 70, 90, CommandTileClick.RunNow, null);

    /// <summary>Runs only on a click (no timer).</summary>
    public bool RunsOnClickOnly => IntervalSeconds == 0;

    public bool ShowsGauge => Display != CommandTileDisplay.Text;

    public static CommandTileSettings From(SpecialIconItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return new CommandTileSettings(
            item.GetSetting(ProgramKey)?.Trim() ?? string.Empty,
            item.GetSetting(ArgumentsKey),
            item.GetSetting(StartInKey),
            NormalizeInterval(ReadInt(item.GetSetting(IntervalKey)) ?? Default.IntervalSeconds),
            NormalizeTimeout(ReadInt(item.GetSetting(TimeoutKey)) ?? DefaultTimeoutSeconds),
            Enum.TryParse<CommandTileDisplay>(item.GetSetting(DisplayKey), ignoreCase: true, out var display) && Enum.IsDefined(display) ? display : CommandTileDisplay.Text,
            ReadDouble(item.GetSetting(WarningKey)) ?? Default.WarningAt,
            ReadDouble(item.GetSetting(CriticalKey)) ?? Default.CriticalAt,
            Enum.TryParse<CommandTileClick>(item.GetSetting(ClickKey), ignoreCase: true, out var click) && Enum.IsDefined(click) ? click : CommandTileClick.RunNow,
            item.GetSetting(OpenKey));
    }

    public void ApplyTo(SpecialIconItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        item.SetSetting(ProgramKey, Program);
        item.SetSetting(ArgumentsKey, Arguments);
        item.SetSetting(StartInKey, StartIn);
        item.SetSetting(IntervalKey, NormalizeInterval(IntervalSeconds).ToString(CultureInfo.InvariantCulture));
        item.SetSetting(TimeoutKey, NormalizeTimeout(TimeoutSeconds).ToString(CultureInfo.InvariantCulture));
        item.SetSetting(DisplayKey, Display.ToString().ToLowerInvariant());
        item.SetSetting(WarningKey, WarningAt.ToString(CultureInfo.InvariantCulture));
        item.SetSetting(CriticalKey, CriticalAt.ToString(CultureInfo.InvariantCulture));
        item.SetSetting(ClickKey, Click == CommandTileClick.Open ? "open" : "runNow");
        item.SetSetting(OpenKey, OpenTarget);
    }

    /// <summary>0 (on a click only), or a timer between <see cref="MinIntervalSeconds"/> and <see cref="MaxIntervalSeconds"/>.</summary>
    public static int NormalizeInterval(int seconds) => seconds <= 0 ? 0 : Math.Clamp(seconds, MinIntervalSeconds, MaxIntervalSeconds);

    public static int NormalizeTimeout(int seconds) => Math.Clamp(seconds, 1, MaxTimeoutSeconds);

    /// <summary>Why the tile cannot run as it is, or <see langword="null"/>. The texts are for the log; the dialog checks the same fields.</summary>
    public string? Problem()
    {
        if (Program.Length == 0)
        {
            return "No program is set.";
        }

        if (new[] { Program, Arguments, StartIn, OpenTarget }.Any(text => text is not null && (text.Length > MaxTextLength || !IsShowable(text))))
        {
            return "A field is too long, or holds a line break or an invisible character.";
        }

        if (Program.Trim().Trim('"').Contains('"'))
        {
            return "The program holds a quote: arguments go in their own field.";
        }

        if (Click == CommandTileClick.Open && string.IsNullOrWhiteSpace(OpenTarget))
        {
            return "A click should open something, but nothing is set.";
        }

        return null;
    }

    /// <summary>
    /// What is approved on a PC: the program, its arguments, its start folder, its timer (a click-only command must not
    /// become an unattended one through a synced edit) and, when a click opens something, what it opens, exactly as
    /// stored. An edit of any of these, on any PC, makes a new hash that needs approving again. The display and the
    /// thresholds are not part of it: they do not change what runs or when.
    /// </summary>
    public string Hash()
    {
        var text = string.Join(
            '\n',
            "command-tile/2",
            Program.Trim(),
            Arguments?.Trim() ?? string.Empty,
            StartIn?.Trim() ?? string.Empty,
            NormalizeInterval(IntervalSeconds).ToString(CultureInfo.InvariantCulture),
            Click == CommandTileClick.Open ? "open:" + (OpenTarget?.Trim() ?? string.Empty) : "run");
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    /// <summary>
    /// No character that changes how the rest reads without being seen: controls (line breaks), format characters
    /// (bidi overrides such as U+202E, zero-width characters) and the Unicode line and paragraph separators. What the
    /// approval window shows must read as what runs.
    /// </summary>
    public static bool IsShowable(string text) =>
        !text.Any(c => char.GetUnicodeCategory(c) is UnicodeCategory.Control or UnicodeCategory.Format
            or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator);

    private static int? ReadInt(string? text) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static double? ReadDouble(string? text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) ? value : null;
}

/// <summary>What one run of a command tile shows.</summary>
/// <param name="Label">The tile text: the first line of output, or the number for a gauge.</param>
/// <param name="Percent">The gauge value (0–100), or <see langword="null"/>.</param>
/// <param name="Level">The colour.</param>
/// <param name="Output">The first lines of output (and of errors), for the tooltip.</param>
public sealed record CommandTileReading(string Label, double? Percent, CommandLevel Level, string Output);

/// <summary>Turns a run's output and exit code into what the tile shows.</summary>
public static partial class CommandTileOutput
{
    public const int MaxLabelLength = 40;

    public const int MaxOutputLines = 6;

    /// <summary>The longest output line shown in the tooltip (the run's whole output can be one line of 64 K).</summary>
    public const int MaxShownLineLength = 200;

    /// <summary>The reading of a run that ended with <paramref name="exitCode"/>, or did not end in time (<paramref name="exitCode"/> null).</summary>
    public static CommandTileReading Read(CommandTileSettings settings, string? output, string? errors, int? exitCode)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var lines = Lines(output);
        var errorLines = Lines(errors);
        var level = exitCode switch
        {
            null => CommandLevel.Failed,
            0 => CommandLevel.Ok,
            1 => CommandLevel.Warning,
            _ => CommandLevel.Failed,
        };

        var shown = string.Join('\n', lines.Concat(errorLines).Take(MaxOutputLines).Select(line => Shorten(line, MaxShownLineLength)));
        if (!settings.ShowsGauge)
        {
            var first = lines.FirstOrDefault() ?? errorLines.FirstOrDefault();
            var label = first is null ? (exitCode is null ? "…" : exitCode == 0 ? "✓" : exitCode.Value.ToString(CultureInfo.InvariantCulture)) : Shorten(first, MaxLabelLength);
            return new CommandTileReading(label, null, level, shown);
        }

        var percent = lines.Select(Number).FirstOrDefault(n => n is not null);
        if (percent is { } p && level == CommandLevel.Ok)
        {
            level = p >= settings.CriticalAt ? CommandLevel.Failed : p >= settings.WarningAt ? CommandLevel.Warning : CommandLevel.Ok;
        }

        var clamped = percent is { } value ? Math.Clamp(value, 0, 100) : (double?)null;
        var text = percent is { } number ? Math.Round(number).ToString(CultureInfo.InvariantCulture) : "?";
        return new CommandTileReading(text, clamped, level, shown);
    }

    /// <summary>The first number on a line (<c>42</c>, <c>-3.5</c>, <c>87%</c>, <c>queue: 12</c>), read the same way on every PC.</summary>
    public static double? Number(string line) =>
        NumberPattern().Match(line) is { Success: true } match
        && double.TryParse(match.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
        && double.IsFinite(value)
            ? value
            : null;

    private static List<string> Lines(string? text) =>
        (text ?? string.Empty).Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0).ToList();

    public static string Shorten(string line, int length) => line.Length > length ? line[..(length - 1)] + "…" : line;

    [GeneratedRegex(@"-?\d+(\.\d+)?")]
    private static partial Regex NumberPattern();
}
