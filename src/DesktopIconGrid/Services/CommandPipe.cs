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
/// checked as strictly as a command line (<see cref="BarCommands.Deserialize"/>) before anything is done with it. The
/// pipe is in message mode, so the bar can answer in the same connection (review 27 C4): one byte, 1 when it took the
/// commands, 0 when it refused them (too many already wait for a dialog to close).
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

        /// <summary>The bar answered that it does not take them now: a dialog is open and too many commands already wait.</summary>
        Busy,
    }

    private static readonly TimeSpan ReadLimit = TimeSpan.FromSeconds(5);

    /// <summary>How long a start waits for the bar's answer once its message is written.</summary>
    private static readonly TimeSpan AnswerLimit = TimeSpan.FromSeconds(10);

    private const byte Taken = 1;
    private const byte NotTaken = 0;

    private readonly CancellationTokenSource _stop = new();
    private readonly Func<IReadOnlyList<BarCommand>, bool> _received;
    private readonly string _name;
    private Task? _loop;

    /// <param name="received">
    /// Called (on a worker thread) with each message's commands; whether the bar takes them, which the sender is told. The
    /// caller asks the UI thread and should only decide there, and carry them out later.
    /// </param>
    /// <param name="name">The pipe's name; <see cref="Name"/> when not given (tests use their own).</param>
    public CommandPipe(Func<IReadOnlyList<BarCommand>, bool> received, string? name = null)
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
            using var client = new NamedPipeClientStream(".", name ?? Name, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            client.Connect((int)timeout.TotalMilliseconds);
            client.ReadMode = PipeTransmissionMode.Message;
            client.Write(message);
            client.Flush();
            return Answer(client);
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

    /// <summary>
    /// The bar's answer to a written message. No byte at all (the bar dropped the message as unreadable, or closed) is no
    /// answer; one that does not come in time means a bar too busy to answer, which has the message and runs it later.
    /// </summary>
    private static SendResult Answer(NamedPipeClientStream client)
    {
        using var limit = new CancellationTokenSource(AnswerLimit);
        var answer = new byte[1];
        try
        {
            var read = client.ReadAsync(answer, limit.Token).AsTask().GetAwaiter().GetResult();
            return read == 0 ? SendResult.NoAnswer : answer[0] == Taken ? SendResult.Sent : SendResult.Busy;
        }
        catch (OperationCanceledException)
        {
            AppLog.Default.Warning("Commands", "The running bar did not answer in time; it has the commands");
            return SendResult.Sent;
        }
    }

    private async Task ListenAsync(CancellationToken stop)
    {
        var failures = 0; // in a row: said once, then waited out with longer pauses (review 21: a held name warned every 2 s for ever)
        while (!stop.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    _name, PipeDirection.InOut, 1, PipeTransmissionMode.Message, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(stop).ConfigureAwait(false);
                if (failures > 0)
                {
                    AppLog.Default.Info("Commands", "The command pipe listens again");
                    failures = 0;
                }

                var message = await ReadAsync(server, stop).ConfigureAwait(false);
                if (message is null)
                {
                    continue;
                }

                if (BarCommands.Deserialize(message) is { } commands)
                {
                    AppLog.Default.Info("Commands", $"{commands.Count} command(s) received from another start");
                    var taken = _received(commands);
                    await AnswerAsync(server, taken, stop).ConfigureAwait(false);
                }
                else
                {
                    AppLog.Default.Warning("Commands", "A message that is no list of commands was ignored"); // no answer: the sender is told nothing was taken
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // Another bar of this user holds the name, a client went away mid-message, or the receiver threw (the
                // window is closing): wait and listen again, whatever it was, so one bad message never ends the listening.
                failures++;
                if (failures == 1)
                {
                    AppLog.Default.Warning("Commands", $"The command pipe failed: {ex.Message}");
                }
                else
                {
                    AppLog.Default.Debug("Commands", $"The command pipe failed again ({failures}): {ex.Message}");
                }

                try
                {
                    await Task.Delay(RetryDelay(failures), stop).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    /// <summary>
    /// The pause before listening again after <paramref name="failures"/> failures in a row: 2 s, doubling up to 10 s. A
    /// second start waits for the bar up to its own timeout, so a longer pause would make it give up (review 27).
    /// </summary>
    internal static TimeSpan RetryDelay(int failures) =>
        TimeSpan.FromSeconds(Math.Min(10, 2 * Math.Pow(2, Math.Clamp(failures - 1, 0, 10))));

    /// <summary>Tells the sender whether its commands were taken; a sender that did not wait for the answer is no failure of the pipe.</summary>
    private static async Task AnswerAsync(NamedPipeServerStream server, bool taken, CancellationToken stop)
    {
        try
        {
            await server.WriteAsync(new[] { taken ? Taken : NotTaken }, stop).ConfigureAwait(false);
            await server.FlushAsync(stop).ConfigureAwait(false);
        }
        catch (IOException)
        {
            // The sender closed its end already.
        }
    }

    /// <summary>The whole message, or <see langword="null"/> when it is too large or does not end within the time limit.</summary>
    private static async Task<byte[]?> ReadAsync(NamedPipeServerStream stream, CancellationToken stop)
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
                if (stream.IsMessageComplete)
                {
                    break; // the sender keeps its end open for the answer
                }
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
