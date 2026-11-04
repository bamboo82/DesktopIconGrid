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
