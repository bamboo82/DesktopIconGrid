using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using DesktopIconGrid.Core.Diagnostics;
using DesktopIconGrid.Core.Launching;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Resources;

namespace DesktopIconGrid.Services;

/// <summary>
/// Executes a <see cref="LaunchPlan"/>: starts processes (with output logging when the shortcut asks for it),
/// hands them to the <see cref="ProcessTracker"/>, or drives the Shell COM object for special actions.
/// </summary>
public sealed class LaunchService(ProcessTracker tracker, ILaunchPrompts? prompts = null) : ILaunchService
{
    public LaunchResult Launch(ShortcutItem shortcut, bool asAdministrator = false)
    {
        ArgumentNullException.ThrowIfNull(shortcut);
        var result = LaunchCore(shortcut, asAdministrator: asAdministrator);
        Log(shortcut, result, asAdministrator || shortcut.RunAsAdministrator);
        return result;
    }

    private static void Log(ShortcutItem shortcut, LaunchResult result, bool asAdministrator = false)
    {
        var what = string.IsNullOrEmpty(shortcut.Parameters) ? shortcut.EffectiveLocation : $"{shortcut.EffectiveLocation} {shortcut.Parameters}";
        switch (result.Outcome)
        {
            case LaunchOutcome.Started:
                AppLog.Default.Info("Launch", $"Started \"{shortcut.Name}\"{(asAdministrator ? " as administrator" : string.Empty)}: {what}{(shortcut.CaptureLog && !asAdministrator ? " (output captured)" : string.Empty)}");
                break;
            case LaunchOutcome.Cancelled:
                AppLog.Default.Info("Launch", $"Cancelled \"{shortcut.Name}\" ({what})");
                break;
            default:
                AppLog.Default.Warning("Launch", $"Failed \"{shortcut.Name}\" ({what}): {result.ErrorMessage}");
                break;
        }
    }

    public LaunchResult LaunchWith(ShortcutItem shortcut, IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(shortcut);
        if (!DropArguments.SupportsOpenWith(shortcut))
        {
            return LaunchResult.Failure(Strings.Launch_CannotOpenFiles);
        }

        // Expand the template first so dropped paths containing '%' are never re-expanded.
        // cmd.exe's own pair around a /c command is put back after filling (CmdCommandLine).
        var template = string.IsNullOrWhiteSpace(shortcut.Parameters)
            ? null
            : CmdCommandLine.Unwrapped(shortcut.EffectiveLocation, Environment.ExpandEnvironmentVariables(shortcut.Parameters));

        // The files take the place of {file} or {files}, or go at the end: they are judged where they go (after ssh's
        // destination), on the template, before any value goes in and moves the words. {files} is judged as two {file},
        // since its second path is a word of its own. Every placeholder, the files at the end too, must be able to stand
        // where it does before anything is asked.
        var judged = LaunchPlaceholders.SplitFiles(
            template is not null
            && (template.Contains(DropArguments.FilePlaceholder, StringComparison.OrdinalIgnoreCase)
                || template.Contains(DropArguments.FilesPlaceholder, StringComparison.OrdinalIgnoreCase))
                ? template
                : $"{template} {DropArguments.FilesPlaceholder}");
        if (RefusedPlacement(shortcut, judged) is { } misplaced)
        {
            return misplaced;
        }

        string? clipboard = null;
        IReadOnlyList<string> answers = [];
        if (template is not null && LaunchPlaceholders.HasAny(template))
        {
            var values = AskForValues(shortcut, template);
            if (values.Result is { } stopped)
            {
                return stopped;
            }

            (clipboard, answers) = (values.Clipboard, values.Answers);
        }

        // Then everything goes in at once.
        var judgements = LaunchPlaceholders.FilePositions(judged).Select(index => JudgeAt(shortcut, judged, index)).Distinct().ToList();
        var terminal = LaunchPlaceholders.SplitsAtSemicolons(shortcut.EffectiveLocation, template);
        var relay = LaunchPlaceholders.DropsQuotes(shortcut.EffectiveLocation, template);
        foreach (var path in paths)
        {
            foreach (var judgement in judgements)
            {
                if (LaunchPlaceholders.RefusedInPath(path, judgement.Reader, judgement.Scp) is { } character)
                {
                    AppLog.Default.Warning("Launch", $"A dropped path was refused for {judgement.Reader}{(judgement.Scp == ScpOperand.None ? string.Empty : " (scp operand)")}: {path}");
                    return LaunchResult.Failure(RefusalMessage(character, judgement));
                }
            }

            if (relay is not null && LaunchPlaceholders.SplitByRelay(path) is not null)
            {
                AppLog.Default.Warning("Launch", $"A dropped path was refused: {relay} quotes it again only at a space, and it ends in a backslash: {path}");
                return LaunchResult.Failure(string.Format(Strings.Launch_RelaySplitsValue, relay));
            }

            if (terminal && path.Contains(';', StringComparison.Ordinal))
            {
                AppLog.Default.Warning("Launch", $"A dropped path was refused for Windows Terminal, which starts another command at \";\": {path}");
                return TerminalRefusal;
            }
        }

        var argumentSets = DropArguments.BuildArgumentSets(template, paths, clipboard, answers, fillInsideQuotes: true);
        if (argumentSets.Count == 0)
        {
            return LaunchResult.Failure(Strings.Launch_NothingToOpen);
        }

        var failures = new List<string>();
        foreach (var arguments in argumentSets)
        {
            var launch = (ShortcutItem)shortcut.Clone();
            launch.Parameters = CmdCommandLine.Wrapped(shortcut.EffectiveLocation, arguments);
            var result = LaunchCore(launch, expandParameters: false, template: shortcut.Parameters);
            Log(shortcut, result); // the template, as a click logs it: a filled value may be a password (pscp -pw {ask:Password})
            switch (result.Outcome)
            {
                case LaunchOutcome.Cancelled:
                    return result;
                case LaunchOutcome.Failed:
                    failures.Add(result.ErrorMessage ?? Strings.Common_UnknownError);
                    break;
            }
        }

        return failures.Count == 0 ? LaunchResult.Started : LaunchResult.Failure(string.Join("\n", failures.Distinct()));
    }

