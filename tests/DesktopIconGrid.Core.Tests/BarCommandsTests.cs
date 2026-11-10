using System.Text;
using DesktopIconGrid.Core.Commands;

namespace DesktopIconGrid.Core.Tests;

/// <summary>The command line, and the message a second start hands to the running bar.</summary>
public class BarCommandsTests
{
    [Fact]
    public void Add_takes_every_path_up_to_the_next_option_and_a_group_after_them()
    {
        var (commands, error) = BarCommands.Parse(["--hidden", "--add", @"C:\a.txt", @"\\nas\share\b", "--group", "Tools", "--show", "--launch", "Mail"]);

        Assert.Null(error);
        Assert.Equal(
        [
            new BarCommand(BarCommandKind.Add, @"C:\a.txt", "Tools"),
            new BarCommand(BarCommandKind.Add, @"\\nas\share\b", "Tools"),
            new BarCommand(BarCommandKind.Show),
            new BarCommand(BarCommandKind.Launch, "Mail"),
        ], commands);
        Assert.Empty(BarCommands.Parse(["--hidden"]).Commands); // the Run entry's start: nothing to do
    }

    [Theory]
    [InlineData("--add")] // no path
    [InlineData("--add", "relative.txt")]
    [InlineData("--add", @"\\?\C:\x")] // a device path
    [InlineData("--add", @"C:\a?.txt")]
    [InlineData("--add", @"C:\a.txt", "--group")]
    [InlineData("--launch")]
    [InlineData("--launch", "--show")]
    [InlineData("--remove", "x")]
    [InlineData(@"C:\a.txt")] // a path without --add
    public void A_command_line_that_is_not_understood_does_nothing(params string[] args)
    {
        var (commands, error) = BarCommands.Parse(args);

        Assert.Empty(commands);
        Assert.NotNull(error);
    }

    [Fact]
    public void A_message_carries_the_commands_and_is_checked_as_strictly_on_arrival()
    {
        BarCommand[] sent = [new(BarCommandKind.Add, @"C:\a.txt", "Tools"), new(BarCommandKind.Toggle)];

        Assert.Equal(sent, BarCommands.Deserialize(BarCommands.Serialize(sent)));
        Assert.Null(BarCommands.Deserialize(Encoding.UTF8.GetBytes("""{"version":1,"commands":[{"kind":"Add","value":"relative"}]}""")));
        Assert.Null(BarCommands.Deserialize(Encoding.UTF8.GetBytes("""{"version":2,"commands":[]}""")));
        Assert.Null(BarCommands.Deserialize(Encoding.UTF8.GetBytes("""{"version":1,"commands":[{"kind":"Show","value":"x"}]}""")));
        Assert.Null(BarCommands.Deserialize(Encoding.UTF8.GetBytes("""{"version":1,"commands":[{"kind":"Launch","value":"a\nb"}]}""")));
        Assert.Null(BarCommands.Deserialize(Encoding.UTF8.GetBytes("""{"version":1,"commands":[{"kind":"Delete"}]}""")));
        Assert.Null(BarCommands.Deserialize(Encoding.UTF8.GetBytes("not json")));
        Assert.Null(BarCommands.Deserialize(new byte[BarCommands.MaxMessageBytes + 1]));
        Assert.Null(BarCommands.Deserialize(BarCommands.Serialize(Enumerable.Repeat(new BarCommand(BarCommandKind.Show), BarCommands.MaxCommands + 1).ToList())));
    }

    [Fact]
    public void A_group_may_come_before_the_paths_as_a_Send_to_shortcut_gives_them()
    {
        // The shortcut's arguments are "--add --group Tools"; Explorer appends the chosen files (review 21 C2).
        var (commands, error) = BarCommands.Parse(["--add", "--group", "Tools", @"C:\f1.txt", @"C:\f2.txt"]);

        Assert.Null(error);
        Assert.Equal([new BarCommand(BarCommandKind.Add, @"C:\f1.txt", "Tools"), new BarCommand(BarCommandKind.Add, @"C:\f2.txt", "Tools")], commands);
        Assert.NotNull(BarCommands.Parse(["--add", "--group", "A", @"C:\f.txt", "--group", "B"]).Error);
    }
}
