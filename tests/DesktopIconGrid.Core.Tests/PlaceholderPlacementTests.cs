using DesktopIconGrid.Core.Launching;

namespace DesktopIconGrid.Core.Tests;

/// <summary>
/// Where a placeholder may stand, for the program that reads it (review 17): the quotes around it as each reader sees
/// them, programs that hand it on without its quotes, and cmd.exe's own switches.
/// </summary>
public class PlaceholderPlacementTests
{
    /// <summary>Who reads the placeholder at <paramref name="index"/>, looked up nowhere on this machine.</summary>
    private static ArgumentReader ReaderAt(string location, string template, int index) =>
        LaunchPlaceholders.ReaderAt(location, template, index, searchPath: string.Empty, pathExtensions: ".EXE;.CMD", systemDirectory: string.Empty);

    private static PlacementProblem Placement(string location, string template, string placeholder = "{")
    {
        var index = template.IndexOf(placeholder, StringComparison.Ordinal);
        return LaunchPlaceholders.PlacementAt(location, template, index, ReaderAt(location, template, index));
    }

    [Theory]
    [InlineData("ssh.exe", "server2 \"tail -n 50 {ask:File}\"")] // review 17 G3: the remote shell reads bare words anyway
    [InlineData("powershell.exe", "-Command \"Get-Content {file}\"")]
    [InlineData("pwsh.exe", "-c \"Write-Output '{clipboard}'\"")] // and PowerShell's own quotes, too
    public void A_quoted_command_for_the_remote_shell_or_PowerShell_takes_a_placeholder(string location, string template)
    {
        Assert.Equal(PlacementProblem.None, Placement(location, template));
    }