    /// <param name="template">The parameters as written, when <paramref name="shortcut"/>'s are already filled in: what the
    /// output log writes, never a filled value.</param>
    private LaunchResult LaunchCore(ShortcutItem shortcut, bool expandParameters = true, bool asAdministrator = false, string? template = null)
    {
        if (expandParameters && DropArguments.WithoutFilePlaceholders(shortcut.Parameters) is var forClick && forClick != shortcut.Parameters?.Trim())
        {
            // A click brings no files: {file} and {files} would reach the program as words (Open with fills them in).
            shortcut = (ShortcutItem)shortcut.Clone();
            shortcut.Parameters = forClick;
        }

        if (expandParameters && !string.IsNullOrWhiteSpace(shortcut.Parameters) && LaunchPlaceholders.HasAny(shortcut.Parameters))
        {
            // Expand the template first, then fill in the values: a '%' in the clipboard text or an answer stays as typed,
            // and so does a placeholder in them (one pass).
            var unwrapped = CmdCommandLine.Unwrapped(shortcut.EffectiveLocation, Environment.ExpandEnvironmentVariables(shortcut.Parameters));
            if (RefusedPlacement(shortcut, unwrapped) is { } misplaced)
            {
                return misplaced;
            }

            var values = AskForValues(shortcut, unwrapped);
            if (values.Result is { } stopped)
            {
                return stopped;
            }

            template = shortcut.Parameters;
            shortcut = (ShortcutItem)shortcut.Clone();
            shortcut.Parameters = CmdCommandLine.Wrapped(
                shortcut.EffectiveLocation, LaunchPlaceholders.Fill(unwrapped, values.Clipboard, values.Answers, fillInsideQuotes: true));
            expandParameters = false;
        }

        LaunchPlan plan;
        try
        {
            plan = LaunchPlanner.Plan(shortcut, expandParameters, asAdministrator);
        }
        catch (ArgumentException ex)
        {
            return LaunchResult.Failure(ex.Message);
        }

        switch (plan.Action)
        {
            case LaunchAction.ShowDesktop:
                return InvokeShell("ToggleDesktop");
            case LaunchAction.RunDialog:
                return InvokeShell("FileRun");
        }

        var startInfo = plan.StartInfo!;
        UseProgramFolderWhenStartInIsGone(shortcut, startInfo);
        if (CapturesOutput(shortcut, asAdministrator, startInfo))
        {
            return StartWithLog(shortcut, OutputCapture.WithCapture(startInfo), template is null ? null : $"{shortcut.EffectiveLocation} {template}");
        }

        var result = ProcessLauncher.Start(startInfo);
        if (result.Process is { } process)
        {
            tracker.Track(shortcut.Id, process);
        }

        return result;
    }

