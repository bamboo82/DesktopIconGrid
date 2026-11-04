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
public sealed class LaunchService(ProcessTracker tracker) : ILaunchService
{
    public LaunchResult Launch(ShortcutItem shortcut)
    {
        ArgumentNullException.ThrowIfNull(shortcut);
        var result = LaunchCore(shortcut);
        Log(shortcut, result);
        return result;
    }

    private static void Log(ShortcutItem shortcut, LaunchResult result)
    {
        var what = string.IsNullOrEmpty(shortcut.Parameters) ? shortcut.Location : $"{shortcut.Location} {shortcut.Parameters}";
        switch (result.Outcome)
        {
            case LaunchOutcome.Started:
                AppLog.Default.Info("Launch", $"Started \"{shortcut.Name}\": {what}{(shortcut.CaptureLog ? " (output captured)" : string.Empty)}");
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

    private LaunchResult LaunchCore(ShortcutItem shortcut, bool expandParameters = true)
    {
        LaunchPlan plan;
        try
        {
            plan = LaunchPlanner.Plan(shortcut, expandParameters);
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
        if (shortcut.CaptureLog && !LaunchPlanner.IsShellNamespace(shortcut.Location) && OutputCapture.CanCapture(startInfo))
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
