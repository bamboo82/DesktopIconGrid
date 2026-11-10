using System.Collections.Concurrent;
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
/// that is cancelled (the tile left the bar), or that is still under way when the app ends (<see cref="StopAll"/>) is
/// stopped with its whole process tree.
/// </summary>
public static class CommandTileRunner
{
    /// <summary>The most characters kept of each stream; the rest is read and dropped, so the program never blocks on a full pipe.</summary>
    public const int MaxChars = 64 * 1024;

    private static readonly ConcurrentDictionary<int, Process> Running = new();
    private static volatile bool _stopping;

    public static ProcessStartInfo StartInfo(CommandTileSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var startIn = string.IsNullOrWhiteSpace(settings.StartIn) ? null : Environment.ExpandEnvironmentVariables(settings.StartIn.Trim().Trim('"'));
        var source = new ProcessStartInfo(Environment.ExpandEnvironmentVariables(settings.Program.Trim().Trim('"'))) // typed with quotes: a .bat still goes through cmd
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
        var info = StartInfo(settings);
        Process process;
        try
        {
            // Off the UI thread: starting a program on a share that does not answer can take a long time.
            process = await Task.Run(() => Process.Start(info) ?? throw new InvalidOperationException("No process was started."), CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return new CommandRun(string.Empty, string.Empty, null, ex.Message, watch.Elapsed);
        }

        using (process)
        {
            Running[process.Id] = process;
            if (_stopping)
            {
                Kill(process); // started while the app was ending, after StopAll looked
            }

            try
            {
                var output = new StringBuilder();
                var errors = new StringBuilder();
                var reads = Task.WhenAll(ReadAsync(process.StandardOutput, output), ReadAsync(process.StandardError, errors));
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
                    Kill(process);
                }

                // The pipes close when the process and everything that inherited them end; a child started in the background
                // can hold them open. Wait a little, then take what was read so far.
                await Task.WhenAny(reads, Task.Delay(TimeSpan.FromSeconds(2), CancellationToken.None)).ConfigureAwait(false);
                return new CommandRun(Text(output), Text(errors), exitCode, null, watch.Elapsed);
            }
            finally
            {
                Running.TryRemove(process.Id, out _);
            }
        }
    }

    /// <summary>Stops every run under way (the app is ending), with its children.</summary>
    public static void StopAll()
    {
        _stopping = true;
        foreach (var process in Running.Values)
        {
            Kill(process);
        }
    }

    private static void Kill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException or AggregateException)
        {
            // It ended on its own meanwhile, or a child could not be stopped (AggregateException): nothing more to do here.
        }
    }

    private static string Text(StringBuilder builder)
    {
        lock (builder)
        {
            return builder.ToString().Replace("\r", string.Empty, StringComparison.Ordinal);
        }
    }

    private static async Task ReadAsync(StreamReader reader, StringBuilder kept)
    {
        var buffer = new char[4096];
        int read;
        try
        {
            while ((read = await reader.ReadAsync(buffer).ConfigureAwait(false)) > 0)
            {
                lock (kept)
                {
                    var room = MaxChars - kept.Length;
                    if (room > 0)
                    {
                        kept.Append(buffer, 0, Math.Min(room, read));
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            // The process was stopped mid-write: keep what came.
        }
    }
}
