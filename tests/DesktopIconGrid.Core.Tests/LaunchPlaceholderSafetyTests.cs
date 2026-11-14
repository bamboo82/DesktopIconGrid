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
    [InlineData("ssh.exe", "-F {clipboard} server2", "{clipboard}", ArgumentReader.SshOption)] // review 16 P2′ c: a whole config file, ProxyCommand and all
    [InlineData("ssh.exe", "-- {clipboard} uptime", "{clipboard}", ArgumentReader.SshDestination)]
    [InlineData("wt.exe", "ssh {clipboard}", "{clipboard}", ArgumentReader.SshDestination)]
    [InlineData("plink.exe", "-batch -l {ask:User} server2 tail {clipboard}", "{ask:User}", ArgumentReader.SshDestination)]
    [InlineData("plink.exe", "-batch -l {ask:User} server2 tail {clipboard}", "{clipboard}", ArgumentReader.RemoteShell)]
    [InlineData("plink.exe", "-ssh {clipboard}", "{clipboard}", ArgumentReader.SshDestination)]
    [InlineData("plink.exe", "-proxycmd {clipboard} server2", "{clipboard}", ArgumentReader.SshOption)] // runs here, as ssh's ProxyCommand
    [InlineData("plink.exe", "-m {clipboard} server2", "{clipboard}", ArgumentReader.SshOption)] // its text goes to the server as the command
    [InlineData("ssh.exe", "-I {clipboard} server2", "{clipboard}", ArgumentReader.SshOption)] // a PKCS#11 library to load
    [InlineData("ssh.exe", "-o SmartcardDevice={clipboard} server2", "{clipboard}", ArgumentReader.SshOption)] // -I's old name
    [InlineData("ssh.exe", "-o Proxy\"Command\"={clipboard} server2", "{clipboard}", ArgumentReader.SshOption)] // ssh takes the quotes off
    [InlineData("putty.exe", "-ssh {ask:Host} -pw {ask:Password}", "{ask:Password}", ArgumentReader.Plain)] // review 16 P6: options after the host
    [InlineData("putty.exe", "-ssh {ask:Host} -l {ask:User}", "{ask:User}", ArgumentReader.SshDestination)]
    [InlineData("putty.exe", "-ssh server2 {ask:Port}", "{ask:Port}", ArgumentReader.Plain)] // a bare word after the host: the port
    [InlineData("scp.exe", "-S {clipboard} a.txt server2:/tmp/", "{clipboard}", ArgumentReader.SshOption)] // a program run instead of ssh
    [InlineData("scp.exe", "-F {clipboard} a.txt server2:/tmp/", "{clipboard}", ArgumentReader.SshOption)]
    [InlineData("scp.exe", "-o {clipboard} a.txt server2:/tmp/", "{clipboard}", ArgumentReader.SshOption)]
    [InlineData("scp.exe", "{file} server2:/tmp/", "{file}", ArgumentReader.Plain)] // a local path; the colon rule comes on top (JudgeAt)
    [InlineData("scp.exe", "C:\\out\\{file} server2:/tmp/", "{file}", ArgumentReader.Plain)] // a drive letter is no host
    [InlineData("scp.exe", "\"{clipboard}\" server2:/tmp/", "{clipboard}", ArgumentReader.Plain)]
    [InlineData("scp.exe", "server2:/tmp/a.txt {ask:Where}", "{ask:Where}", ArgumentReader.Plain)]
    [InlineData("scp.exe", "scp://{ask:Host}/tmp/a.txt .", "{ask:Host}", ArgumentReader.SshDestination)] // review 17 P15: a URI's authority
    [InlineData("scp.exe", "scp://{ask:User}@server2:2222/tmp/a.txt .", "{ask:User}", ArgumentReader.SshDestinationPart)] // review 27 C1: part of it
    [InlineData("scp.exe", "scp://server2/{clipboard} .", "{clipboard}", ArgumentReader.RemoteShell)]
    [InlineData("scp.exe", "-s scp://server2/{clipboard} .", "{clipboard}", ArgumentReader.Plain)]
    [InlineData("scp.exe", "SCP://{clipboard} .", "{clipboard}", ArgumentReader.SshDestination)]
    [InlineData("sftp.exe", "sftp://{ask:Host}/pub", "{ask:Host}", ArgumentReader.SshDestination)]
    [InlineData("sftp.exe", "sftp://server2/{ask:Folder}", "{ask:Folder}", ArgumentReader.Plain)]
    [InlineData("scp.exe", "{ask:Host}:/var/log/syslog .", "{ask:Host}", ArgumentReader.SshDestination)]
    [InlineData("scp.exe", "server2:{clipboard} .", "{clipboard}", ArgumentReader.RemoteShell)] // the old protocol hands it to the remote shell
    [InlineData("sftp.exe", "-b {clipboard} server2", "{clipboard}", ArgumentReader.SshOption)] // "!" lines run here
    [InlineData("sftp.exe", "-D {clipboard}", "{clipboard}", ArgumentReader.SshOption)] // a local sftp-server, run here
    [InlineData("sftp.exe", "{ask:Host}", "{ask:Host}", ArgumentReader.SshDestination)]
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
    [InlineData("admin@-x", "-")] // review 16 P2′ e: the host after "@"
    [InlineData("server-2", null)]
    public void Ssh_destination_takes_what_user_at_host_and_an_ssh_URI_need(string value, string? refused)
    {
        // CVE-2023-51385: OpenSSH before 9.6 put such names into ProxyCommand's shell through %h and %u.
        Assert.Equal(refused, LaunchPlaceholders.Refused(value, ArgumentReader.SshDestination));
    }

    [Theory]
    [InlineData("ssh.exe {ask:Host -- e.g. admin@server2} {clipboard}", true)] // review 16 P2′ d: the "--" is the prompt's
    [InlineData("wsl.exe -- ssh server2 {clipboard}", true)] // wsl's "--", not ssh's
    [InlineData("ssh.exe server2 -- {clipboard}", false)]
    [InlineData("notepad.exe -- {clipboard}", false)]
    [InlineData("notepad.exe --name={clipboard}", false)] // part of a word
    public void Only_a_dash_dash_the_reading_program_sees_ends_its_options(string template, bool takesOption)
    {
        Assert.Equal(takesOption, LaunchPlaceholders.TakesOptionAt(template, template.IndexOf("{clipboard}", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData("ProxyCommand calc")]
    [InlineData("ConnectTimeout=5")] // harmless, but the value would choose the keyword
    [InlineData("x")]
    public void Ssh_settings_take_no_value(string value)
    {
        // Review 15 P2: ssh_config takes a space or "=" after the keyword, so the RemoteShell check let "ProxyCommand calc" through.
        Assert.NotNull(LaunchPlaceholders.Refused(value, ArgumentReader.SshOption));
        Assert.NotNull(LaunchPlaceholders.Refused(string.Empty, ArgumentReader.SshOption)); // review 16: the empty one too
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
    [InlineData(@"C:\Windows\System32\sudo.exe", "cmd /c type {file}", false, "sudo")] // review 17 P8: sudo and gsudo too
    [InlineData("powershell.exe", "-Command gsudo Get-Content {file}", false, "gsudo")]
    [InlineData("notepad.exe", "{file}", false, null)]
    public void Windows_Terminal_conhost_and_sudo_hand_arguments_on_without_their_quotes(string location, string parameters, bool splits, string? dropsQuotes)
    {
        Assert.Equal(splits, LaunchPlaceholders.SplitsAtSemicolons(location, parameters));
        Assert.Equal(dropsQuotes, LaunchPlaceholders.DropsQuotes(location, parameters));
    }

    [Theory]
    [InlineData("file:///C:/x\" --upload-pack=calc \"y", "\"")] // review 17 P8: wt re-quotes at the space, and escapes nothing
    [InlineData("x\t--upload-pack=calc", "\t")] // a tab, which it does not quote
    [InlineData(@"C:\My Files\", @"\")] // quoted for its space, the backslash would escape the closing quote
    [InlineData(@"C:\", null)] // no space: not quoted, so the backslash stays one
    [InlineData(@"C:\Program Files (x86)\R&D", null)] // review 17 G2: only cmd.exe or a shell would act on these
    public void After_a_program_that_re_quotes_at_spaces_only_what_would_split_a_value_is_refused(string value, string? refused)
    {
        Assert.Equal(refused, LaunchPlaceholders.SplitByRelay(value));
    }

    [Fact]
    public void The_second_path_of_files_is_judged_where_it_lands()
    {
        // Review 16: wsl.exe --cd {files} with two folders: the second path starts the shell's command.
        var template = LaunchPlaceholders.SplitFiles("--cd {files}");
        var positions = LaunchPlaceholders.FilePositions(template);

        Assert.Equal("--cd {file} {file}", template);
        Assert.Equal([5, 12], positions);
        Assert.Equal(ArgumentReader.Plain, LaunchPlaceholders.ReaderAt("wsl.exe", template, positions[0]));
        Assert.Equal(ArgumentReader.PosixShell, LaunchPlaceholders.ReaderAt("wsl.exe", template, positions[1]));
    }

    [Theory]
    [InlineData("{files} server2:/tmp/")] // review 17 G1: the second path was judged at the start of the host's word
    [InlineData("-r {files} server2:dir")]
    [InlineData("\"{files}\" server2:/tmp/")]
    public void Both_paths_of_files_are_local_paths_for_scp(string parameters)
    {
        var template = LaunchPlaceholders.SplitFiles(parameters);
        var positions = LaunchPlaceholders.FilePositions(template);

        Assert.Equal(2, positions.Count);
        Assert.All(positions, index => Assert.Equal(new PlaceholderJudgement(ArgumentReader.Plain, ScpOperand.WholeWord), LaunchPlaceholders.JudgeAt("scp.exe", template, index)));
        Assert.Null(LaunchPlaceholders.RefusedInPath(@"C:\Users\me\report.txt", ArgumentReader.Plain, ScpOperand.WholeWord));
        Assert.Null(LaunchPlaceholders.RefusedInPath(@"\\nas\share\report.txt", ArgumentReader.Plain, ScpOperand.WholeWord));
    }

    [Theory]
    [InlineData("evil.example:/etc/passwd")] // review 17 P15: the value would turn a local operand into host:path
    [InlineData("scp://evil.example/x")]
    [InlineData("me@evil.example:")]
    [InlineData("ab:c")]
    [InlineData(":x")]
    [InlineData(@"C:\out\a:b")]
    public void A_value_that_scp_would_read_as_another_computer_is_refused(string value)
    {
        Assert.Equal(":", LaunchPlaceholders.Refused(value, ArgumentReader.Plain, ScpOperand.WholeWord));
        Assert.Equal(":", LaunchPlaceholders.Refused(value, ArgumentReader.Plain, ScpOperand.PartOfWord));
    }

    [Fact]
    public void A_drive_letter_passes_only_where_the_value_starts_the_word()
    {
        Assert.Null(LaunchPlaceholders.Refused(@"C:\out\a.txt", ArgumentReader.Plain, ScpOperand.WholeWord));
        Assert.Null(LaunchPlaceholders.Refused(@"out\a.txt", ArgumentReader.Plain, ScpOperand.PartOfWord));
        Assert.Equal(":", LaunchPlaceholders.Refused(@"s:tmp", ArgumentReader.Plain, ScpOperand.PartOfWord)); // "ho{x}" filled to "hos:tmp"
        Assert.Equal("\n", LaunchPlaceholders.Refused("a\nb", ArgumentReader.Plain, ScpOperand.WholeWord)); // what every program refuses, too
        Assert.Null(LaunchPlaceholders.Refused("evil.example:/x", ArgumentReader.Plain)); // without the flag no colon rule
    }

    /// <summary>
    /// Review 27 B2: scp's colon rule ranked as a reader of its own replaced the shell's reader wherever scp came before
    /// the placeholder. Now the shell's rule stays and the colon rule comes on top.
    /// </summary>
    [Theory]
    [InlineData("wsl.exe", "scp {clipboard} h:/tmp/", "$(id)", "$", ArgumentReader.PosixShell)]
    [InlineData("cmd.exe", "/c scp {clipboard} h:/tmp/", "a&calc", "&", ArgumentReader.Cmd)]
    [InlineData("ssh.exe", "s scp {clipboard} b:/x", "x;id", ";", ArgumentReader.RemoteShell)]
    public void Scp_after_a_shell_keeps_the_shells_rule_and_adds_its_colon_rule(string location, string parameters, string value, string refused, ArgumentReader reader)
    {
        var judgement = LaunchPlaceholders.JudgeAt(location, parameters, parameters.IndexOf("{clipboard}", StringComparison.Ordinal));

        Assert.Equal(new PlaceholderJudgement(reader, ScpOperand.WholeWord), judgement);
        Assert.Equal(reader, LaunchPlaceholders.ReaderAt(location, parameters, parameters.IndexOf("{clipboard}", StringComparison.Ordinal)));
        Assert.Equal(refused, LaunchPlaceholders.Refused(value, judgement.Reader, judgement.Scp));
        Assert.Equal(":", LaunchPlaceholders.Refused("evil.example:/etc/passwd", judgement.Reader, judgement.Scp)); // and still no host:path
    }

    [Fact]
    public void A_dropped_file_through_wsl_and_scp_keeps_the_shells_rule()
    {
        const string Parameters = "scp {file} h:/tmp/";
        var judgement = LaunchPlaceholders.JudgeAt("wsl.exe", Parameters, Parameters.IndexOf("{file}", StringComparison.Ordinal));

        Assert.Equal(new PlaceholderJudgement(ArgumentReader.PosixShell, ScpOperand.WholeWord), judgement);
        Assert.Equal("$", LaunchPlaceholders.RefusedInPath(@"C:\Users\me\$(id).txt", judgement.Reader, judgement.Scp));
        Assert.Null(LaunchPlaceholders.RefusedInPath(@"C:\Users\me\report.txt", judgement.Reader, judgement.Scp));
    }

    /// <summary>Review 27 C1: a value beside a host the template writes could change the host.</summary>
    [Theory]
    [InlineData("scp.exe", "a.txt {ask:User}@server2:/tmp/", "{ask:User}", "evil.example:", ":")]
    [InlineData("scp.exe", "scp://{ask:User}@server2:2222/x .", "{ask:User}", "evil.example/", "/")]
    [InlineData("scp.exe", "scp://server2:{ask:Port}/x .", "{ask:Port}", "22@evil.example", "@")]
    [InlineData("ssh.exe", "{ask:User}@server2 uptime", "{ask:User}", "a@evil.example", "@")]
    [InlineData("ssh.exe", "ssh://admin@{ask:Host}:22", "{ask:Host}", "evil.example:2222", ":")]
    public void A_value_inside_a_destination_the_template_writes_more_of_can_only_complete_a_name(string location, string parameters, string placeholder, string value, string refused)
    {
        var judgement = LaunchPlaceholders.JudgeAt(location, parameters, parameters.IndexOf(placeholder, StringComparison.Ordinal));

        Assert.Equal(ArgumentReader.SshDestinationPart, judgement.Reader);
        Assert.Equal(refused, LaunchPlaceholders.Refused(value, judgement.Reader, judgement.Scp));
        Assert.Null(LaunchPlaceholders.Refused("deploy-2.example", judgement.Reader, judgement.Scp)); // a name passes
        Assert.Equal("-", LaunchPlaceholders.Refused("-oProxyCommand", judgement.Reader, judgement.Scp)); // never an option
    }

    /// <summary>The review's note 1 on WP2: where the value ends a host name the template begins, a dot would move the host.</summary>
    [Theory]
    [InlineData("scp.exe", "scp://server2{ask:Suffix} .", "{ask:Suffix}")]
    [InlineData("scp.exe", "a.txt user@host{ask:N}:/p", "{ask:N}")]
    [InlineData("scp.exe", "a.txt host{ask:N}:/p", "{ask:N}")]
    [InlineData("ssh.exe", "srv.{ask:Zone} uptime", "{ask:Zone}")] // "evil.example" would make srv.evil.example
    [InlineData("ssh.exe", "ssh://admin@db{ask:N}:22", "{ask:N}")]
    public void A_value_that_ends_a_host_name_the_template_begins_may_not_hold_a_dot(string location, string parameters, string placeholder)
    {
        var judgement = LaunchPlaceholders.JudgeAt(location, parameters, parameters.IndexOf(placeholder, StringComparison.Ordinal));

        Assert.Equal(ArgumentReader.SshHostEnd, judgement.Reader);
        Assert.Equal(".", LaunchPlaceholders.Refused(".evil.example", judgement.Reader, judgement.Scp));
        Assert.Equal("/", LaunchPlaceholders.Refused("2/x", judgement.Reader, judgement.Scp)); // what a part refuses, too
        Assert.Equal("-", LaunchPlaceholders.Refused("-x", judgement.Reader, judgement.Scp));
        Assert.Null(LaunchPlaceholders.Refused("02-b_c", judgement.Reader, judgement.Scp));
    }

    [Theory]
    [InlineData("scp.exe", "a.txt {ask:Sub}.example.com:/p", "{ask:Sub}")] // the template ends the host: a subdomain at most
    [InlineData("scp.exe", "a.txt srv{ask:N}.example.com:/p", "{ask:N}")] // inside the host
    [InlineData("scp.exe", "a.txt {ask:User}.ops@host:/p", "{ask:User}")] // the user part
    [InlineData("ssh.exe", "admin@{ask:Host} uptime", "{ask:Host}")] // the whole host, after the user the template writes
    [InlineData("scp.exe", "scp://host:{ask:Port}/x .", "{ask:Port}")] // the port
    public void Elsewhere_in_a_destination_a_value_keeps_its_dots(string location, string parameters, string placeholder)
    {
        var judgement = LaunchPlaceholders.JudgeAt(location, parameters, parameters.IndexOf(placeholder, StringComparison.Ordinal));

        Assert.Equal(ArgumentReader.SshDestinationPart, judgement.Reader);
        Assert.Null(LaunchPlaceholders.Refused("a.b", judgement.Reader, judgement.Scp));
    }

    [Theory]
    [InlineData("ssh.exe", "{ask:Target} uptime", "{ask:Target}")] // the whole destination may still be user@host
    [InlineData("ssh.exe", "ssh://{ask:Target}/", "{ask:Target}")]
    [InlineData("scp.exe", "{ask:Target}:/tmp/x .", "{ask:Target}")]
    [InlineData("sftp.exe", "{ask:Target}", "{ask:Target}")]
    public void A_placeholder_that_is_the_whole_destination_keeps_the_destination_reader(string location, string parameters, string placeholder)
    {
        var reader = LaunchPlaceholders.ReaderAt(location, parameters, parameters.IndexOf(placeholder, StringComparison.Ordinal));

        Assert.Equal(ArgumentReader.SshDestination, reader);
        Assert.Null(LaunchPlaceholders.Refused("admin@server2", reader));
    }

    /// <summary>Review 27 C2: with no operand naming another computer, Windows' scp copies here through cmd.exe.</summary>
    [Theory]
    [InlineData("scp.exe", "{clipboard} D:\\backup\\", "{clipboard}")]
    [InlineData("scp.exe", "C:\\in\\a.txt {ask:Where}", "{ask:Where}")]
    [InlineData("scp.exe", "-r {file} {ask:Where}", "{file}")]
    public void An_scp_line_that_names_no_other_computer_is_read_as_cmd_reads_it(string location, string parameters, string placeholder)
    {
        var judgement = LaunchPlaceholders.JudgeAt(location, parameters, parameters.IndexOf(placeholder, StringComparison.Ordinal));

        Assert.Equal(new PlaceholderJudgement(ArgumentReader.Cmd, ScpOperand.WholeWord), judgement);
        Assert.Equal("&", LaunchPlaceholders.Refused("a&calc", judgement.Reader, judgement.Scp));
        Assert.Equal(":", LaunchPlaceholders.Refused("server2:/tmp/", judgement.Reader, judgement.Scp));
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
        Assert.Null(LaunchPlaceholders.RefusedInPath(@"C:\In\Tom & Jerry (1).mp4", ArgumentReader.PosixShell));
        Assert.Equal("!", LaunchPlaceholders.RefusedInPath(@"C:\In\Tom & Jerry (1)!.mp4", ArgumentReader.PosixShell)); // review 18 D3: delayed expansion
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
        Assert.Null(LaunchPlaceholders.RefusedInPath(@"C:\In\Tom & Jerry (1).mp4", ArgumentReader.Cmd)); // literal inside quotes
        Assert.Equal("!", LaunchPlaceholders.RefusedInPath(@"C:\In\Tom & Jerry (1)!.mp4", ArgumentReader.Cmd)); // review 18 D3: delayed expansion
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
