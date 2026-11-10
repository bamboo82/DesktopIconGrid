using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using DesktopIconGrid.Core.Commands;
using DesktopIconGrid.Core.Diagnostics;

namespace DesktopIconGrid.Services;

/// <summary>
/// How a second start hands its command line to the running bar: a named pipe of this Windows session, which both
/// ends open with <see cref="PipeOptions.CurrentUserOnly"/>, so neither another user's process nor a pipe another
/// user made under this name is talked to. One message per connection, read with a size limit and a time limit, and
/// checked as strictly as a command line (<see cref="BarCommands.Deserialize"/>) before anything is done with it.
/// </summary>
public sealed class CommandPipe : IDisposable
{
    /// <summary>How a hand-over went.</summary>
    public enum SendResult
    {
        Sent,

        /// <summary>No bar listened within the time (it may still be starting).</summary>
        NoAnswer,

        /// <summary>
        /// A bar listens but would not take it: <see cref="PipeOptions.CurrentUserOnly"/> compares the processes' token
        /// owners, which differ between an elevated start and a plain one of the same user.
        /// </summary>
        Refused,
    }

    private static readonly TimeSpan ReadLimit = TimeSpan.FromSeconds(5);

    private readonly CancellationTokenSource _stop = new();
    private readonly Action<IReadOnlyList<BarCommand>> _received;
    private readonly string _name;
    private Task? _loop;

    /// <param name="received">Called (on a worker thread) with each message's commands; the caller moves to the UI thread.</param>
    /// <param name="name">The pipe's name; <see cref="Name"/> when not given (tests use their own).</param>
    public CommandPipe(Action<IReadOnlyList<BarCommand>> received, string? name = null)
    {
        _received = received;
        _name = name ?? Name;
    }

    /// <summary>The pipe of this Windows session (the single-instance mutex is per session too).</summary>
    public static string Name => $"DesktopIconGrid.Commands.{Process.GetCurrentProcess().SessionId}";

    /// <summary>Starts listening for messages.</summary>
    public void Start() => _loop = Task.Run(() => ListenAsync(_stop.Token));

    /// <summary>Sends <paramref name="commands"/> to the running bar (to the pipe <paramref name="name"/>, <see cref="Name"/> by default).</summary>
    public static SendResult Send(IReadOnlyList<BarCommand> commands, TimeSpan timeout, string? name = null) =>
        SendBytes(BarCommands.Serialize(commands ?? throw new ArgumentNullException(nameof(commands))), timeout, name);

    /// <summary>Sends a message as it is (tests send what a hostile or broken client might).</summary>
    internal static SendResult SendBytes(byte[] message, TimeSpan timeout, string? name = null)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", name ?? Name, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            client.Connect((int)timeout.TotalMilliseconds);
            client.Write(message);
            client.Flush();
            return SendResult.Sent;
        }
        catch (UnauthorizedAccessException ex)
        {
            AppLog.Default.Warning("Commands", $"The running bar would not take the commands: {ex.Message}");
            return SendResult.Refused;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException)
        {
            AppLog.Default.Warning("Commands", $"The running bar could not be reached: {ex.Message}");
            return SendResult.NoAnswer;
        }
    }

    private async Task ListenAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    _name, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(stop).ConfigureAwait(false);
                var message = await ReadAsync(server, stop).ConfigureAwait(false);
                if (message is null)
                {
                    continue;
                }

                if (BarCommands.Deserialize(message) is { } commands)
                {
                    AppLog.Default.Info("Commands", $"{commands.Count} command(s) received from another start");
                    _received(commands);
                }
                else
                {
                    AppLog.Default.Warning("Commands", "A message that is no list of commands was ignored");
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Another bar of this user holds the name, or a client went away mid-message: wait a little and listen again.
                AppLog.Default.Warning("Commands", $"The command pipe failed: {ex.Message}");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), stop).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    /// <summary>The whole message, or <see langword="null"/> when it is too large or does not end within the time limit.</summary>
    private static async Task<byte[]?> ReadAsync(Stream stream, CancellationToken stop)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(stop);
        limit.CancelAfter(ReadLimit);
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        try
        {
            int read;
            while ((read = await stream.ReadAsync(chunk, limit.Token).ConfigureAwait(false)) > 0)
            {
                if (buffer.Length + read > BarCommands.MaxMessageBytes)
                {
                    AppLog.Default.Warning("Commands", "A message over the size limit was ignored");
                    return null;
                }

                buffer.Write(chunk, 0, read);
            }
        }
        catch (OperationCanceledException) when (!stop.IsCancellationRequested)
        {
            AppLog.Default.Warning("Commands", "A message that did not end in time was ignored");
            return null;
        }

        return buffer.ToArray();
    }

    public void Dispose()
    {
        _stop.Cancel();
        try
        {
            _loop?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
            // Stopping: what it threw no longer matters.
        }

        _stop.Dispose();
    }
}