    /// <summary>
    /// Whether the start captures the program's output. Capturing needs the process started directly, which cannot ask
    /// for elevation, so an elevated start (from the click or from <see cref="ShortcutItem.RunAsAdministrator"/>) runs
    /// through the shell with the runas verb and uncaptured.
    /// </summary>
    internal static bool CapturesOutput(ShortcutItem shortcut, bool asAdministrator, ProcessStartInfo startInfo) =>
        shortcut.CaptureLog
        && !(asAdministrator || shortcut.RunAsAdministrator)
        && !LaunchPlanner.IsShellNamespace(shortcut.EffectiveLocation)
        && OutputCapture.CanCapture(startInfo);

    /// <summary>
    /// The values for <c>{clipboard}</c> and <c>{ask:…}</c> (see <see cref="LaunchPlaceholders"/>), each checked where it
    /// goes in <paramref name="template"/>, whose placements the caller has judged (<see cref="RefusedPlacement"/>); the
    /// caller puts them in with one pass. Returns a result instead when the launch stops: the clipboard holds no text, the
    /// user cancelled a question, a value holds something the program that reads the command line could act on (see
    /// <see cref="LaunchPlaceholders.Refused"/>) or that a program handing it on would split it at
    /// (<see cref="LaunchPlaceholders.SplitByRelay"/>), or a value would reach the program as an option or a switch (see
    /// <see cref="LaunchPlaceholders.TakesOptionAt"/>). The clipboard text is trimmed first: a copied line often ends in a
    /// line break.
    /// </summary>
    private (string? Clipboard, IReadOnlyList<string> Answers, LaunchResult? Result) AskForValues(ShortcutItem shortcut, string template)
    {
        var terminal = LaunchPlaceholders.SplitsAtSemicolons(shortcut.EffectiveLocation, template);
        var relay = LaunchPlaceholders.DropsQuotes(shortcut.EffectiveLocation, template);
        string? clipboard = null;
        if (LaunchPlaceholders.UsesClipboard(template))
        {
            clipboard = prompts?.ClipboardText()?.Trim();
            if (string.IsNullOrEmpty(clipboard))
            {
                return (null, [], LaunchResult.Failure(Strings.Launch_ClipboardEmpty));
            }

            foreach (var index in LaunchPlaceholders.ClipboardPositions(template)) // each place it goes is judged on its own
            {
                if (Refuse(clipboard, JudgeAt(shortcut, template, index), LaunchPlaceholders.TakesOptionAt(template, index, shortcut.EffectiveLocation), terminal, relay) is { } refused)
                {
                    return (null, [], refused);
                }
            }
        }

        var answers = new List<string>();
        var askPositions = LaunchPlaceholders.AskPositions(template);
        foreach (var prompt in LaunchPlaceholders.Prompts(template))
        {
            var answer = prompts?.Ask(shortcut.Name, prompt.Length == 0 ? Strings.Launch_AskValue : prompt);
            if (answer is null)
            {
                return (null, [], LaunchResult.Cancelled);
            }

            var position = askPositions[answers.Count];
            if (Refuse(answer, JudgeAt(shortcut, template, position), LaunchPlaceholders.TakesOptionAt(template, position, shortcut.EffectiveLocation), terminal, relay) is { } refused)
            {
                return (null, [], refused);
            }

            answers.Add(answer);
        }

        return (clipboard, answers, null);
    }

    /// <summary>
    /// Who reads the value of the placeholder at <paramref name="index"/>: the shortcut's program as Windows finds it (App
    /// Paths, its start-in folder, PATH), what its parameters hand on to, and where the placeholder stands among them.
    /// </summary>
    private static ArgumentReader ReaderAt(ShortcutItem shortcut, string template, int index) => JudgeAt(shortcut, template, index).Reader;

    /// <summary><see cref="ReaderAt"/>, with whether scp's colon rule applies to the value on top (review 27 B2).</summary>
    private static PlaceholderJudgement JudgeAt(ShortcutItem shortcut, string template, int index) =>
        LaunchPlaceholders.JudgeAt(shortcut.EffectiveLocation, template, index, shortcut.StartIn, InstalledPrograms.RegisteredPath);

