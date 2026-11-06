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
}

