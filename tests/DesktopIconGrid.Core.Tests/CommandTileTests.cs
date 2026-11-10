using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Core.SpecialIcons;

namespace DesktopIconGrid.Core.Tests;

/// <summary>The command tile: its settings, what a run shows, and approval per PC.</summary>
[Collection("This device")]
public sealed class CommandTileTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "dig-trust-" + Guid.NewGuid().ToString("N"));
    private readonly string? _device = ThisDevice.Id;

    public void Dispose()
    {
        ThisDevice.Id = _device;
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void Settings_round_trip_and_keep_the_timer_and_timeout_in_range()
    {
        var item = new SpecialIconItem { Kind = SpecialIconKinds.CommandTile };
        new CommandTileSettings(@"C:\Tools\check.exe", "--quiet", @"C:\Work", 3, 5000, CommandTileDisplay.Ring, 60, 85, CommandTileClick.Open, @"C:\Work")
            .ApplyTo(item);

        var read = CommandTileSettings.From(item);

        Assert.Equal(@"C:\Tools\check.exe", read.Program);
        Assert.Equal("--quiet", read.Arguments);
        Assert.Equal(CommandTileSettings.MinIntervalSeconds, read.IntervalSeconds); // 3 s is too often
        Assert.Equal(CommandTileSettings.MaxTimeoutSeconds, read.TimeoutSeconds);
        Assert.Equal(CommandTileDisplay.Ring, read.Display);
        Assert.Equal((60d, 85d), (read.WarningAt, read.CriticalAt));
        Assert.Equal(CommandTileClick.Open, read.Click);
        Assert.Null(read.Problem());

        Assert.Equal(0, CommandTileSettings.NormalizeInterval(-5)); // on a click only
        Assert.True((read with { IntervalSeconds = 0 }).RunsOnClickOnly);
        Assert.NotNull(CommandTileSettings.Default.Problem()); // no program
        Assert.NotNull((read with { Arguments = "a\nb" }).Problem());
        Assert.NotNull((read with { OpenTarget = null }).Problem());
    }

    [Fact]
    public void Unknown_or_broken_values_fall_back_to_the_defaults()
    {
        var item = new SpecialIconItem { Kind = SpecialIconKinds.CommandTile };
        item.SetSetting(CommandTileSettings.ProgramKey, "ping.exe");
        item.SetSetting(CommandTileSettings.DisplayKey, "hologram");
        item.SetSetting(CommandTileSettings.ClickKey, "7");
        item.SetSetting(CommandTileSettings.IntervalKey, "soon");
        item.SetSetting(CommandTileSettings.WarningKey, "NaN");

        var read = CommandTileSettings.From(item);

        Assert.Equal(CommandTileDisplay.Text, read.Display);
        Assert.Equal(CommandTileClick.RunNow, read.Click);
        Assert.Equal(CommandTileSettings.Default.IntervalSeconds, read.IntervalSeconds);
        Assert.Equal(CommandTileSettings.Default.WarningAt, read.WarningAt);
    }

    [Fact]
    public void The_hash_covers_what_runs_and_nothing_else()
    {
        var settings = CommandTileSettings.Default with { Program = "git.exe", Arguments = "status --short", StartIn = @"C:\src" };

        Assert.Equal(64, settings.Hash().Length);
        Assert.Equal(settings.Hash(), (settings with { IntervalSeconds = 600, Display = CommandTileDisplay.Bar, Click = CommandTileClick.Open, OpenTarget = "x" }).Hash());
        Assert.NotEqual(settings.Hash(), (settings with { Arguments = "status" }).Hash());
        Assert.NotEqual(settings.Hash(), (settings with { StartIn = @"C:\other" }).Hash());
        Assert.NotEqual(settings.Hash(), (settings with { Program = "git2.exe" }).Hash());
        // A field's end must not slide into the next one.
        Assert.NotEqual(
            (settings with { Arguments = "a", StartIn = null }).Hash(),
            (settings with { Arguments = null, StartIn = "a" }).Hash());
    }

    [Theory]
    [InlineData("All good\nsecond line", 0, "All good", CommandLevel.Ok)]
    [InlineData("", 0, "✓", CommandLevel.Ok)]
    [InlineData("  \n  3 files changed  \n", 1, "3 files changed", CommandLevel.Warning)]
    [InlineData("", 2, "2", CommandLevel.Failed)]
    [InlineData(null, null, "…", CommandLevel.Failed)] // did not end in time
    public void Text_shows_the_first_line_and_the_exit_code_picks_the_colour(string? output, int? exitCode, string label, CommandLevel level)
    {
        var reading = CommandTileOutput.Read(CommandTileSettings.Default with { Program = "x" }, output, null, exitCode);

        Assert.Equal(label, reading.Label);
        Assert.Equal(level, reading.Level);
        Assert.Null(reading.Percent);
    }

    [Fact]
    public void Text_falls_back_to_the_errors_and_keeps_the_tile_short()
    {
        var settings = CommandTileSettings.Default with { Program = "x" };

        Assert.Equal("not found", CommandTileOutput.Read(settings, "", "not found", 2).Label);
        var long_ = CommandTileOutput.Read(settings, new string('a', 100), null, 0).Label;
        Assert.Equal(CommandTileOutput.MaxLabelLength, long_.Length);
        Assert.EndsWith("…", long_);
        Assert.Equal(CommandTileOutput.MaxOutputLines, CommandTileOutput.Read(settings, string.Join('\n', Enumerable.Range(1, 20)), null, 0).Output.Split('\n').Length);
    }

    [Theory]
    [InlineData("42", 0, 42d, CommandLevel.Ok)]
    [InlineData("queue: 75%", 0, 75d, CommandLevel.Warning)]
    [InlineData("disk 95.5", 0, 95.5d, CommandLevel.Failed)]
    [InlineData("250", 0, 100d, CommandLevel.Failed)] // drawn full; the label keeps the number
    [InlineData("no number here", 0, null, CommandLevel.Ok)]
    [InlineData("12", 2, 12d, CommandLevel.Failed)] // the exit code wins over the thresholds
    public void A_gauge_reads_the_first_number_with_the_thresholds(string output, int exitCode, double? percent, CommandLevel level)
    {
        var settings = CommandTileSettings.Default with { Program = "x", Display = CommandTileDisplay.Ring, WarningAt = 70, CriticalAt = 90 };

        var reading = CommandTileOutput.Read(settings, output, null, exitCode);

        Assert.Equal(percent, reading.Percent);
        Assert.Equal(level, reading.Level);
        Assert.Equal(percent is null ? "?" : output == "250" ? "250" : Math.Round(CommandTileOutput.Number(output)!.Value).ToString(System.Globalization.CultureInfo.InvariantCulture), reading.Label);
    }

    [Fact]
    public void Approval_is_per_PC_and_kept_in_the_local_file()
    {
        var path = Path.Combine(_directory, CommandTrust.FileName);
        var hash = (CommandTileSettings.Default with { Program = "x.exe" }).Hash();
        ThisDevice.Id = "pc-a";
        var trust = new CommandTrust(path);
        var changes = 0;
        trust.Changed += (_, _) => changes++;

        Assert.False(trust.IsApproved(hash));
        Assert.True(trust.Approve(hash));
        Assert.True(trust.IsApproved(hash));
        Assert.True(new CommandTrust(path).IsApproved(hash)); // read back
        Assert.Equal(1, changes);

        ThisDevice.Id = "pc-b"; // the same data folder on another PC (a portable copy on a stick)
        Assert.False(new CommandTrust(path).IsApproved(hash));
    }

    [Fact]
    public void A_damaged_file_approves_nothing_and_the_oldest_approvals_go_first()
    {
        ThisDevice.Id = "pc-a";
        var path = Path.Combine(_directory, CommandTrust.FileName);
        Directory.CreateDirectory(_directory);
        File.WriteAllText(path, "{ not json");
        Assert.False(new CommandTrust(path).IsApproved(new string('a', 64)));

        var time = DateTimeOffset.UnixEpoch;
        var trust = new CommandTrust(path, () => time = time.AddMinutes(1));
        var hashes = Enumerable.Range(0, CommandTrust.MaxEntries + 1).Select(i => i.ToString("x64")).ToList();
        hashes.ForEach(h => trust.Approve(h));

        Assert.False(trust.IsApproved(hashes[0]));
        Assert.True(trust.IsApproved(hashes[^1]));
    }
}
