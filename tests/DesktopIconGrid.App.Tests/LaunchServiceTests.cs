using System.IO;
using System.Windows.Threading;
using DesktopIconGrid.Core.Launching;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Resources;
using DesktopIconGrid.Services;

namespace DesktopIconGrid.App.Tests;

/// <summary>{clipboard} and {ask:…} are filled in before anything starts; a cancelled question starts nothing.</summary>
public sealed class LaunchServiceTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("dig-launch-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private sealed class Prompts(string? clipboard, string? answer) : ILaunchPrompts
    {
        public List<string> Asked { get; } = [];

        public string? ClipboardText() => clipboard;

        public string? Ask(string title, string prompt)
        {
            Asked.Add(prompt);
            return answer;
        }
    }

    private LaunchService Create(Prompts prompts, out ProcessTracker tracker)
    {
        tracker = new ProcessTracker(Dispatcher.CurrentDispatcher, Path.Combine(_directory, "logs"));
        return new LaunchService(tracker, prompts);
    }

    [Fact]
    public void Cancelling_a_question_cancels_the_launch_before_anything_starts()
    {
        var prompts = new Prompts(clipboard: null, answer: null);
        var service = Create(prompts, out var tracker);
        using (tracker)
        {
            var result = service.Launch(new ShortcutItem { Name = "SSH", Location = Path.Combine(_directory, "missing.exe"), Parameters = "{ask:Host} {ask:Port}" });

            Assert.Equal(LaunchOutcome.Cancelled, result.Outcome);
            Assert.Equal(["Host"], prompts.Asked); // stops at the first cancelled question
        }
    }

    [Fact]
    public void An_empty_clipboard_stops_the_launch_with_a_reason()
    {
        var service = Create(new Prompts(clipboard: null, answer: "x"), out var tracker);
        using (tracker)
        {
            var result = service.Launch(new ShortcutItem { Name = "Search", Location = Path.Combine(_directory, "missing.exe"), Parameters = "{clipboard}" });

            Assert.Equal(LaunchOutcome.Failed, result.Outcome);
            Assert.Equal(Strings.Launch_ClipboardEmpty, result.ErrorMessage);
        }
    }

    [Theory]
    [InlineData("lookup.bat", "{clipboard}", "cmd.exe")]
    [InlineData("cmd.exe", "/c echo {clipboard}", "cmd.exe")]
    public void A_clipboard_value_cmd_could_run_is_refused_before_anything_starts(string program, string parameters, string reader)
    {
        var service = Create(new Prompts(clipboard: "x\" & calc & \"", answer: null), out var tracker);
        using (tracker)
        {
            var location = program.EndsWith(".bat", StringComparison.Ordinal) ? Path.Combine(_directory, program) : program;
            var result = service.Launch(new ShortcutItem { Name = "Lookup", Location = location, Parameters = parameters, CaptureLog = true });

            Assert.Equal(LaunchOutcome.Failed, result.Outcome);
            Assert.Equal(string.Format(Strings.Launch_UnsafeValue, '"', reader), result.ErrorMessage);
        }
    }

    [Fact]
    public void An_answer_PowerShell_would_run_is_refused()
    {
        var service = Create(new Prompts(clipboard: null, answer: "$(Start-Process calc)"), out var tracker);
        using (tracker)
        {
            var result = service.Launch(new ShortcutItem { Name = "Query", Location = "powershell.exe", Parameters = "-NoProfile -Command Get-Item {ask:Path}" });

            Assert.Equal(string.Format(Strings.Launch_UnsafeValue, '$', "PowerShell"), result.ErrorMessage);
        }
    }

    [Fact]
    public void A_word_PowerShell_would_take_as_a_parameter_is_refused()
    {
        var service = Create(new Prompts(clipboard: @"x -Destination \\host\share", answer: null), out var tracker);
        using (tracker)
        {
            var result = service.Launch(new ShortcutItem { Name = "Back up", Location = "powershell.exe", Parameters = @"-NoProfile -Command Copy-Item {clipboard} D:\bak" });

            Assert.Equal(Strings.Launch_WordIsParameter, result.ErrorMessage);
        }
    }

    [Fact]
    public void A_value_the_Linux_shell_would_expand_is_refused_for_WSL()
    {
        var service = Create(new Prompts(clipboard: "$(echo INJECTED)", answer: null), out var tracker);
        using (tracker)
        {
            var result = service.Launch(new ShortcutItem { Name = "Grep", Location = "wsl.exe", Parameters = "grep {clipboard} notes.txt" });

            Assert.Equal(string.Format(Strings.Launch_UnsafeValue, '$', "WSL"), result.ErrorMessage);
        }
    }

    [Fact]
    public void A_semicolon_is_refused_for_Windows_Terminal_in_a_value_and_in_a_dropped_file()
    {
        var service = Create(new Prompts(clipboard: "server2;notepad", answer: null), out var tracker);
        using (tracker)
        {
            var typed = service.Launch(new ShortcutItem { Name = "Open", Location = "wt.exe", Parameters = "notepad {clipboard}" });
            var dropped = service.LaunchWith(new ShortcutItem { Name = "Edit", Location = "wt.exe", Parameters = "notepad {file}" }, [@"C:\t\a;calc.txt"]);
            var host = service.Launch(new ShortcutItem { Name = "Connect", Location = "wt.exe", Parameters = "ssh {clipboard}" });

            var refusal = string.Format(Strings.Launch_UnsafeValue, ';', "Windows Terminal");
            Assert.Equal(refusal, typed.ErrorMessage);
            Assert.Equal(refusal, dropped.ErrorMessage);
            Assert.Equal(string.Format(Strings.Launch_UnsafeValue, ';', "ssh"), host.ErrorMessage); // ssh's destination check comes first there
        }
    }

    [Fact]
    public void What_ssh_sends_to_the_remote_shell_is_checked_as_bare_words_and_dropped_files_where_they_go()
    {
        var service = Create(new Prompts(clipboard: "x; rm -rf ~", answer: "x -rf"), out var tracker);
        using (tracker)
        {
            var typed = service.Launch(new ShortcutItem { Name = "Show", Location = "ssh.exe", Parameters = "server2 cat {clipboard}" });
            var asked = service.Launch(new ShortcutItem { Name = "Show", Location = "ssh.exe", Parameters = "server2 ls {ask:Folder}" });
            var appended = service.LaunchWith(new ShortcutItem { Name = "Show", Location = "ssh.exe", Parameters = "server2 cat" }, [@"C:\t\a;b.txt"]);

            Assert.Equal(string.Format(Strings.Launch_UnsafeValue, ';', "ssh"), typed.ErrorMessage);
            Assert.Equal(Strings.Launch_WordIsRemoteOption, asked.ErrorMessage);
            Assert.Equal(string.Format(Strings.Launch_UnsafeValue, ';', "ssh"), appended.ErrorMessage);
        }
    }

    [Fact]
    public void The_destination_takes_user_at_host_while_the_words_after_it_stay_bare_words()
    {
        var ssh = Path.Combine(_directory, "ssh.exe"); // not started: the refusals come first, and it is not there
        var fine = Create(new Prompts(clipboard: "/var/log/syslog", answer: "admin@server2"), out var tracker);
        using (tracker)
        {
            var result = fine.Launch(new ShortcutItem { Name = "Tail", Location = ssh, Parameters = "{ask:Host} tail {clipboard}" });

            Assert.NotEqual(string.Format(Strings.Launch_UnsafeValue, '@', "ssh"), result.ErrorMessage); // went on, and failed only to start
        }

        var bad = Create(new Prompts(clipboard: "/var/log/syslog", answer: "server2;calc"), out var tracker2);
        using (tracker2)
        {
            var result = bad.Launch(new ShortcutItem { Name = "Tail", Location = ssh, Parameters = "{ask:Host} tail {clipboard}" });

            Assert.Equal(string.Format(Strings.Launch_UnsafeValue, ';', "ssh"), result.ErrorMessage);
        }
    }

    [Fact]
    public void A_start_in_folder_that_is_not_on_this_PC_gives_way_to_the_programs_own_folder()
    {
        var program = Path.Combine(_directory, "tool.exe");
        var gone = new ShortcutItem { Name = "Tool", Location = program, StartIn = Path.Combine(_directory, "project-on-another-PC") };
        var startInfo = new System.Diagnostics.ProcessStartInfo(program) { WorkingDirectory = gone.StartIn };

        LaunchService.UseProgramFolderWhenStartInIsGone(gone, startInfo);

        Assert.Equal(_directory, startInfo.WorkingDirectory);

        var there = new ShortcutItem { Name = "Tool", Location = program, StartIn = Path.Combine(_directory, "logs") };
        Directory.CreateDirectory(there.StartIn);
        var kept = new System.Diagnostics.ProcessStartInfo(program) { WorkingDirectory = there.StartIn };
        LaunchService.UseProgramFolderWhenStartInIsGone(there, kept);
        Assert.Equal(there.StartIn, kept.WorkingDirectory);
    }

    [Fact]
    public void A_start_in_folder_on_a_drive_letter_this_PC_lacks_gives_way_too()
    {
        // Review 15 L1: D:\Projects\X on a laptop without D: counted as slow storage, there without a look.
        var letter = StartupRegistrationTests.UnusedDriveLetter();
        var program = Path.Combine(_directory, "tool.exe");
        var away = new ShortcutItem { Name = "Tool", Location = program, StartIn = letter + @":\Projects\X" };
        var startInfo = new System.Diagnostics.ProcessStartInfo(program) { WorkingDirectory = away.StartIn };

        LaunchService.UseProgramFolderWhenStartInIsGone(away, startInfo);

        Assert.Equal(_directory, startInfo.WorkingDirectory);
    }

    [Fact]
    public void The_clipboard_is_trimmed_and_a_value_of_several_lines_is_refused_for_any_program()
    {
        var trimmed = Create(new Prompts(clipboard: "  report.txt\r\n", answer: null), out var tracker);
        using (tracker)
        {
            var result = trimmed.Launch(new ShortcutItem { Name = "Open", Location = Path.Combine(_directory, "missing.exe"), Parameters = "{clipboard}" });

            Assert.NotEqual(Strings.Launch_ValueHasLineBreak, result.ErrorMessage); // the trailing line break is gone; the missing program is what fails
        }

        var lines = Create(new Prompts(clipboard: "one\ntwo", answer: null), out var tracker2);
        using (tracker2)
        {
            var result = lines.Launch(new ShortcutItem { Name = "Open", Location = Path.Combine(_directory, "missing.exe"), Parameters = "{clipboard}" });

            Assert.Equal(Strings.Launch_ValueHasLineBreak, result.ErrorMessage);
        }
    }

    [Fact]
    public void Run_as_administrator_wins_over_Keep_a_log_so_the_program_really_runs_elevated()
    {
        var program = new System.Diagnostics.ProcessStartInfo(Path.Combine(_directory, "tool.exe"));
        var logged = new ShortcutItem { Name = "Tool", Location = program.FileName, CaptureLog = true };
        var elevated = new ShortcutItem { Name = "Tool", Location = program.FileName, CaptureLog = true, RunAsAdministrator = true };

        Assert.True(LaunchService.CapturesOutput(logged, asAdministrator: false, program));
        Assert.False(LaunchService.CapturesOutput(logged, asAdministrator: true, program)); // Ctrl+Shift+click
        Assert.False(LaunchService.CapturesOutput(elevated, asAdministrator: false, program)); // the stored option
        Assert.Equal(LaunchPlanner.RunAsVerb, LaunchPlanner.Plan(elevated).StartInfo!.Verb);
    }

    [Fact]
    public void A_value_that_would_be_an_option_is_refused_unless_double_dash_ends_the_options()
    {
        var python = Path.Combine(_directory, "python.exe"); // not started: the refusal comes first
        var service = Create(new Prompts(clipboard: "-cprint(1+1)", answer: "--upload-pack=calc"), out var tracker);
        using (tracker)
        {
            var asCode = service.Launch(new ShortcutItem { Name = "Run", Location = python, Parameters = "{clipboard}" });
            var asOption = service.Launch(new ShortcutItem { Name = "Clone", Location = python, Parameters = "clone {ask:Repository} dir" });
            var afterDoubleDash = service.Launch(new ShortcutItem { Name = "Open", Location = python, Parameters = "script.py -- {clipboard}" });
            var insideAWord = service.Launch(new ShortcutItem { Name = "Open", Location = python, Parameters = "--file={clipboard}" });

            Assert.Equal(Strings.Launch_ValueIsOption, asCode.ErrorMessage);
            Assert.Equal(Strings.Launch_ValueIsOption, asOption.ErrorMessage);
            Assert.NotEqual(Strings.Launch_ValueIsOption, afterDoubleDash.ErrorMessage); // went on, and failed only to start
            Assert.NotEqual(Strings.Launch_ValueIsOption, insideAWord.ErrorMessage);
        }
    }

    [Fact]
    public void The_fullwidth_quotation_mark_is_refused_for_any_program()
    {
        var service = Create(new Prompts(clipboard: "repo\uFF02 --upload-pack=calc", answer: null), out var tracker);
        using (tracker)
        {
            var result = service.Launch(new ShortcutItem { Name = "Clone", Location = Path.Combine(_directory, "git.exe"), Parameters = "clone {clipboard}" });

            Assert.Equal(Strings.Launch_LookalikeQuote, result.ErrorMessage);
        }
    }

    [Fact]
    public void A_dropped_file_whose_name_PowerShell_would_run_is_refused_for_script_text()
    {
        var service = Create(new Prompts(clipboard: null, answer: null), out var tracker);
        using (tracker)
        {
            var shortcut = new ShortcutItem { Name = "Show", Location = "powershell.exe", Parameters = "-NoProfile -Command Get-Item {file}" };

            var result = service.LaunchWith(shortcut, [@"C:\t\a;Write-Output INJECTED;.txt"]);

            Assert.Equal(string.Format(Strings.Launch_UnsafeValue, ';', "PowerShell"), result.ErrorMessage);
        }
    }

    [Fact]
    public void A_dropped_file_whose_name_cmd_would_expand_is_refused_for_a_batch_file()
    {
        var service = Create(new Prompts(clipboard: null, answer: null), out var tracker);
        using (tracker)
        {
            var batch = new ShortcutItem { Name = "Convert", Location = Path.Combine(_directory, "convert.bat"), Parameters = "{file}" };

            var result = service.LaunchWith(batch, [Path.Combine(_directory, "100%PATH%.txt")]);

            Assert.Equal(LaunchOutcome.Failed, result.Outcome);
            Assert.Equal(string.Format(Strings.Launch_UnsafeValue, '%', "cmd.exe"), result.ErrorMessage);
        }
    }
}

