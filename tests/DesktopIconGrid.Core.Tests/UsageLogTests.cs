using System.Text.Json;
using DesktopIconGrid.Core.Launching;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Core.Storage;

namespace DesktopIconGrid.Core.Tests;

/// <summary>0.12.0 WP4 (U2, U4): how often each shortcut was used on this PC, kept in usage.json, one block per PC.</summary>
[Collection("This device")] // sets ThisDevice.Id, which is process-wide
public sealed class UsageLogTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 11, 20, 8, 0, 0, TimeSpan.Zero);

    private readonly string _folder = Directory.CreateTempSubdirectory("dig-usage-").FullName;
    private readonly string? _device = ThisDevice.Id;
    private DateTimeOffset _now = Start;

    public UsageLogTests() => ThisDevice.Id = "pc-a";

    public void Dispose()
    {
        ThisDevice.Id = _device;
        ThisDevice.IsOfThisMachine = true;
        Directory.Delete(_folder, recursive: true);
    }

    private string FilePath => Path.Combine(_folder, UsageLog.FileName);

    private UsageLog Log()
    {
        var log = new UsageLog(FilePath, () => _now);
        log.Load();
        return log;
    }

    private static bool Everything(Guid id) => true;

    [Fact]
    public void Uses_are_written_in_the_form_of_this_pc_json_with_utc_times_and_only_ids()
    {
        var notepad = Guid.NewGuid();
        var log = Log();
        Assert.Equal(Start, log.Since); // counting begins with the first start of 0.12.0 on this PC

        _now = Start.AddHours(1);
        log.Count(notepad);
        _now = Start.AddHours(2);
        log.Count(notepad);
        Assert.True(log.Save(Everything));

        using var document = JsonDocument.Parse(File.ReadAllText(FilePath));
        var root = document.RootElement;
        Assert.Equal(1, root.GetProperty("version").GetInt32());
        var block = root.GetProperty("pcs").GetProperty("pc-a");
        Assert.Equal("2026-11-20T08:00:00Z", block.GetProperty("since").GetString());
        var entry = block.GetProperty("items").GetProperty(notepad.ToString("D"));
        Assert.Equal(2, entry.GetProperty("count").GetInt32());
        Assert.Equal("2026-11-20T10:00:00Z", entry.GetProperty("last").GetString());
        Assert.Equal(["count", "last"], entry.EnumerateObject().Select(property => property.Name));

        // Read back at the next start.
        _now = Start.AddDays(3);
        var again = Log();
        Assert.Equal(Start, again.Since);
        Assert.Equal(new ItemUse(2, Start.AddHours(2)), again.Of(notepad));
        Assert.False(again.Save(Everything)); // nothing changed: nothing written
    }

    [Fact]
    public void Another_PCs_block_and_what_a_newer_version_wrote_are_kept()
    {
        File.WriteAllText(FilePath, """{"version":1,"pcs":{"pc-b":{"since":"2025-01-01T00:00:00Z","items":{"5b0f8c1e-2f39-4c35-9a33-2d0c9e3f7a11":{"count":7,"last":"2026-01-02T03:04:05Z"}}},"pc-a":{"since":"2026-11-01T00:00:00Z","items":{},"future":true}},"futureTop":5}""");

        var log = Log();
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero), log.Since);
        log.Count(Guid.NewGuid());
        Assert.True(log.Save(Everything));

        using var document = JsonDocument.Parse(File.ReadAllText(FilePath));
        var pcs = document.RootElement.GetProperty("pcs");
        var otherBlock = pcs.GetProperty("pc-b");
        Assert.Equal("2025-01-01T00:00:00Z", otherBlock.GetProperty("since").GetString());
        var otherEntry = otherBlock.GetProperty("items").GetProperty("5b0f8c1e-2f39-4c35-9a33-2d0c9e3f7a11");
        Assert.Equal((7, "2026-01-02T03:04:05Z"), (otherEntry.GetProperty("count").GetInt32(), otherEntry.GetProperty("last").GetString()));
        Assert.True(pcs.GetProperty("pc-a").GetProperty("future").GetBoolean());
        Assert.Equal(5, document.RootElement.GetProperty("futureTop").GetInt32());
    }

    [Fact]
    public void A_file_over_1_MB_or_one_that_cannot_be_read_is_left_alone_and_counting_goes_on_in_memory()
    {
        var item = Guid.NewGuid();
        foreach (var bytes in new[] { new byte[UsageLog.MaxFileBytes + 1], "{ not json"u8.ToArray() })
        {
            File.WriteAllBytes(FilePath, bytes);
            var log = Log();
            Assert.True(log.IsMemoryOnly);

            log.Count(item);
            Assert.Equal(1, log.Of(item)!.Count);
            Assert.False(log.Save(Everything));
            Assert.Equal(bytes, File.ReadAllBytes(FilePath)); // untouched
        }
    }

    [Fact]
    public void Without_this_PCs_own_id_nothing_is_read_or_written()
    {
        ThisDevice.IsOfThisMachine = false;
        var log = Log();
        log.Count(Guid.NewGuid());

        Assert.True(log.IsMemoryOnly);
        Assert.False(log.Save(Everything));
        Assert.False(File.Exists(FilePath));
    }

    [Fact]
    public void Items_gone_from_the_bar_are_dropped_400_days_after_their_last_use_and_the_others_kept()
    {
        var kept = Guid.NewGuid(); // still on the bar, used long ago
        var removedLongAgo = Guid.NewGuid();
        var removedLately = Guid.NewGuid(); // an Undo may bring it back
        var log = Log();
        log.Count(kept);
        log.Count(removedLongAgo);
        _now = Start.AddDays(100);
        log.Count(removedLately);

        _now = Start.AddDays(401);
        log.Count(Guid.NewGuid()); // a change, so the next save writes
        Assert.True(log.Save(id => id == kept));

        var again = Log();
        Assert.NotNull(again.Of(kept));
        Assert.Null(again.Of(removedLongAgo));
        Assert.NotNull(again.Of(removedLately));
    }

    [Fact]
    public void A_use_counts_only_for_a_shortcut_that_was_started_or_brought_forward()
    {
        var shortcut = new ShortcutItem { Name = "Notepad", Location = "notepad.exe" };
        Assert.True(UsageRules.IsCounted(shortcut, LaunchOutcome.Started)); // started, or its window brought forward
        Assert.False(UsageRules.IsCounted(shortcut, LaunchOutcome.Failed));
        Assert.False(UsageRules.IsCounted(shortcut, LaunchOutcome.Cancelled)); // an approval or a question cancelled
        Assert.False(UsageRules.IsCounted(new SpecialIconItem { Kind = "clock" }, LaunchOutcome.Started));
        Assert.False(UsageRules.IsCounted(new GroupItem { Name = "Tools" }, LaunchOutcome.Started));
        Assert.False(UsageRules.IsCounted(new SeparatorItem(), LaunchOutcome.Started));
    }

    [Fact]
    public void Not_used_in_90_days_lists_a_never_used_shortcut_only_once_counting_has_run_that_long()
    {
        var since = Start;
        var day89 = since.AddDays(89);
        var day90 = since.AddDays(90);

        Assert.False(UsageRules.IsUnused(null, since, day89)); // nobody knows yet
        Assert.True(UsageRules.IsUnused(null, since, day90));
        Assert.Equal(day90, UsageRules.CompleteFrom(since));

        var used = new ItemUse(3, since.AddDays(10));
        Assert.False(UsageRules.IsUnused(used, since, since.AddDays(100))); // exactly 90 days ago
        Assert.True(UsageRules.IsUnused(used, since, since.AddDays(100).AddMinutes(1)));
    }

    [Fact]
    public void A_last_use_reads_today_yesterday_days_ago_up_to_30_then_its_date_by_local_days()
    {
        // UTC+8: local midnight is 16:00 UTC the day before.
        var taipei = TimeZoneInfo.CreateCustomTimeZone("UTC+8", TimeSpan.FromHours(8), "UTC+8", "UTC+8");
        var now = new DateTimeOffset(2026, 11, 20, 16, 1, 0, TimeSpan.Zero); // 00:01 on 21 November, local

        Assert.Equal(LastUseKind.Never, LastUse.Of(null, now, taipei).Kind);
        Assert.Equal(LastUseKind.Today, LastUse.Of(now.AddMinutes(-1), now, taipei).Kind); // 00:00 local
        Assert.Equal((LastUseKind.Yesterday, 1), Of(now.AddMinutes(-2))); // 23:59 the day before, local
        Assert.Equal((LastUseKind.DaysAgo, 2), Of(now.AddDays(-2)));
        Assert.Equal((LastUseKind.DaysAgo, 30), Of(now.AddDays(-30)));
        Assert.Equal((LastUseKind.Date, 31), Of(now.AddDays(-31)));
        Assert.Equal(LastUseKind.Today, LastUse.Of(now.AddHours(1), now, taipei).Kind); // a clock set back
        Assert.Equal(new DateTime(2026, 10, 21, 0, 1, 0), LastUse.Of(now.AddDays(-31), now, taipei).Local);

        (LastUseKind, int) Of(DateTimeOffset last)
        {
            var use = LastUse.Of(last, now, taipei);
            return (use.Kind, use.Days);
        }
    }
}
