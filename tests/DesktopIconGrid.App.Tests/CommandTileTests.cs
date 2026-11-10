using System.Diagnostics;
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

        var watch = Stopwatch.StartNew();
        var run = await CommandTileRunner.RunAsync(Cmd("/d /c ping -n 60 127.0.0.1 >nul", timeout: 1), CancellationToken.None);

        Assert.Null(run.ExitCode);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(20), $"took {watch.Elapsed}");
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
}
