using DesktopIconGrid.Core.Launching;

namespace DesktopIconGrid.Core.Tests;

/// <summary>cmd.exe's /c, /k and /r, which take off the first and the last quote of the command (review 16 P4).</summary>
public class CmdCommandLineTests
{
    [Theory]
    [InlineData("cmd.exe", "/c \"C:\\t\\a&calc&.txt\"", "/c \"\"C:\\t\\a&calc&.txt\"\"")] // the pair cmd takes off is the wrap
    [InlineData("C:\\Windows\\System32\\CMD.EXE", "/d /s /K \"C:\\x.bat\" \"a b\"", "/d /s /K \"\"C:\\x.bat\" \"a b\"\"")]
    [InlineData("cmd", "/c type \"C:\\t\\a&b.txt\"", "/c type \"C:\\t\\a&b.txt\"")] // no quote first: cmd takes off nothing
    [InlineData("cmd.exe", "/q dir", "/q dir")] // no command switch
    [InlineData("notepad.exe", "/c \"x\"", "/c \"x\"")] // not cmd.exe
    public void A_command_that_starts_with_a_quote_gets_one_more_pair(string location, string arguments, string wrapped)
    {
        Assert.Equal(wrapped, CmdCommandLine.Wrapped(location, arguments));
    }

    [Theory]
    [InlineData("/c \"\"C:\\Program Files\\x.exe\" \"{file}\"\"", "/c \"C:\\Program Files\\x.exe\" \"{file}\"")] // written around the whole command
    [InlineData("/c \"\"{file}\"\"", "/c \"{file}\"")]
    [InlineData("/c \"C:\\x.exe\" {file}", "/c \"C:\\x.exe\" {file}")] // a single quote first: nothing to take off
    public void A_pair_written_around_the_whole_command_is_read_from_inside(string template, string unwrapped)
    {
        Assert.Equal(unwrapped, CmdCommandLine.Unwrapped("cmd.exe", template));
        Assert.Equal(template, CmdCommandLine.Unwrapped("notepad.exe", template));
    }

    [Fact]
    public void Unwrapped_filled_and_wrapped_gives_back_the_written_pair_with_the_value_quoted_inside()
    {
        var template = CmdCommandLine.Unwrapped("cmd.exe", "/c \"\"{file}\"\"");
        var filled = Assert.Single(DropArguments.BuildArgumentSets(template, [@"C:\t\a&calc&.txt"]));
        Assert.Equal("/c \"\"C:\\t\\a&calc&.txt\"\"", CmdCommandLine.Wrapped("cmd.exe", filled));
    }
}