    [Fact]
    public void A_value_inside_quotes_goes_in_as_it_is_with_its_last_backslashes_doubled()
    {
        Assert.Equal(
            "server2 \"tail -n 50 notes.txt\"",
            LaunchPlaceholders.Fill("server2 \"tail -n 50 {ask:File}\"", null, ["notes.txt"], fillInsideQuotes: true));
        Assert.Equal(
            "-Command \"Get-ChildItem C:\\t\\\\\"",
            LaunchPlaceholders.Fill("-Command \"Get-ChildItem {ask:Folder}\"", null, [@"C:\t\"], fillInsideQuotes: true));
        Assert.Equal(
            "-Command \"Get-ChildItem C:\\a C:\\b\" -x",
            Assert.Single(DropArguments.BuildArgumentSets("-Command \"Get-ChildItem {files}\" -x", [@"C:\a", @"C:\b"], fillInsideQuotes: true)));
        Assert.Throws<ArgumentException>(() => LaunchPlaceholders.Fill("-c \"x {clipboard}\"", "a\"b", [], fillInsideQuotes: true));
        Assert.Throws<ArgumentException>(() => LaunchPlaceholders.Fill("-c \"x {clipboard}\"", "a", []));
    }

    [Theory]
    [InlineData("notepad.exe", "\"--in={file}\"")] // a program's own parser: its value's quotes would end the span
    [InlineData("wsl.exe", "git commit -m \"WIP {clipboard}\"")]
    [InlineData("wsl.exe", "git commit -m 'WIP {clipboard}'")] // review 17 P9: bash's single quotes
    [InlineData("wsl.exe", "cat '{file}'")]
    [InlineData("wsl.exe", "printf $'%s\\n' $'x {clipboard}'")]
    [InlineData("msedge.exe", "\"https://www.bing.com/search?q=\"{clipboard}")] // review 17 P10: a "" CommandLineToArgvW ends the span at
    [InlineData("msedge.exe", "\"a\"\"\" {clipboard}")] // the C runtime and CommandLineToArgvW disagree after the ""
    [InlineData(@"C:\t\tool.bat", "\\\" {file}")] // cmd.exe opens a span at \" and the value's quote closes it
    [InlineData(@"C:\t\tool.bat", "x^{file}")] // cmd.exe takes the value's opening quote as a character
    [InlineData(@"C:\t\tool.bat", "\"D:\\Backup\\\" {file}")] // review 17 G5, kept: a program the batch file hands %* to reads \" as a quote character
    public void A_placeholder_whose_quotes_a_reader_could_lose_is_refused(string location, string template)
    {
        Assert.Equal(PlacementProblem.InsideQuotes, Placement(location, template));
    }

    [Theory]
    [InlineData("wsl.exe", "grep {clipboard} 'notes.txt'")] // closed before it
    [InlineData("wsl.exe", "git commit -m \"{clipboard}\"")]
    [InlineData("msedge.exe", "\"say \"\"hi\"\"\" {clipboard}")] // both readers leave the span before it
    [InlineData(@"C:\t\tool.bat", "\"D:\\Backup\" {file}")]
    public void A_placeholder_every_reader_sees_alike_is_fine(string location, string template)
    {
        Assert.Equal(PlacementProblem.None, Placement(location, template));
    }

    [Theory]
    [InlineData("wt.exe", "cmd /c type {file}")] // review 17 P8: cmd gets the value bare when it has no space
    [InlineData("wt.exe", @"C:\proj\build.cmd {ask:Target}")]
    [InlineData("conhost.exe", "wsl cat {file}")] // a;calc.exe;.txt
    [InlineData(@"C:\Windows\System32\sudo.exe", "cmd /c type {file}")]
    [InlineData("gsudo.exe", "{file}")] // gsudo runs a file that is no program as %COMSPEC% /s /c "…"
    public void After_a_program_that_drops_quotes_a_placeholder_cmd_or_a_shell_reads_is_refused(string location, string template)
    {
        Assert.Equal(PlacementProblem.HandedOnWithoutQuotes, Placement(location, template));
    }

    [Theory]
    [InlineData("wt.exe", "-d {file}")] // review 17 G2: Windows Terminal's own folder, which no cmd.exe reads
    [InlineData("wt.exe", "notepad {file}")]
    [InlineData("wt.exe", "-p Ubuntu wsl --cd {file}")]
    [InlineData("wt.exe", "ssh {ask:Host}")]
    [InlineData(@"C:\Windows\System32\sudo.exe", "notepad {file}")]
    public void After_a_program_that_drops_quotes_a_placeholder_its_program_reads_is_fine(string location, string template)
    {
        Assert.Equal(PlacementProblem.None, Placement(location, template));
    }

    [Theory]
    [InlineData("/d/c {file}", 5)] // review 17 P12: switches written together
    [InlineData("/q/c {file}", 5)]
    [InlineData("/V:ON/C echo {file}", 8)]
    [InlineData("/d /s /k {file}", 9)]
    [InlineData("/cdir {file}", 2)]
    [InlineData("/q {file}", -1)]
    [InlineData("{clipboard} /c dir", -1)]
    public void Cmds_command_starts_after_its_first_c_k_or_r(string arguments, int start)
    {
        Assert.Equal(start, CmdCommandLine.CommandStart("cmd.exe", arguments));
    }

    [Theory]
    [InlineData("{clipboard}")] // cmd looks for /c inside the value
    [InlineData("/q {ask:Switches} /c dir")]
    public void A_value_among_cmds_own_switches_is_refused(string template)
    {
        Assert.Equal(PlacementProblem.CmdSwitches, Placement("cmd.exe", template));
        Assert.Equal(PlacementProblem.None, Placement("cmd.exe", "/d/c echo {clipboard}"));
        Assert.Equal(PlacementProblem.None, Placement("cmd.exe", "{file}")); // a path holds no "/"
    }

    [Theory]
    [InlineData("wsl.exe", "-- python3 {clipboard}", true)] // review 17 P11: wsl's "--", and python3 reads -c
    [InlineData("wt.exe", "-- python {clipboard}", true)]
    [InlineData(@"C:\nodejs\npx.cmd", "-- tool {clipboard}", true)]
    [InlineData("dotnet.exe", "run -- {clipboard}", true)] // the app reads what follows dotnet's "--"
    [InlineData("wsl.exe", "-- {clipboard}", false)] // the command itself
    [InlineData("wsl.exe", "python3 -- {clipboard}", false)] // python3's own
    [InlineData(@"C:\nodejs\npx.cmd", "tool -- {clipboard}", false)]
    [InlineData("notepad.exe", "-- {clipboard}", false)]
    public void A_wrappers_double_dash_ends_only_its_own_options(string location, string template, bool takesOption)
    {
        Assert.Equal(takesOption, LaunchPlaceholders.TakesOptionAt(template, template.IndexOf("{clipboard}", StringComparison.Ordinal), location));
    }

    [Theory]
    [InlineData("/user-data-dir=\\\\host\\share", ArgumentReader.Plain, true)] // review 17 P13: Chromium reads /name as --name
    [InlineData("//H:CScript", ArgumentReader.Plain, true)] // Windows Script Host
    [InlineData("/c calc", ArgumentReader.Cmd, true)]
    [InlineData("/home/me/notes.txt", ArgumentReader.PosixShell, false)] // a Linux path
    [InlineData("/var/log/syslog", ArgumentReader.RemoteShell, false)]
    [InlineData("C:/x", ArgumentReader.Plain, false)]
    public void A_value_that_starts_with_a_slash_is_a_switch_except_for_a_shell(string value, ArgumentReader reader, bool isSwitch)
    {
        Assert.Equal(isSwitch, LaunchPlaceholders.TakesSwitch(value, reader));
    }

    [Theory]
    [InlineData("ssh://-x@server2")] // review 17 P16: a part of the name that starts with "-"
    [InlineData("[-x]")]
    [InlineData("server2:-1")]
    public void An_ssh_name_none_of_whose_parts_starts_with_a_dash(string value)
    {
        Assert.Equal("-", LaunchPlaceholders.Refused(value, ArgumentReader.SshDestination));
        Assert.Null(LaunchPlaceholders.Refused("ssh://admin@my-host:2222", ArgumentReader.SshDestination));
    }

    [Theory]
    [InlineData("sftp.exe", "-s {clipboard} server2", ArgumentReader.RemoteShell)] // review 17 P16: sftp's subsystem may be a command
    [InlineData("scp.exe", "server2:{ask:Path} .", ArgumentReader.RemoteShell)] // review 17 G4: the old protocol, Windows 10's default
    [InlineData("scp.exe", "-s server2:{ask:Path} .", ArgumentReader.Plain)] // SFTP asked for
    [InlineData("scp.exe", "-rs server2:{ask:Path} .", ArgumentReader.Plain)]
    [InlineData("scp.exe", "-s -O server2:{ask:Path} .", ArgumentReader.RemoteShell)] // the last one wins
    public void Scp_and_sftp_values_are_read_by_who_reads_them(string location, string template, ArgumentReader reader)
    {
        Assert.Equal(reader, ReaderAt(location, template, template.IndexOf('{', StringComparison.Ordinal)));
    }

    [Fact]
    public void A_name_after_a_relay_that_only_a_share_could_hold_counts_as_a_batch_file()
    {
        // Review 17 G6: an offline share in PATH held up every lookup; it is passed over, and a name found nowhere else
        // may be a batch file there.
        const string template = "build {clipboard}";
        var index = template.IndexOf('{', StringComparison.Ordinal);
        var folder = Directory.CreateTempSubdirectory("dig-path-").FullName;
        try
        {
            Assert.Equal(ArgumentReader.Cmd, LaunchPlaceholders.ReaderAt("wt.exe", template, index, searchPath: @"\\offline\share\bin", pathExtensions: ".EXE;.CMD"));
            Assert.Equal(ArgumentReader.Plain, LaunchPlaceholders.ReaderAt("wt.exe", template, index, searchPath: folder, pathExtensions: ".EXE;.CMD"));

            File.WriteAllText(Path.Combine(folder, "build.exe"), string.Empty);
            var both = string.Join(Path.PathSeparator, folder, @"\\offline\share\bin");
            Assert.Equal(ArgumentReader.Plain, LaunchPlaceholders.ReaderAt("wt.exe", template, index, searchPath: both, pathExtensions: ".EXE;.CMD"));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
