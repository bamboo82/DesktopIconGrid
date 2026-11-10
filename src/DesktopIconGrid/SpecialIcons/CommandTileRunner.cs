using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using DesktopIconGrid.Core.Launching;
using DesktopIconGrid.Core.SpecialIcons;

namespace DesktopIconGrid.SpecialIcons;

/// <summary>One run of a command tile: its output and errors (each cut at a limit), its exit code, or why it did not start.</summary>
/// <param name="ExitCode"><see langword="null"/> when it did not end within the time limit (it was stopped) or did not start.</param>
public sealed record CommandRun(string Output, string Errors, int? ExitCode, string? StartError, TimeSpan Duration);

/// <summary>
/// Runs a command tile's program without a window and with its output read (as a logged shortcut runs, through
/// <see cref="OutputCapture"/>): batch files through <c>cmd.exe /d /c</c>. A run that does not end within its time limit,
/// or that is cancelled (the tile left the bar), is stopped with its whole process tree.
/// </summary>
public static class CommandTileRunner
{
    /// <summary>The most characters kept of each stream; the rest is read and dropped, so the program never blocks on a full pipe.</summary>
    public const int MaxChars = 64 * 1024;

    public static ProcessStartInfo StartInfo(CommandTileSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var startIn = string.IsNullOrWhiteSpace(settings.StartIn) ? null : Environment.ExpandEnvironmentVariables(settings.StartIn.Trim());
        var source = new ProcessStartInfo(Environment.ExpandEnvironmentVariables(settings.Program.Trim()))
        {
            Arguments = settings.Arguments?.Trim() ?? string.Empty,
            WorkingDirectory = startIn ?? string.Empty,
        };
        return OutputCapture.WithCapture(source); // the output's encoding as for a logged shortcut
    }

    public static async Task<CommandRun> RunAsync(CommandTileSettings settings, CancellationToken stop)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var watch = Stopwatch.StartNew();
        Process process;
        try
        {
            process = Process.Start(StartInfo(settings)) ?? throw new InvalidOperationException("No process was started.");
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return new CommandRun(string.Empty, string.Empty, null, ex.Message, watch.Elapsed);
        }

        using (process)
        {
            var output = ReadAsync(process.StandardOutput);
            var errors = ReadAsync(process.StandardError);
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(stop);
            limit.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));
            int? exitCode;
            try
            {
                await process.WaitForExitAsync(limit.Token).ConfigureAwait(false);
                exitCode = process.ExitCode;
            }
            catch (OperationCanceledException)
            {
                exitCode = null;
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
                {
                    // It ended on its own meanwhile, or cannot be stopped: nothing more to do.
                }
            }

            // The pipes close when the process (and anything that inherited them) ends; do not wait long for that.
            var read = Task.WhenAll(output, errors);
            await Task.WhenAny(read, Task.Delay(TimeSpan.FromSeconds(2), CancellationToken.None)).ConfigureAwait(false);
            return new CommandRun(
                output.IsCompletedSuccessfully ? output.Result : string.Empty,
                errors.IsCompletedSuccessfully ? errors.Result : string.Empty,
                exitCode,
                null,
                watch.Elapsed);
        }
    }

    private static async Task<string> ReadAsync(StreamReader reader)
    {
        var kept = new StringBuilder();
        var buffer = new char[4096];
        int read;
        try
        {
            while ((read = await reader.ReadAsync(buffer).ConfigureAwait(false)) > 0)
            {
                var room = MaxChars - kept.Length;
                if (room > 0)
                {
                    kept.Append(buffer, 0, Math.Min(room, read));
                }
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            // The process was stopped mid-write: keep what came.
        }

        return kept.ToString().Replace("\r", string.Empty, StringComparison.Ordinal);
    }
}
