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
        var template = string.IsNullOrWhiteSpace(shortcut.Parameters) ? null : Environment.ExpandEnvironmentVariables(shortcut.Parameters);
        if (template is not null && LaunchPlaceholders.HasAny(template))
        {
            var filled = FillPlaceholders(shortcut, template);
            if (filled.Result is { } stopped)
            {
                return stopped;
            }

            template = filled.Parameters;
        }

        // The files take the place of {file} or {files}, or go at the end: they are judged where they go (after ssh's destination).
        var judged = template is not null
                     && (template.Contains(DropArguments.FilePlaceholder, StringComparison.OrdinalIgnoreCase)
                         || template.Contains(DropArguments.FilesPlaceholder, StringComparison.OrdinalIgnoreCase))
            ? template
            : $"{template} {DropArguments.FilesPlaceholder}";
        var reader = ReaderOf(shortcut, judged);
        var terminal = LaunchPlaceholders.SplitsAtSemicolons(shortcut.EffectiveLocation, template);
        foreach (var path in paths)
        {
            if (LaunchPlaceholders.RefusedInPath(path, reader) is { } character)
            {
                AppLog.Default.Warning("Launch", $"A dropped path was refused for {reader}: {path}");
                return LaunchResult.Failure(RefusalMessage(character, reader));
            }

            if (terminal && path.Contains(';', StringComparison.Ordinal))
            {
                AppLog.Default.Warning("Launch", $"A dropped path was refused for Windows Terminal, which starts another command at \";\": {path}");
                return TerminalRefusal;
            }
        }

        var argumentSets = DropArguments.BuildArgumentSets(template, paths);
        if (argumentSets.Count == 0)
        {
            return LaunchResult.Failure(Strings.Launch_NothingToOpen);
        }

        var failures = new List<string>();
        foreach (var arguments in argumentSets)
        {
            var launch = (ShortcutItem)shortcut.Clone();
            launch.Parameters = arguments;
            var result = LaunchCore(launch, expandParameters: false);
            Log(launch, result);
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

    private LaunchResult LaunchCore(ShortcutItem shortcut, bool expandParameters = true, bool asAdministrator = false)
    {
        if (expandParameters && !string.IsNullOrWhiteSpace(shortcut.Parameters) && LaunchPlaceholders.HasAny(shortcut.Parameters))
        {
            // Expand the template first, then fill in the values: a '%' in the clipboard text or an answer stays as typed.
            var filled = FillPlaceholders(shortcut, Environment.ExpandEnvironmentVariables(shortcut.Parameters));
            if (filled.Result is { } stopped)
            {
                return stopped;
            }

            shortcut = (ShortcutItem)shortcut.Clone();
            shortcut.Parameters = filled.Parameters;
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
        if (CapturesOutput(shortcut, asAdministrator, startInfo))
        {
            return StartWithLog(shortcut, OutputCapture.WithCapture(startInfo));
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
    /// Fills in <c>{clipboard}</c> and <c>{ask:…}</c> (see <see cref="LaunchPlaceholders"/>). Returns a result instead
    /// when the launch stops: the clipboard holds no text, the user cancelled a question, a value holds something the
    /// program that reads the command line could act on (see <see cref="LaunchPlaceholders.Refused"/>), or a value would
    /// reach the program as an option (see <see cref="LaunchPlaceholders.TakesOptionAt"/>). The clipboard text is trimmed
    /// first: a copied line often ends in a line break.
    /// </summary>
    private (string Parameters, LaunchResult? Result) FillPlaceholders(ShortcutItem shortcut, string template)
    {
        var reader = ReaderOf(shortcut, template);
        var terminal = LaunchPlaceholders.SplitsAtSemicolons(shortcut.EffectiveLocation, template);
        string? clipboard = null;
        if (LaunchPlaceholders.UsesClipboard(template))
        {
            clipboard = prompts?.ClipboardText()?.Trim();
            if (string.IsNullOrEmpty(clipboard))
            {
                return (template, LaunchResult.Failure(Strings.Launch_ClipboardEmpty));
            }

            var takesOption = LaunchPlaceholders.ClipboardPositions(template).Any(index => LaunchPlaceholders.TakesOptionAt(template, index));
            if (Refuse(clipboard, reader, takesOption, terminal) is { } refused)
            {
                return (template, refused);
            }
        }

        var answers = new List<string>();
        var askPositions = LaunchPlaceholders.AskPositions(template);
        foreach (var prompt in LaunchPlaceholders.Prompts(template))
        {
            var answer = prompts?.Ask(shortcut.Name, prompt.Length == 0 ? Strings.Launch_AskValue : prompt);
            if (answer is null)
            {
                return (template, LaunchResult.Cancelled);
            }

            if (Refuse(answer, reader, LaunchPlaceholders.TakesOptionAt(template, askPositions[answers.Count]), terminal) is { } refused)
            {
                return (template, refused);
            }

            answers.Add(answer);
        }

        return (LaunchPlaceholders.Fill(template, clipboard, answers), null);
    }

    /// <summary>Who reads the shortcut's command line: its program as Windows finds it (App Paths, its start-in folder, PATH), and what its parameters hand on to.</summary>
    private static ArgumentReader ReaderOf(ShortcutItem shortcut, string? template) =>
        LaunchPlaceholders.ReaderOf(shortcut.EffectiveLocation, template, shortcut.StartIn, InstalledPrograms.RegisteredPath);

    /// <summary>The refusal of a <c>;</c> for Windows Terminal (<see cref="LaunchPlaceholders.SplitsAtSemicolons"/>).</summary>
    private static LaunchResult TerminalRefusal => LaunchResult.Failure(string.Format(Strings.Launch_UnsafeValue, ';', "Windows Terminal"));

    private static LaunchResult? Refuse(string value, ArgumentReader reader, bool takesOption, bool terminal)
    {
        if (LaunchPlaceholders.Refused(value, reader) is { } character)
        {
            AppLog.Default.Warning("Launch", $"A placeholder value was refused: it holds U+{char.ConvertToUtf32(character, 0):X4}, which {reader} could act on");
            return LaunchResult.Failure(RefusalMessage(character, reader));
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

        return null;
    }

    private static string RefusalMessage(string character, ArgumentReader reader) => character switch
    {
        "\r" or "\n" => Strings.Launch_ValueHasLineBreak,
        "-" => reader == ArgumentReader.RemoteShell ? Strings.Launch_WordIsRemoteOption : Strings.Launch_WordIsParameter, // a word of bare words
        _ when character[0] == LaunchPlaceholders.FullwidthQuotationMark => Strings.Launch_LookalikeQuote,
        _ => string.Format(Strings.Launch_UnsafeValue, character, reader switch
        {
            ArgumentReader.Cmd => "cmd.exe",
            ArgumentReader.PosixShell => "WSL",
            ArgumentReader.RemoteShell => "ssh",
            _ => "PowerShell",
        }),
    };

    private LaunchResult StartWithLog(ShortcutItem shortcut, ProcessStartInfo startInfo)
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

        tracker.StartLog(shortcut, process, startInfo);
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
