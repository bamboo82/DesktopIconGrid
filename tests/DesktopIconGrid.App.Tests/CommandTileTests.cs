using System.Diagnostics;
using System.Windows.Threading;
using DesktopIconGrid.Core.Launching;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Core.SpecialIcons;
using DesktopIconGrid.SpecialIcons;
using DesktopIconGrid.ViewModels;

namespace DesktopIconGrid.App.Tests;

/// <summary>The command tile's runner and its properties dialog.</summary>
public class CommandTileTests
{
    private static CommandTileSettings Cmd(string arguments, int timeout = 30) =>
        CommandTileSettings.Default with { Program = "cmd.exe", Arguments = arguments, TimeoutSeconds = timeout };

    [Fact]
    public async Task A_run_returns_the_output_and_the_exit_code_without_a_window()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var info = CommandTileRunner.StartInfo(Cmd("/d /c echo hello"));
        Assert.True(info.CreateNoWindow);
        Assert.False(info.UseShellExecute);

        var run = await CommandTileRunner.RunAsync(Cmd("/d /c echo hello& echo oops 1>&2& exit /b 1"), CancellationToken.None);

        Assert.Null(run.StartError);
        Assert.Equal(1, run.ExitCode);
        Assert.Equal("hello", run.Output.Trim());
        Assert.Equal("oops", run.Errors.Trim());
    }

    [Fact]
    public async Task A_run_over_its_time_limit_is_stopped_with_its_children()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        // A ping to an address no other test uses, so its children can be told apart.
        static int Pings() => Process.GetProcessesByName("PING").Count(p => { try { return p.StartTime > DateTime.Now.AddMinutes(-2); } catch (Exception) { return false; } });
        var before = Pings();
        var watch = Stopwatch.StartNew();
        var run = await CommandTileRunner.RunAsync(Cmd("/d /c ping -n 60 127.0.0.9 >nul", timeout: 1), CancellationToken.None);

        Assert.Null(run.ExitCode);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(20), $"took {watch.Elapsed}");
        await Task.Delay(500);
        Assert.True(Pings() <= before, "the ping started by cmd outlived the time limit"); // the whole tree was stopped
    }

    [Fact]
    public async Task A_program_that_is_not_there_says_why()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var run = await CommandTileRunner.RunAsync(
            CommandTileSettings.Default with { Program = @"C:\no such folder\nothing.exe" }, CancellationToken.None);

        Assert.NotNull(run.StartError);
        Assert.Null(run.ExitCode);
    }

    [Fact]
    public void The_dialog_saves_only_settings_that_can_run()
    {
        var item = new SpecialIconItem { Name = "Build", Kind = SpecialIconKinds.CommandTile };
        CommandTileSettings.Default.ApplyTo(item);
        var viewModel = new CommandTileViewModel(item);

        Assert.False(viewModel.CanSave); // no program yet
        viewModel.Program = "git.exe";
        viewModel.Arguments = "status --short";
        Assert.True(viewModel.CanSave);
        viewModel.Interval = "soon";
        Assert.False(viewModel.CanSave);
        viewModel.Interval = "5"; // saved as the shortest timer
        viewModel.OpensTarget = true;
        Assert.False(viewModel.CanSave); // nothing to open
        viewModel.OpenTarget = @"C:\src";
        viewModel.SelectedDisplay = CommandTileViewModel.DisplayChoices.Single(c => c.Display == CommandTileDisplay.Bar);
        Assert.True(viewModel.ShowsGauge);
        Assert.True(viewModel.CanSave);

        viewModel.Apply(item);
        var saved = CommandTileSettings.From(item);

        Assert.Equal(("git.exe", "status --short"), (saved.Program, saved.Arguments));
        Assert.Equal(CommandTileSettings.MinIntervalSeconds, saved.IntervalSeconds);
        Assert.Equal(CommandTileClick.Open, saved.Click);
        Assert.Equal(CommandTileDisplay.Bar, saved.Display);
    }

    private static SpecialIconItem Tile(string arguments, int interval = 0)
    {
        var item = new SpecialIconItem { Name = "Tile", Kind = SpecialIconKinds.CommandTile };
        (CommandTileSettings.Default with { Program = "cmd.exe", Arguments = arguments, IntervalSeconds = interval }).ApplyTo(item);
        return item;
    }

    [Fact]
    public void An_unapproved_tile_waits_and_a_click_approves_exactly_what_it_showed()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var trust = new CommandTrust(null);
        CommandTileSettings? shown = null;
        var answer = false;
        var handler = new CommandTileHandler(trust, () => null, Dispatcher.CurrentDispatcher, (_, settings) =>
        {
            shown = settings;
            return answer;
        });
        var item = Tile("/d /c exit 0");
        var state = handler.CreateState(item)!;
        try
        {
            Assert.Equal("⏸", state.LabelText);
            Assert.False(handler.GetActions(item).Single().IsEnabled); // Run now is gated too

            Assert.Equal(LaunchOutcome.Cancelled, handler.Activate(item, ActivationModifiers.None).Outcome);
            Assert.Equal("⏸", state.LabelText);
            Assert.False(trust.IsApproved(CommandTileSettings.From(item).Hash()));

            answer = true;
            Assert.True(handler.Activate(item, ActivationModifiers.None).Succeeded);

            Assert.Equal(CommandTileSettings.From(item), shown);
            Assert.True(trust.IsApproved(shown!.Hash()));
            Assert.NotEqual("⏸", state.LabelText); // it started
            Assert.True(handler.GetActions(item).Single().IsEnabled);
        }
        finally
        {
            state.Detach();
        }
    }

    [Fact]
    public void An_approval_starts_only_the_tiles_with_that_command()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var trust = new CommandTrust(null);
        var handler = new CommandTileHandler(trust, () => null, Dispatcher.CurrentDispatcher, (_, _) => false);
        var first = Tile("/d /c exit 0");
        var same = Tile("/d /c exit 0");
        var other = Tile("/d /c exit 1");
        var timed = Tile("/d /c exit 0", interval: 60); // the same program, but on a timer: another approval
        var states = new[] { first, same, other, timed }.Select(item => handler.CreateState(item)!).ToList();
        try
        {
            trust.Approve(CommandTileSettings.From(first).Hash());

            Assert.Equal([false, false, true, true], states.Select(s => s.LabelText == "⏸"));
        }
        finally
        {
            states.ForEach(s => s.Detach());
        }
    }

    [Fact]
    public void Saving_approves_a_new_or_trusted_tile_but_not_one_that_arrived_paused()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var trust = new CommandTrust(null);
        var handler = new CommandTileHandler(trust, () => null, Dispatcher.CurrentDispatcher, (_, _) => false);

        var arrived = Tile("/d /c exit 0");
        handler.NoteSaved(CommandTileSettings.From(arrived), trustedBefore: false); // OK in its Properties
        var arrivedState = handler.CreateState(arrived)!;
        var typed = Tile("/d /c exit 1");
        handler.NoteSaved(CommandTileSettings.From(typed), trustedBefore: true); // new, or an edit of an approved one
        var typedState = handler.CreateState(typed)!;
        try
        {
            Assert.Equal("⏸", arrivedState.LabelText);
            Assert.False(trust.IsApproved(CommandTileSettings.From(arrived).Hash()));
            Assert.NotEqual("⏸", typedState.LabelText);
            Assert.True(trust.IsApproved(CommandTileSettings.From(typed).Hash()));
        }
        finally
        {
            arrivedState.Detach();
            typedState.Detach();
        }
    }

    [Fact]
    public void The_approval_window_shows_the_timer_and_what_a_click_opens()
    {
        var text = DesktopIconGrid.Views.CommandApprovalWindow.Describe(
            CommandTileSettings.Default with { Program = "deploy.cmd", Arguments = "--prod", IntervalSeconds = 30, Click = CommandTileClick.Open, OpenTarget = @"\\srv\logs" });

        Assert.Contains("deploy.cmd", text);
        Assert.Contains("--prod", text);
        Assert.Contains("30", text);
        Assert.Contains(@"\\srv\logs", text);
    }
}
