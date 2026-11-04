using DesktopIconGrid.Core.Launching;
using DesktopIconGrid.Core.Models;

namespace DesktopIconGrid.Core.Tests;

public class DropArgumentsTests
{
    [Fact]
    public void Without_placeholder_files_are_appended_in_one_launch()
    {
        var sets = DropArguments.BuildArgumentSets("--open", ["C:\\a b.txt", "C:\\c.txt"]);

        Assert.Equal(["--open \"C:\\a b.txt\" \"C:\\c.txt\""], sets);
    }

    [Fact]
    public void Empty_parameters_yield_just_the_files()
    {
        Assert.Equal(["\"C:\\a.txt\""], DropArguments.BuildArgumentSets(null, ["C:\\a.txt"]));
        Assert.Equal(["\"C:\\a.txt\""], DropArguments.BuildArgumentSets("   ", [" C:\\a.txt "]));
    }

    [Fact]
    public void File_placeholder_launches_once_per_file()
    {
        var sets = DropArguments.BuildArgumentSets("-edit {FILE} --fast", ["C:\\a.txt", "C:\\b.txt"]);

        Assert.Equal(["-edit \"C:\\a.txt\" --fast", "-edit \"C:\\b.txt\" --fast"], sets);
    }

    [Fact]
    public void Files_placeholder_launches_once_with_all_files()
    {
        var sets = DropArguments.BuildArgumentSets("{files} /merge", ["C:\\a.txt", "C:\\b.txt"]);

        Assert.Equal(["\"C:\\a.txt\" \"C:\\b.txt\" /merge"], sets);
    }

    [Theory]
    [InlineData("C:\\", "\"C:\\\\\"")]
    [InlineData("C:\\dir\\", "\"C:\\dir\\\\\"")]
    [InlineData("C:\\a b\\x.txt", "\"C:\\a b\\x.txt\"")]
    [InlineData("say \"hi\"", "\"say \\\"hi\\\"\"")]
    [InlineData("tail\\\"", "\"tail\\\\\\\"\"")]
    public void Quote_follows_windows_rules(string argument, string expected)
    {
        Assert.Equal(expected, DropArguments.Quote(argument));
    }

    [Fact]
    public void Quoted_placeholders_are_not_double_quoted()
    {
        Assert.Equal(["-f \"C:\\a.txt\""], DropArguments.BuildArgumentSets("-f \"{file}\"", ["C:\\a.txt"]));
        Assert.Equal(["\"C:\\a.txt\" \"C:\\b.txt\""], DropArguments.BuildArgumentSets("\"{files}\"", ["C:\\a.txt", "C:\\b.txt"]));
    }

    [Fact]
    public void Blank_paths_are_ignored()
    {
        Assert.Empty(DropArguments.BuildArgumentSets("{file}", ["", "  "]));
        Assert.Empty(DropArguments.BuildArgumentSets(null, []));
    }

    [Theory]
    [InlineData("C:\\Tools\\editor.exe", true)]
    [InlineData("notepad.exe", true)]
    [InlineData("C:\\Users\\me\\Documents", true)]
    [InlineData("https://example.com", false)]
    [InlineData("shell:MyComputerFolder", false)]
    [InlineData("special:show-desktop", false)]
    [InlineData("", false)]
    public void Only_programs_and_files_support_open_with(string location, bool expected)
    {
        Assert.Equal(expected, DropArguments.SupportsOpenWith(new ShortcutItem { Location = location }));
    }
}
