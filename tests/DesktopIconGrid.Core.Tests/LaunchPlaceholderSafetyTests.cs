using DesktopIconGrid.Core.Launching;

namespace DesktopIconGrid.Core.Tests;

/// <summary>{clipboard} and {ask:} never reach cmd.exe or PowerShell script text with something they would run.</summary>
public sealed class LaunchPlaceholderSafetyTests : IDisposable
{
    private const string PathExtensions = ".COM;.EXE;.BAT;.CMD";

    private readonly string _directory = Directory.CreateTempSubdirectory("dig-reader-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

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
    [InlineData(@"C:\Links\Tool.lnk", "{clipboard}", ArgumentReader.Cmd)] // its target is not known here
    public void The_reader_of_the_command_line_is_found_from_the_program_and_its_switches(string location, string parameters, ArgumentReader reader)
    {
        Assert.Equal(reader, LaunchPlaceholders.ReaderOf(location, parameters));
    }

    [Theory]
    [InlineData("wt.exe", "cmd /c {clipboard}", ArgumentReader.Cmd)]
    [InlineData("wt.exe", @"C:\Windows\System32\cmd.exe /k type {clipboard}", ArgumentReader.Cmd)]
    [InlineData("conhost.exe", "powershell -NoProfile -Command Write-Output {clipboard}", ArgumentReader.PowerShellCommand)]
    [InlineData("runas.exe", "/user:admin \"cmd /c {clipboard}\"", ArgumentReader.Cmd)]
    [InlineData("wt.exe", "pwsh -File tool.ps1 {clipboard}", ArgumentReader.Plain)]
    [InlineData("wt.exe", "-p Ubuntu {clipboard}", ArgumentReader.Plain)]
    public void A_program_that_hands_its_command_line_to_cmd_or_PowerShell_is_read_as_they_read_it(string location, string parameters, ArgumentReader reader)
    {
        Assert.Equal(reader, LaunchPlaceholders.ReaderOf(location, parameters));
    }

    [Theory]
    [InlineData("wsl.exe", "grep {clipboard} notes.txt", ArgumentReader.PosixShell)]
    [InlineData("wsl", "{clipboard}", ArgumentReader.PosixShell)]
    [InlineData(@"C:\Windows\System32\wsl.exe", "-d Ubuntu -u root -- echo {clipboard}", ArgumentReader.PosixShell)]
    [InlineData("wsl.exe", "grep -e {clipboard} notes.txt", ArgumentReader.PosixShell)] // grep's -e: the command had started
    [InlineData("wsl.exe", "-e grep {clipboard} notes.txt", ArgumentReader.Plain)] // runs grep itself, no shell
    [InlineData("wsl.exe", "--distribution Ubuntu --exec grep {clipboard} notes.txt", ArgumentReader.Plain)]
    [InlineData("wt.exe", "wsl grep {clipboard} notes.txt", ArgumentReader.PosixShell)]
    [InlineData("wt.exe", "-p Ubuntu wsl.exe -e grep {clipboard} notes.txt", ArgumentReader.Plain)]
    [InlineData("cmd.exe", "/c wsl echo {clipboard}", ArgumentReader.PosixShell)] // which refuses what cmd.exe refuses too
    public void WSL_hands_the_command_line_to_a_Linux_shell_unless_it_runs_the_program_itself(string location, string parameters, ArgumentReader reader)
    {
        Assert.Equal(reader, LaunchPlaceholders.ReaderOf(location, parameters, systemDirectory: _directory));
    }

    [Fact]
    public void System32s_bash_is_WSL_and_another_bash_is_a_program()
    {
        var system = Directory.CreateDirectory(Path.Combine(_directory, "System32")).FullName;
        var git = Directory.CreateDirectory(Path.Combine(_directory, "Git")).FullName;
        File.WriteAllBytes(Path.Combine(git, "bash.exe"), []);
        ArgumentReader Reader(string location, string parameters) =>
            LaunchPlaceholders.ReaderOf(location, parameters, searchPath: git, pathExtensions: PathExtensions, systemDirectory: system);

        Assert.Equal(ArgumentReader.Plain, Reader("bash", "script.sh {clipboard}")); // Git Bash, found on PATH
        Assert.Equal(ArgumentReader.Plain, Reader("bash.exe", "script.sh {clipboard}"));

        File.WriteAllBytes(Path.Combine(system, "bash.exe"), []); // WSL's: Windows looks in System32 before PATH
        Assert.Equal(ArgumentReader.PosixShell, Reader("bash", "script.sh {clipboard}"));
        Assert.Equal(ArgumentReader.PosixShell, Reader("bash.exe", "-c ls {clipboard}"));
        Assert.Equal(ArgumentReader.PosixShell, Reader(Path.Combine(system, "bash.exe"), "{clipboard}"));
        Assert.Equal(ArgumentReader.PosixShell, Reader("wt.exe", "bash {clipboard}"));
        Assert.Equal(ArgumentReader.Plain, Reader(Path.Combine(git, "bash.exe"), "script.sh {clipboard}")); // named by its own path
    }

    [Theory]
    [InlineData("ssh.exe", "server2 cat {clipboard}", ArgumentReader.RemoteShell)]
    [InlineData("ssh.exe", "-p 2222 -i key admin@server2 tail -n 50 {ask:Log file}", ArgumentReader.RemoteShell)]
    [InlineData("ssh.exe", "-p2222 -vv server2 cat {clipboard}", ArgumentReader.RemoteShell)]
    [InlineData("ssh.exe", "-- server2 cat {file}", ArgumentReader.RemoteShell)]
    [InlineData("ssh.exe", "server2 cat {files}", ArgumentReader.RemoteShell)]
    [InlineData("wt.exe", "ssh server2 cat {clipboard}", ArgumentReader.RemoteShell)]
    [InlineData("ssh.exe", "{clipboard}", ArgumentReader.Plain)] // the destination: an argument of ssh itself
    [InlineData("ssh.exe", "-l {ask:User} server2 uptime", ArgumentReader.Plain)]
    [InlineData("ssh.exe", "-o ConnectTimeout=5 {ask:Which host} uptime", ArgumentReader.Plain)] // a prompt with a space
    public void Ssh_sends_the_words_after_its_destination_to_the_remote_shell(string location, string parameters, ArgumentReader reader)
    {
        Assert.Equal(reader, LaunchPlaceholders.ReaderOf(location, parameters));
    }

    [Fact]
    public void A_name_without_an_extension_is_read_by_cmd_when_the_shell_finds_a_batch_file_for_it_or_nothing()
    {
        // As on the owner's PC: npm.cmd next to an extensionless sh script called npm. The shell runs npm.cmd.
        File.WriteAllText(Path.Combine(_directory, "npm.cmd"), "@echo %*");
        File.WriteAllText(Path.Combine(_directory, "npm"), "#!/bin/sh");
        File.WriteAllBytes(Path.Combine(_directory, "tool.exe"), []);

        Assert.Equal(ArgumentReader.Cmd, LaunchPlaceholders.ReaderOf("npm", "install {clipboard}", searchPath: _directory, pathExtensions: PathExtensions));
        Assert.Equal(ArgumentReader.Cmd, LaunchPlaceholders.ReaderOf(Path.Combine(_directory, "npm"), "{clipboard}", pathExtensions: PathExtensions));
        Assert.Equal(ArgumentReader.Cmd, LaunchPlaceholders.ReaderOf("npm", "{clipboard}", startIn: _directory, searchPath: string.Empty, pathExtensions: PathExtensions));
        Assert.Equal(ArgumentReader.Plain, LaunchPlaceholders.ReaderOf("tool", "{clipboard}", searchPath: _directory, pathExtensions: PathExtensions));
        Assert.Equal(ArgumentReader.Cmd, LaunchPlaceholders.ReaderOf("missing", "{clipboard}", searchPath: _directory, pathExtensions: PathExtensions));

        // App Paths names an .exe, PATH has a batch file of the same name: either may be what runs, so cmd.exe counts.
        File.WriteAllText(Path.Combine(_directory, "code.cmd"), "@echo %*");
        string? AppPaths(string fileName) => fileName.ToLowerInvariant() switch
        {
            "code.exe" => @"C:\VS Code\Code.exe",
            "tool.exe" => @"C:\Tool\tool.exe",
            _ => null,
        };
        Assert.Equal(ArgumentReader.Cmd, LaunchPlaceholders.ReaderOf("code", "{clipboard}", registeredPathOf: AppPaths, searchPath: _directory, pathExtensions: PathExtensions));
        Assert.Equal(ArgumentReader.Plain, LaunchPlaceholders.ReaderOf("tool", "{clipboard}", registeredPathOf: AppPaths, searchPath: _directory, pathExtensions: PathExtensions));
    }

    [Theory]
    [InlineData("x\" & calc & \"", "\"")]
    [InlineData("100%PATH%", "%")]
    [InlineData("hi!there", "!")]
    [InlineData("a^b", "^")]
    [InlineData("a & calc", "&")]
    [InlineData("a | calc", "|")]
    [InlineData("a < b", "<")]
    [InlineData("a > b", ">")]
    [InlineData("one\ntwo", "\n")]
    [InlineData("one\rtwo", "\r")]
    [InlineData("a\uFF02 & calc", "\uFF02")]
    public void Cmd_never_gets_quotes_variables_or_operators(string value, string refused)
    {
        Assert.Equal(refused, LaunchPlaceholders.Refused(value, ArgumentReader.Cmd));
    }

    [Theory]
    [InlineData("$(echo INJECTED)", "$")] // the review: wsl.exe echo "$(echo INJECTED)" printed INJECTED
    [InlineData("`id`", "`")]
    [InlineData("$HOME", "$")]
    [InlineData("x\" & calc & \"", "\"")] // what cmd.exe refuses is refused too
    [InlineData("100%PATH%", "%")]
    public void A_Linux_shell_through_WSL_gets_no_expansion_inside_its_quotes(string value, string refused)
    {
        Assert.Equal(refused, LaunchPlaceholders.Refused(value, ArgumentReader.PosixShell));
    }

    [Fact]
    public void A_Linux_shell_through_WSL_takes_what_stays_inside_the_quotes()
    {
        Assert.Null(LaunchPlaceholders.Refused("a;echo INJECTED2", ArgumentReader.PosixShell)); // the review: printed as written
        Assert.Null(LaunchPlaceholders.Refused("notes (final) 'v2' *.txt -x", ArgumentReader.PosixShell));
        Assert.Equal("$", LaunchPlaceholders.RefusedInPath(@"C:\In\a$(id).txt", ArgumentReader.PosixShell));
        Assert.Equal("`", LaunchPlaceholders.RefusedInPath(@"C:\In\a`id`.txt", ArgumentReader.PosixShell));
        Assert.Equal("%", LaunchPlaceholders.RefusedInPath(@"C:\In\100%PATH%.txt", ArgumentReader.PosixShell));
        Assert.Null(LaunchPlaceholders.RefusedInPath(@"C:\In\Tom & Jerry (1)!.mp4", ArgumentReader.PosixShell));
    }

    [Fact]
    public void The_command_ssh_sends_gets_what_PowerShell_script_text_gets()
    {
        Assert.Equal(";", LaunchPlaceholders.Refused("x; rm -rf ~", ArgumentReader.RemoteShell));
        Assert.Equal("-", LaunchPlaceholders.Refused("x -rf", ArgumentReader.RemoteShell));
        Assert.Equal("@", LaunchPlaceholders.Refused("admin@server2", ArgumentReader.RemoteShell)); // fine as the destination, which is Plain
        Assert.Null(LaunchPlaceholders.Refused("/var/log/syslog.1", ArgumentReader.RemoteShell));
        Assert.Equal(";", LaunchPlaceholders.RefusedInPath(@"C:\t\a;b.txt", ArgumentReader.RemoteShell));
    }

    [Theory]
    [InlineData("(1+1)", "(")] // the review: Write-Output "(1+1)" printed 2, the quotes never reach the script
    [InlineData("x|Measure-Object|Select-Object -Expand Count", "|")]
    [InlineData("a;b", ";")]
    [InlineData("a&b", "&")]
    [InlineData("a\u2019,(1+1),\u2019b", "\u2019")] // a single quote to PowerShell
    [InlineData("\u201Ca\u201D", "\u201C")] // a double quote to PowerShell
    [InlineData("$(calc)", "$")]
    [InlineData("a`b", "`")]
    [InlineData("@{a=1}", "@")]
    [InlineData("{calc}", "{")]
    [InlineData("a,b", ",")]
    [InlineData("\u2013Recurse", "\u2013")] // a dash to PowerShell
    [InlineData("it's", "'")]
    [InlineData("say \"hi\"", "\"")]
    [InlineData("one\ttwo", "\t")]
    [InlineData("\U0001F600", "\U0001F600")] // a surrogate pair is named whole
    public void PowerShell_script_text_gets_letters_digits_spaces_and_path_characters_only(string value, string refused)
    {
        Assert.Equal(refused, LaunchPlaceholders.Refused(value, ArgumentReader.PowerShellCommand));
    }

    [Theory]
    [InlineData("server-01.example.com")]
    [InlineData(@"C:\My Files\a.txt")]
    [InlineData(@"\\nas\share\report_2026.pdf")]
    [InlineData("記事本 小算盤")]
    [InlineData("\U00020000")] // a CJK letter outside the BMP
    [InlineData("user/name:42")]
    public void PowerShell_script_text_takes_words_and_paths(string value)
    {
        Assert.Null(LaunchPlaceholders.Refused(value, ArgumentReader.PowerShellCommand));
    }

    [Theory]
    [InlineData("x -Recurse", "-")] // the review: Remove-Item {clipboard}
    [InlineData(@"x -Destination \\host\share", "-")] // Copy-Item {clipboard} D:\bak
    [InlineData("a  --b", "-")]
    [InlineData("x-Recurse a-b C:-x", null)] // inside a word
    [InlineData("-Recurse", null)] // at the start, where the placeholder stands decides (the option rule)
    public void In_PowerShell_script_text_a_dash_that_starts_a_word_is_a_parameter(string value, string? refused)
    {
        Assert.Equal(refused, LaunchPlaceholders.Refused(value, ArgumentReader.PowerShellCommand));
        Assert.Null(LaunchPlaceholders.Refused(value, ArgumentReader.Plain)); // one argument there, whatever it holds
        Assert.Null(LaunchPlaceholders.Refused(value, ArgumentReader.Cmd));
    }

    [Fact]
    public void Other_programs_get_any_value_but_a_line_break_or_the_fullwidth_quotation_mark()
    {
        Assert.Null(LaunchPlaceholders.Refused(@"C:\Reports\Q3 summary (final).docx", ArgumentReader.Cmd));
        Assert.Null(LaunchPlaceholders.Refused("x\" & calc & \"", ArgumentReader.Plain)); // one quoted argument, whatever it holds
        Assert.Equal("\n", LaunchPlaceholders.Refused("one\ntwo", ArgumentReader.Plain));
        Assert.Equal("\uFF02", LaunchPlaceholders.Refused("a\uFF02 --upload-pack=x", ArgumentReader.Plain)); // a quote after best-fit
    }

    [Fact]
    public void A_dropped_path_is_refused_for_what_works_inside_its_quotes_and_for_PowerShell_as_bare_words()
    {
        Assert.Equal("%", LaunchPlaceholders.RefusedInPath(@"C:\In\100%PATH%.txt", ArgumentReader.Cmd));
        Assert.Null(LaunchPlaceholders.RefusedInPath(@"C:\In\Tom & Jerry (1)!.mp4", ArgumentReader.Cmd)); // literal inside quotes
        Assert.Equal(";", LaunchPlaceholders.RefusedInPath(@"C:\t\a;Write-Output INJECTED;.txt", ArgumentReader.PowerShellCommand)); // the review
        Assert.Equal("$", LaunchPlaceholders.RefusedInPath(@"C:\In\a$(calc).txt", ArgumentReader.PowerShellCommand));
        Assert.Equal("(", LaunchPlaceholders.RefusedInPath(@"C:\In\a (1).txt", ArgumentReader.PowerShellCommand));
        Assert.Null(LaunchPlaceholders.RefusedInPath(@"C:\My Files\report-2026_v2.txt", ArgumentReader.PowerShellCommand));
        Assert.Equal("-", LaunchPlaceholders.RefusedInPath(@"C:\In\a -Force\b.txt", ArgumentReader.PowerShellCommand)); // a folder named "a -Force"
        Assert.Null(LaunchPlaceholders.RefusedInPath(@"C:\In\a$(calc)%x%.txt", ArgumentReader.Plain));
        Assert.Equal("\uFF02", LaunchPlaceholders.RefusedInPath("C:\\In\\a\uFF02b.txt", ArgumentReader.Plain));
        Assert.Equal("\uFF02", LaunchPlaceholders.RefusedInPath("C:\\In\\a\uFF02b.txt", ArgumentReader.Cmd));
    }

    [Theory]
    [InlineData("{clipboard}", true)]
    [InlineData("clone {clipboard} dir", true)]
    [InlineData("\"{clipboard}\"", true)]
    [InlineData("-c {clipboard}", true)]
    [InlineData("-- {clipboard}", false)]
    [InlineData("-x \"--\" {clipboard}", false)]
    [InlineData("--name={clipboard}", false)]
    [InlineData("/v:{clipboard}", false)]
    public void A_value_that_starts_with_a_dash_is_an_option_where_its_placeholder_starts_a_word(string template, bool takesOption)
    {
        Assert.Equal(takesOption, LaunchPlaceholders.TakesOptionAt(template, LaunchPlaceholders.ClipboardPositions(template)[0]));
    }

    [Fact]
    public void Each_question_is_judged_where_it_stands()
    {
        const string template = "{ask:First} -- {ask:Second}";
        var positions = LaunchPlaceholders.AskPositions(template);

        Assert.Equal(2, positions.Count);
        Assert.True(LaunchPlaceholders.TakesOptionAt(template, positions[0]));
        Assert.False(LaunchPlaceholders.TakesOptionAt(template, positions[1]));
    }
}
