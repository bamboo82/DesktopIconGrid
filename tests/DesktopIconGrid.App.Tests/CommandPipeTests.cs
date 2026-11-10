using DesktopIconGrid.Core.Commands;
using DesktopIconGrid.Services;

namespace DesktopIconGrid.App.Tests;

/// <summary>A second start hands its command line to the running bar through the session's pipe.</summary>
public class CommandPipeTests
{
    [Fact]
    public async Task A_message_reaches_the_listening_bar_and_nothing_is_sent_without_one()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        Assert.False(CommandPipe.Send([new BarCommand(BarCommandKind.Show)], TimeSpan.FromMilliseconds(200)));

        var received = new TaskCompletionSource<IReadOnlyList<BarCommand>>(TaskCreationOptions.RunContinuationsAsynchronously);
        using (var pipe = new CommandPipe(commands => received.TrySetResult(commands)))
        {
            pipe.Start();
            BarCommand[] sent = [new(BarCommandKind.Add, @"C:\a.txt", "Tools"), new(BarCommandKind.Launch, "Mail")];

            Assert.True(CommandPipe.Send(sent, TimeSpan.FromSeconds(5)));
            Assert.Equal(sent, await received.Task.WaitAsync(TimeSpan.FromSeconds(10)));
        }
    }
}
