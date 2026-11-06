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
    [InlineData("ssh.exe", "{clipboard}", ArgumentReader.SshDestination)] // the destination: an argument of ssh itself
    [InlineData("ssh.exe", "-l {ask:User} server2 uptime", ArgumentReader.SshDestination)] // a user name, as in the destination
    [InlineData("ssh.exe", "-o ConnectTimeout=5 {ask:Which host} uptime", ArgumentReader.SshDestination)] // a prompt with a space
    public void Ssh_sends_the_words_after_its_destination_to_the_remote_shell(string location, string parameters, ArgumentReader reader)
    {
        Assert.Equal(reader, LaunchPlaceholders.ReaderOf(location, parameters));
    }

    [Theory]
    [InlineData("ssh.exe", "{ask:Host} tail {clipboard}", "{ask:Host}", ArgumentReader.SshDestination)] // the review: both sides
    [InlineData("ssh.exe", "{ask:Host} tail {clipboard}", "{clipboard}", ArgumentReader.RemoteShell)]
    [InlineData("ssh.exe", "-o {clipboard} server2", "{clipboard}", ArgumentReader.SshOption)] // review 15 P2: "ProxyCommand calc" is ssh_config too
    [InlineData("ssh.exe", "-o{clipboard} server2", "{clipboard}", ArgumentReader.SshOption)]
    [InlineData("ssh.exe", "-o \"{clipboard}\" server2", "{clipboard}", ArgumentReader.SshOption)]
    [InlineData("ssh.exe", "-vo {clipboard} server2", "{clipboard}", ArgumentReader.SshOption)]
    [InlineData("ssh.exe", "-o Proxy{clipboard} server2", "{clipboard}", ArgumentReader.SshOption)] // inside the keyword
    [InlineData("ssh.exe", "-o ProxyCommand={clipboard} server2", "{clipboard}", ArgumentReader.SshOption)] // a keyword that runs something
    [InlineData("ssh.exe", "-o \"ProxyCommand {clipboard}\" server2", "{clipboard}", ArgumentReader.SshOption)] // one argument, quoted over two words
    [InlineData("ssh.exe", "-oPKCS11Provider={clipboard} server2", "{clipboard}", ArgumentReader.SshOption)]
    [InlineData("ssh.exe", "-o include={clipboard} server2", "{clipboard}", ArgumentReader.SshOption)]
    [InlineData("ssh.exe", "server2 -o {clipboard} uptime", "{clipboard}", ArgumentReader.SshOption)] // OpenSSH reads options after the host too
    [InlineData("ssh.exe", "server2 -oKnownHostsCommand={clipboard} uptime", "{clipboard}", ArgumentReader.SshOption)]
    [InlineData("ssh.exe", "-v{clipboard} server2", "{clipboard}", ArgumentReader.SshOption)] // among option letters: "oProxyCommand=calc"
    [InlineData("ssh.exe", "server2 -{clipboard}", "{clipboard}", ArgumentReader.SshOption)]
    [InlineData("ssh.exe", "-o ConnectTimeout={clipboard} server2", "{clipboard}", ArgumentReader.RemoteShell)] // the keyword is the template's
    [InlineData("ssh.exe", "-o \"SendEnv {clipboard}\" server2", "{clipboard}", ArgumentReader.RemoteShell)]
    [InlineData("ssh.exe", "-o User={ask:User} server2", "{ask:User}", ArgumentReader.SshDestination)] // as -l
    [InlineData("ssh.exe", "-o ProxyJump={clipboard} server2", "{clipboard}", ArgumentReader.SshDestination)] // as -J
    [InlineData("ssh.exe", "-p{clipboard} server2", "{clipboard}", ArgumentReader.Plain)] // a value attached to -p
    [InlineData("ssh.exe", "server2 -l {ask:User} uptime", "{ask:User}", ArgumentReader.SshDestination)] // -l after the host
    [InlineData("ssh.exe", "server2 -p 22 {clipboard}", "{clipboard}", ArgumentReader.RemoteShell)] // options after the host, then the command
    [InlineData("ssh.exe", "server2 -- -o {clipboard}", "{clipboard}", ArgumentReader.RemoteShell)] // "--" after the host: all command
    [InlineData("ssh.exe", "-- server2 -o {clipboard}", "{clipboard}", ArgumentReader.RemoteShell)] // "--" before the host: no options after it
    [InlineData("putty.exe", "-ssh {ask:Host}", "{ask:Host}", ArgumentReader.SshDestination)] // PuTTY reads plink's command line
    [InlineData("putty.exe", "-load {ask:Session}", "{ask:Session}", ArgumentReader.Plain)]
    [InlineData("ssh.exe", "-J {clipboard} server2", "{clipboard}", ArgumentReader.SshDestination)]
    [InlineData("ssh.exe", "-F {clipboard} server2", "{clipboard}", ArgumentReader.Plain)] // a path (the hint warns)
    [InlineData("ssh.exe", "-- {clipboard} uptime", "{clipboard}", ArgumentReader.SshDestination)]
    [InlineData("wt.exe", "ssh {clipboard}", "{clipboard}", ArgumentReader.SshDestination)]
    [InlineData("plink.exe", "-batch -l {ask:User} server2 tail {clipboard}", "{ask:User}", ArgumentReader.SshDestination)]
    [InlineData("plink.exe", "-batch -l {ask:User} server2 tail {clipboard}", "{clipboard}", ArgumentReader.RemoteShell)]
    [InlineData("plink.exe", "-ssh {clipboard}", "{clipboard}", ArgumentReader.SshDestination)]
    [InlineData("plink.exe", "-proxycmd {clipboard} server2", "{clipboard}", ArgumentReader.RemoteShell)]
    [InlineData("plink.exe", "-load {ask:Session}", "{ask:Session}", ArgumentReader.Plain)]
    [InlineData("plink.exe", "-newer-option {clipboard} server2", "{clipboard}", ArgumentReader.RemoteShell)] // its value or the host: unsure, so strict
    [InlineData("wsl.exe", "-d {ask:Distro} grep {clipboard} notes.txt", "{ask:Distro}", ArgumentReader.Plain)] // wsl's own
    [InlineData("wsl.exe", "-d {ask:Distro} grep {clipboard} notes.txt", "{clipboard}", ArgumentReader.PosixShell)]
    [InlineData("wsl.exe", "--shell-type none grep {clipboard} notes.txt", "{clipboard}", ArgumentReader.Plain)] // no shell
    [InlineData("wsl.exe", "--shell-type login grep {clipboard} notes.txt", "{clipboard}", ArgumentReader.PosixShell)]
    [InlineData("cmd.exe", "/c ssh {ask:Host} uptime", "{ask:Host}", ArgumentReader.SshDestination)] // which refuses what cmd.exe does
    public void Each_placeholder_is_judged_where_it_stands(string location, string parameters, string placeholder, ArgumentReader reader)
    {
        Assert.Equal(reader, LaunchPlaceholders.ReaderAt(location, parameters, parameters.IndexOf(placeholder, StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData("admin@server2", null)]
    [InlineData("[fe80::1]:2222", null)]
    [InlineData("ssh://admin@server2.example.com:2222", null)]
    [InlineData("server2;calc", ";")]
    [InlineData("server2 calc", " ")]
    [InlineData("$(id)@server2", "$")]
    [InlineData("admin`id`@server2", "`")]
    [InlineData("server2%h", "%")]
    [InlineData("服务器", "服")]
    [InlineData("-oProxyCommand=calc", "-")] // review 15: after "--" it is the host, and %h hands it to the proxy program
    [InlineData("server-2", null)]
    public void Ssh_destination_takes_what_user_at_host_and_an_ssh_URI_need(string value, string? refused)
    {
        // CVE-2023-51385: OpenSSH before 9.6 put such names into ProxyCommand's shell through %h and %u.
        Assert.Equal(refused, LaunchPlaceholders.Refused(value, ArgumentReader.SshDestination));
    }

    [Theory]
    [InlineData("ProxyCommand calc")]
    [InlineData("ConnectTimeout=5")] // harmless, but the value would choose the keyword
    [InlineData("x")]
    public void Ssh_settings_take_no_value(string value)
    {
        // Review 15 P2: ssh_config takes a space or "=" after the keyword, so the RemoteShell check let "ProxyCommand calc" through.
        Assert.NotNull(LaunchPlaceholders.Refused(value, ArgumentReader.SshOption));
        Assert.NotNull(LaunchPlaceholders.RefusedInPath(@"C:\Users\me\x.conf", ArgumentReader.SshOption));
    }

    [Theory]
    [InlineData("wt.exe", "ssh {clipboard}", true)]
    [InlineData("wt", "{clipboard}", true)]
    [InlineData(@"%LocalAppData%\Microsoft\WindowsApps\wt.exe", "-p Ubuntu {clipboard}", true)]
    [InlineData("conhost.exe", "wt.exe cmd /k echo {clipboard}", true)]
    [InlineData("ssh.exe", "{clipboard}", false)]
    [InlineData("notepad.exe", "wt.txt {clipboard}", false)]
    public void Windows_Terminal_is_found_where_it_reads_the_command_line(string location, string parameters, bool splits)
    {
        Assert.Equal(splits, LaunchPlaceholders.SplitsAtSemicolons(location, parameters));
    }

    [Theory]
    [InlineData("WindowsTerminal.exe", "ssh {clipboard}", true, "Windows Terminal")] // review 16 P5: the packaged name too
    [InlineData("conhost.exe", "cmd /c type {file}", false, "conhost.exe")]
    [InlineData("cmd.exe", "/c wt cmd /c type {file}", true, "Windows Terminal")]
    [InlineData("notepad.exe", "{file}", false, null)]
    public void Windows_Terminal_and_conhost_hand_arguments_on_without_their_quotes(string location, string parameters, bool splits, string? dropsQuotes)
    {
        Assert.Equal(splits, LaunchPlaceholders.SplitsAtSemicolons(location, parameters));
        Assert.Equal(dropsQuotes, LaunchPlaceholders.DropsQuotes(location, parameters));
        Assert.Equal("&", LaunchPlaceholders.RefusedWithoutQuotes(@"C:\t\a&calc&.txt"));
        Assert.Equal("(", LaunchPlaceholders.RefusedWithoutQuotes(@"C:\t\Report (2).txt"));
        Assert.Null(LaunchPlaceholders.RefusedWithoutQuotes(@"C:\t\Report 2.txt"));
    }

    [Fact]
    public void A_batch_file_a_relay_starts_is_read_by_cmd()
    {
        // Review 16 P5: wt.exe C:\proj\build.cmd {ask:Target} counted as a plain program, so x" & calc & " passed.
        var folder = Directory.CreateTempSubdirectory("dig-relay-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(folder, "build.cmd"), "@echo %*");
            ArgumentReader At(string location, string parameters) =>
                LaunchPlaceholders.ReaderAt(location, parameters, parameters.IndexOf("{ask:", StringComparison.Ordinal), searchPath: folder, pathExtensions: ".EXE;.CMD", systemDirectory: string.Empty);

            Assert.Equal(ArgumentReader.Cmd, At("wt.exe", @"C:\proj\build.cmd {ask:Target}"));
            Assert.Equal(ArgumentReader.Cmd, At("wt.exe", "-p Dev build {ask:Target}")); // found on PATH as a batch file
            Assert.Equal(ArgumentReader.Cmd, At("runas.exe", "/user:admin build {ask:Target}"));
            Assert.Equal(ArgumentReader.Plain, At("wt.exe", "-p Dev notepad {ask:Target}")); // nothing of the kind on PATH
            Assert.Equal(ArgumentReader.Plain, At("notepad.exe", "build {ask:Target}")); // no relay: build is notepad's argument
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
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

    [Theory]
    [InlineData("{files}", null)]
    [InlineData("\"{file}\"", null)]
    [InlineData("-multiInst {files}", "-multiInst")]
    [InlineData("--open {FILE} --wait", "--open --wait")]
    [InlineData("-n", "-n")]
    [InlineData(null, null)]
    public void A_click_brings_no_files_so_file_placeholders_are_taken_out(string? parameters, string? forClick)
    {
        // The review: a click on a {files} shortcut started notepad++.exe {files}.
        Assert.Equal(forClick, DropArguments.WithoutFilePlaceholders(parameters));
    }

    [Fact]
    public void File_placeholders_do_not_make_a_program_start_again_instead_of_switching()
    {
        Assert.True(LaunchPlanner.SwitchesByDefault(@"C:\Tools\notepad++.exe", "{files}"));
        Assert.True(LaunchPlanner.SwitchesByDefault(@"C:\Tools\notepad++.exe", "\"{file}\""));
        Assert.False(LaunchPlanner.SwitchesByDefault(@"C:\Tools\notepad++.exe", "-multiInst {files}"));
    }
}
