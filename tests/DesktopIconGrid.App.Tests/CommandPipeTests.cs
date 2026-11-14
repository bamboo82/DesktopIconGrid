using System.Text;
using DesktopIconGrid.Core.Commands;
using DesktopIconGrid.Services;

namespace DesktopIconGrid.App.Tests;

/// <summary>
/// A second start hands its command line to the running bar through the session's pipe. Each test uses a pipe of its
/// own, so a bar running on the same PC is neither shown nor in the way (review 21 C4).
/// </summary>
public class CommandPipeTests
{
    private static string NewName() => "DesktopIconGrid.Tests." + Guid.NewGuid().ToString("N");

    [Fact]
    public async Task A_message_reaches_the_listening_bar_and_nothing_is_sent_without_one()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var name = NewName();
        Assert.Equal(CommandPipe.SendResult.NoAnswer, CommandPipe.Send([new BarCommand(BarCommandKind.Show)], TimeSpan.FromMilliseconds(200), name));

        var received = new TaskCompletionSource<IReadOnlyList<BarCommand>>(TaskCreationOptions.RunContinuationsAsynchronously);
        using (var pipe = new CommandPipe(commands => received.TrySetResult(commands), name))
        {
            pipe.Start();
            BarCommand[] sent = [new(BarCommandKind.Add, @"C:\a.txt", "Tools"), new(BarCommandKind.Launch, "Mail")];

            Assert.Equal(CommandPipe.SendResult.Sent, CommandPipe.Send(sent, TimeSpan.FromSeconds(5), name));
            Assert.Equal(sent, await received.Task.WaitAsync(TimeSpan.FromSeconds(10)));
        }
    }

    [Fact]
    public async Task A_broken_or_oversized_message_is_dropped_and_the_bar_keeps_listening()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var name = NewName();
        var received = new List<IReadOnlyList<BarCommand>>();
        var good = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var pipe = new CommandPipe(
            commands =>
            {
                lock (received)
                {
                    received.Add(commands);
                }

                good.TrySetResult();
                return true;
            },
            name);
        pipe.Start();

        // Dropped without an answer, so the sender knows nothing was taken (review 27 C4: the pipe answers now).
        Assert.Equal(CommandPipe.SendResult.NoAnswer, CommandPipe.SendBytes(Encoding.UTF8.GetBytes("not json"), TimeSpan.FromSeconds(5), name));
        Assert.Equal(CommandPipe.SendResult.NoAnswer, CommandPipe.SendBytes(Encoding.UTF8.GetBytes("""{"version":1,"commands":[{"kind":"Add","value":"relative"}]}"""), TimeSpan.FromSeconds(5), name));
        // Over the cap: the writer may see the pipe closed under it (NoAnswer), which is fine; nothing may arrive.
        CommandPipe.SendBytes(new byte[BarCommands.MaxMessageBytes + 1024], TimeSpan.FromSeconds(5), name);

        Assert.Equal(CommandPipe.SendResult.Sent, CommandPipe.Send([new BarCommand(BarCommandKind.Toggle)], TimeSpan.FromSeconds(5), name));
        await good.Task.WaitAsync(TimeSpan.FromSeconds(10));

        lock (received)
        {
            Assert.Equal([new BarCommand(BarCommandKind.Toggle)], Assert.Single(received));
        }
    }
    [Fact]
    public void A_bar_that_does_not_take_the_commands_says_so_to_the_sender()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        // Review 27 C4: a dialog is open and 200 commands already wait.
        var name = NewName();
        using var pipe = new CommandPipe(_ => false, name);
        pipe.Start();

        Assert.Equal(CommandPipe.SendResult.Busy, CommandPipe.Send([new BarCommand(BarCommandKind.Launch, "Mail")], TimeSpan.FromSeconds(5), name));
    }
}
