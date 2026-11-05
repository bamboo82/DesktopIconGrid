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
}