    /// <summary>
    /// The refusal of a template with a placeholder that cannot stand where it does for the program that reads it (see
    /// <see cref="LaunchPlaceholders.PlacementAt"/>), before anything is asked: inside quotes with other text, where the
    /// value's own quotes would end the span; where cmd.exe or WSL's shell reads it after a program that hands it on
    /// without its quotes; or, for a value, among cmd.exe's own switches.
    /// </summary>
    private static LaunchResult? RefusedPlacement(ShortcutItem shortcut, string? template)
    {
        if (string.IsNullOrEmpty(template)
            || LaunchPlaceholders.FirstMisplaced(shortcut.EffectiveLocation, template, index => ReaderAt(shortcut, template, index)) is not { } misplaced)
        {
            return null;
        }

        switch (misplaced.Problem)
        {
            case PlacementProblem.InsideQuotes:
                AppLog.Default.Warning("Launch", "Not started: a placeholder stands inside quotes with other text, or touches a quote");
                return LaunchResult.Failure(Strings.Launch_PlaceholderInQuotes);
            case PlacementProblem.HandedOnWithoutQuotes:
                var relay = LaunchPlaceholders.DropsQuotes(shortcut.EffectiveLocation, template) ?? string.Empty;
                var reader = misplaced.Reader == ArgumentReader.Cmd ? "cmd.exe" : "WSL";
                AppLog.Default.Warning("Launch", $"Not started: {relay} hands a placeholder on without its quotes, where {reader} reads it");
                return LaunchResult.Failure(string.Format(Strings.Launch_RelayDropsQuotes, relay, reader));
            default:
                AppLog.Default.Warning("Launch", "Not started: a value would stand before cmd.exe's /c or /k, among its own switches");
                return LaunchResult.Failure(Strings.Launch_CmdSwitchValue);
        }
    }

    /// <summary>The refusal of a <c>;</c> for Windows Terminal (<see cref="LaunchPlaceholders.SplitsAtSemicolons"/>).</summary>
    private static LaunchResult TerminalRefusal => LaunchResult.Failure(string.Format(Strings.Launch_UnsafeValue, ';', "Windows Terminal"));

    private static LaunchResult? Refuse(string value, PlaceholderJudgement judgement, bool takesOption, bool terminal, string? relay)
    {
        var reader = judgement.Reader;
        if (LaunchPlaceholders.Refused(value, reader, judgement.Scp) is { } character)
        {
            AppLog.Default.Warning("Launch", character.Length == 0
                ? $"An empty placeholder value was refused for {reader}"
                : $"A placeholder value was refused: it holds U+{char.ConvertToUtf32(character, 0):X4}, which {reader}{(judgement.Scp == ScpOperand.None ? string.Empty : " or scp")} could act on");
            return LaunchResult.Failure(RefusalMessage(character, judgement));
        }

        if (relay is not null && LaunchPlaceholders.SplitByRelay(value) is { } split)
        {
            AppLog.Default.Warning("Launch", $"A placeholder value was refused: {relay} quotes it again only at a space, and U+{(int)split[0]:X4} would split it");
            return LaunchResult.Failure(string.Format(Strings.Launch_RelaySplitsValue, relay));
        }

        if (terminal && value.Contains(';', StringComparison.Ordinal))
        {
            AppLog.Default.Warning("Launch", "A placeholder value was refused: it holds \";\", where Windows Terminal starts another command");
            return TerminalRefusal;
        }

        if (takesOption && value.StartsWith('-'))
        {
            AppLog.Default.Warning("Launch", "A placeholder value was refused: it begins with \"-\" where an argument starts, so the program would take it as an option");
            return LaunchResult.Failure(Strings.Launch_ValueIsOption);
        }

        if (takesOption && LaunchPlaceholders.TakesSwitch(value, reader))
        {
            AppLog.Default.Warning("Launch", "A placeholder value was refused: it begins with \"/\" where an argument starts, so the program could take it as a switch");
            return LaunchResult.Failure(Strings.Launch_ValueIsSwitch);
        }

        return null;
    }

