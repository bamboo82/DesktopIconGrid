using System.Reflection;
using System.Runtime.InteropServices;
using DesktopIconGrid.Core.Launching;
using DesktopIconGrid.Core.Models;

namespace DesktopIconGrid.Services;

/// <summary>Executes a <see cref="LaunchPlan"/>: starts processes, or drives the Shell COM object for special actions.</summary>
public sealed class LaunchService : ILaunchService
{
    public LaunchResult Launch(ShortcutItem shortcut)
    {
        LaunchPlan plan;
        try
        {
            plan = LaunchPlanner.Plan(shortcut);
        }
        catch (ArgumentException ex)
        {
            return LaunchResult.Failure(ex.Message);
        }

        return plan.Action switch
        {
            LaunchAction.StartProcess => ProcessLauncher.Start(plan.StartInfo!),
            LaunchAction.ShowDesktop => InvokeShell("ToggleDesktop"),
            LaunchAction.RunDialog => InvokeShell("FileRun"),
            _ => LaunchResult.Failure("Unsupported action."),
        };
    }

    public LaunchResult LaunchWith(ShortcutItem shortcut, IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(shortcut);
        if (!DropArguments.SupportsOpenWith(shortcut))
        {
            return LaunchResult.Failure("This shortcut cannot open files.");
        }

        var argumentSets = DropArguments.BuildArgumentSets(shortcut.Parameters, paths);
        if (argumentSets.Count == 0)
        {
            return LaunchResult.Failure("Nothing to open.");
        }

        var failures = new List<string>();
        foreach (var arguments in argumentSets)
        {
            var launch = (ShortcutItem)shortcut.Clone();
            launch.Parameters = arguments;
            var result = Launch(launch);
            switch (result.Outcome)
            {
                case LaunchOutcome.Cancelled:
                    return result;
                case LaunchOutcome.Failed:
                    failures.Add(result.ErrorMessage ?? "Unknown error");
                    break;
            }
        }

        return failures.Count == 0 ? LaunchResult.Started : LaunchResult.Failure(string.Join("\n", failures.Distinct()));
    }

    private static LaunchResult InvokeShell(string method)
    {
        object? shell = null;
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType is null)
            {
                return LaunchResult.Failure("Windows Shell automation is not available.");
            }

            shell = Activator.CreateInstance(shellType);
            if (shell is null)
            {
                return LaunchResult.Failure("Windows Shell automation is not available.");
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
