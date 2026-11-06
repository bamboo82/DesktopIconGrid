using DesktopIconGrid.Core.Launching;

namespace DesktopIconGrid.Core.Tests;

/// <summary>{clipboard} and {ask:} never reach cmd.exe or PowerShell script text with something they would run.</summary>
public class LaunchPlaceholderSafetyTests
{
    [Theory]
    [InlineData(@"C:\Tools\lookup.bat", "{clipboard}", ArgumentReader.Cmd)]
    [InlineData(@"%USERPROFILE%\run.CMD", "{ask:Name}", ArgumentReader.Cmd)]
    [InlineData("cmd.exe", "/c echo {clipboard}", ArgumentReader.Cmd)]
    [InlineData(@"C:\Windows\System32\cmd", "/k {clipboard}", ArgumentReader.Cmd)]
    [InlineData("powershell.exe", "{clipboard}", ArgumentReader.PowerShellCommand)] // -Command is the default
    [InlineData("powershell.exe", "-NoProfile -Command Get-Item {clipboard}", ArgumentReader.PowerShellCommand)]
    [InlineData("powershell", "-File script.ps1 {clipboard}", ArgumentReader.Plain)]
    [InlineData("powershell", "-File script.ps1 -Command {clipboard}", ArgumentReader.PowerShellCommand)]
    [InlineData("pwsh.exe", "script.ps1 {clipboard}", ArgumentReader.Plain)] // -File is the default
    [InlineData("pwsh.exe", "-c {clipboard}", ArgumentReader.PowerShellCommand)]
    [InlineData("pwsh", "-NoLogo -Comm {clipboard}", ArgumentReader.PowerShellCommand)]
    [InlineData("pwsh", "-ec {clipboard}", ArgumentReader.PowerShellCommand)]
    [InlineData("notepad.exe", "{clipboard}", ArgumentReader.Plain)]
    [InlineData("https://example.com/?q=", "{clipboard}", ArgumentReader.Plain)]
    public void The_reader_of_the_command_line_is_found_from_the_program_and_its_switches(string location, string parameters, ArgumentReader reader)
    {
        Assert.Equal(reader, LaunchPlaceholders.ReaderOf(location, parameters));
    }

    [Theory]
    [InlineData("x\" & calc & \"", '"')]
    [InlineData("100%PATH%", '%')]
    [InlineData("hi!there", '!')]
    [InlineData("a^b", '^')]
    [InlineData("a & calc", '&')]
    [InlineData("a | calc", '|')]
    [InlineData("a < b", '<')]
    [InlineData("a > b", '>')]
    [InlineData("one\ntwo", '\n')]
    [InlineData("one\rtwo", '\r')]
    public void Cmd_never_gets_quotes_variables_or_operators(string value, char refused)
    {
        Assert.Equal(refused, LaunchPlaceholders.Refused(value, ArgumentReader.Cmd));
    }

    [Theory]
    [InlineData("$(calc)", '$')]
    [InlineData("a`b", '`')]
    [InlineData("x; calc", ';')]
    [InlineData("it's", '\'')]
    [InlineData("say \"hi\"", '"')]
    public void PowerShell_script_text_never_gets_expansions_or_statement_breaks(string value, char refused)
    {
        Assert.Equal(refused, LaunchPlaceholders.Refused(value, ArgumentReader.PowerShellCommand));
    }

    [Fact]
    public void Plain_values_pass_and_only_a_line_break_is_refused_for_other_programs()
    {
        Assert.Null(LaunchPlaceholders.Refused(@"C:\Reports\Q3 summary (final).docx", ArgumentReader.Cmd));
        Assert.Null(LaunchPlaceholders.Refused("server-01.example.com", ArgumentReader.PowerShellCommand));
        Assert.Null(LaunchPlaceholders.Refused("x\" & calc & \"", ArgumentReader.Plain)); // one quoted argument, whatever it holds
        Assert.Equal('\n', LaunchPlaceholders.Refused("one\ntwo", ArgumentReader.Plain));
    }
}