    private static string RefusalMessage(string character, PlaceholderJudgement judgement) => judgement.Reader == ArgumentReader.SshOption ? Strings.Launch_SshOptionValue : character switch
    {
        "\r" or "\n" => Strings.Launch_ValueHasLineBreak,
        "-" when judgement.Reader is ArgumentReader.SshDestination or ArgumentReader.SshDestinationPart => Strings.Launch_SshNameIsOption,
        "-" => judgement.Reader == ArgumentReader.RemoteShell ? Strings.Launch_WordIsRemoteOption : Strings.Launch_WordIsParameter, // a word of bare words
        _ when character[0] == LaunchPlaceholders.FullwidthQuotationMark => Strings.Launch_LookalikeQuote,
        ":" when judgement.Scp != ScpOperand.None => string.Format(Strings.Launch_UnsafeValue, character, "scp"),
        _ => string.Format(Strings.Launch_UnsafeValue, character, judgement.Reader switch
        {
            ArgumentReader.Cmd => "cmd.exe",
            ArgumentReader.PosixShell => "WSL",
            ArgumentReader.SshDestination or ArgumentReader.SshDestinationPart or ArgumentReader.RemoteShell => "ssh",
            ArgumentReader.Plain => "scp", // only scp's colon rule refuses anything more than every program does
            _ => "PowerShell",
        }),
    };

    /// <summary>
    /// A <see cref="ShortcutItem.StartIn"/> folder that is not on this PC (another PC's project folder, the folder of a
    /// program that moved) would stop the start with "The directory name is invalid": the program's own folder is used
    /// instead. Shares and removable drives are not looked at, so an offline share never holds up a click.
    /// </summary>
    internal static void UseProgramFolderWhenStartInIsGone(ShortcutItem shortcut, ProcessStartInfo startInfo)
    {
        // A share or a mounted removable drive counts as there without a look: an offline share would hold up the start.
        // A drive letter this PC does not have is not slow storage, and an optical drive is looked at: the program would
        // start there anyway.
        var folder = startInfo.WorkingDirectory;
        if (string.IsNullOrWhiteSpace(shortcut.StartIn) || string.IsNullOrEmpty(folder) || LocationProbe.CheckablePath(folder) is null
            || (IconWorker.IsOnSlowStorage(folder) && IconWorker.DriveTypeOf(folder) is not DriveType.CDRom) || Directory.Exists(folder))
        {
            return;
        }

        var own = LaunchPlanner.DefaultWorkingDirectory(Environment.ExpandEnvironmentVariables(shortcut.EffectiveLocation.Trim().Trim('"')));
        AppLog.Default.Warning("Launch", $"\"{shortcut.Name}\": Start in {folder} is not on this PC; it starts in {own ?? "the default folder"}");
        startInfo.WorkingDirectory = own ?? string.Empty;
    }

    private LaunchResult StartWithLog(ShortcutItem shortcut, ProcessStartInfo startInfo, string? commandLine)
    {
        Process? process;
        try
        {
            process = Process.Start(startInfo);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or FileNotFoundException or DirectoryNotFoundException)
        {
            return LaunchResult.Failure(ex.Message);
        }

        if (process is null)
        {
            return LaunchResult.Failure(Strings.Launch_NotStarted);
        }

        tracker.StartLog(shortcut, process, startInfo, commandLine);
        tracker.Track(shortcut.Id, process, disposeOnExit: false); // the log session drains the output, then disposes
        return LaunchResult.Started;
    }

    private static LaunchResult InvokeShell(string method)
    {
        object? shell = null;
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType is null)
            {
                return LaunchResult.Failure(Strings.Launch_ShellUnavailable);
            }

            shell = Activator.CreateInstance(shellType);
            if (shell is null)
            {
                return LaunchResult.Failure(Strings.Launch_ShellUnavailable);
            }

            shellType.InvokeMember(method, BindingFlags.InvokeMethod, null, shell, null);
            return LaunchResult.Started;
        }
        catch (Exception ex) when (ex is COMException or TargetInvocationException or MissingMethodException or InvalidCastException or MemberAccessException)
        {
            return LaunchResult.Failure((ex as TargetInvocationException)?.InnerException?.Message ?? ex.Message);
        }
        finally
        {
            if (shell is not null && Marshal.IsComObject(shell))
            {
                Marshal.ReleaseComObject(shell);
            }
        }
    }
}
