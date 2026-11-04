using System.ComponentModel;
using System.Diagnostics;

namespace DesktopIconGrid.Core.Launching;

/// <summary>Starts processes through the shell and maps the usual failures to a <see cref="LaunchResult"/>.</summary>
public static class ProcessLauncher
{
    private const int ErrorCancelled = 1223;

    public static LaunchResult Start(ProcessStartInfo startInfo)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        try
        {
            var process = Process.Start(startInfo);
            return process is null ? LaunchResult.Started : LaunchResult.Started with { Process = process };
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            return LaunchResult.Cancelled;
        }
        catch (Exception ex) when (ex is Win32Exception
                                   or InvalidOperationException
                                   or FileNotFoundException
                                   or DirectoryNotFoundException
                                   or PlatformNotSupportedException)
        {
            return LaunchResult.Failure(ex.Message);
        }
    }
}
