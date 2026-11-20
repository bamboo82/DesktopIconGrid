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
    public void A_file_over_1_MB_is_left_alone_and_counting_goes_on_in_memory()
    {
        var item = Guid.NewGuid();
        var bytes = new byte[UsageLog.MaxFileBytes + 1];
        File.WriteAllBytes(FilePath, bytes);
        var log = Log();
        Assert.True(log.IsMemoryOnly);

        log.Count(item);
        Assert.Equal(1, log.Of(item)!.Count);
        Assert.False(log.Save(Everything));
        Assert.Equal(bytes, File.ReadAllBytes(FilePath)); // untouched
    }

    [Theory]
    [InlineData("")]
    [InlineData("\0\0\0\0")]
    [InlineData("{ not json")]
    [InlineData("[1, 2]")]
    [InlineData("""{"version":1,"pcs":7}""")]
    [InlineData("""{"version":1,"pcs":{"pc-a":{"items":{}},"pc-a":{"items":{}}}}""")]
    public void A_file_that_is_not_the_json_this_app_writes_is_kept_beside_it_and_this_PC_counts_anew(string text)
    {
        // 0.12.1, review 28 U2: such a file (an empty one, NUL bytes after a power cut) meant memory only at every start,
        // for good.
        var item = Guid.NewGuid();
        var bytes = System.Text.Encoding.UTF8.GetBytes(text);
        File.WriteAllBytes(FilePath, bytes);

        var log = Log();
        Assert.False(log.IsMemoryOnly);
        var aside = Assert.Single(Directory.GetFiles(_folder, UsageLog.FileName + ".corrupt-*"));
        Assert.Equal(bytes, File.ReadAllBytes(aside));

        log.Count(item);
        Assert.True(log.Save(Everything));
        Assert.Equal(new ItemUse(1, Start), Log().Of(item));
        Assert.Equal(bytes, File.ReadAllBytes(aside)); // kept as it was
    }

    [Fact]
    public void A_bad_date_in_another_PCs_block_harms_nothing_and_this_PCs_block_that_is_no_block_is_kept_beside_it()
    {
        // Review 28 U2: one bad date in another PC's block, or this PC's block a number, made the whole file unreadable.
        var item = Guid.NewGuid();
        const string other = """{"since":"not a date","items":{"5b0f8c1e-2f39-4c35-9a33-2d0c9e3f7a11":{"count":"many","last":"yesterday"}}}""";
        File.WriteAllText(FilePath, """{"version":1,"pcs":{"pc-b":""" + other + ""","pc-a":{"since":"2026-11-01T00:00:00Z","items":{}}}}""");

        var log = Log();
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero), log.Since); // this PC's block is read
        Assert.Empty(Directory.GetFiles(_folder, UsageLog.FileName + ".corrupt-*"));
        log.Count(item);
        Assert.True(log.Save(Everything));
        Assert.True(System.Text.Json.Nodes.JsonNode.DeepEquals(System.Text.Json.Nodes.JsonNode.Parse(other), System.Text.Json.Nodes.JsonNode.Parse(Block("pc-b").GetRawText())));

        // This PC's block a number: the file is kept beside it, the other blocks are written back with a new one.
        File.WriteAllText(FilePath, """{"version":1,"pcs":{"pc-b":""" + other + ""","pc-a":42}}""");
        var again = Log();
        Assert.Single(Directory.GetFiles(_folder, UsageLog.FileName + ".corrupt-*"));
        Assert.Equal(Start, again.Since);
        again.Count(item);
        Assert.True(again.Save(Everything));
        Assert.Equal(1, Block("pc-a").GetProperty("items").GetProperty(item.ToString("D")).GetProperty("count").GetInt32());
        Assert.Equal("not a date", Block("pc-b").GetProperty("since").GetString());
    }

    [Fact]
    public void A_write_that_fails_keeps_the_counts_for_the_next_save_and_exit()
    {
        // Release check U1: the temporary file held for a moment (a scanner, a sync client) must not lose the session's
        // counts. The temporary file is this instance's own (review 28 U6).
        var item = Guid.NewGuid();
        var log = Log();
        log.Count(item);
        Assert.NotEqual(log.TemporaryPath, Log().TemporaryPath);
        Directory.CreateDirectory(log.TemporaryPath); // the temporary file cannot be written

        Assert.False(log.Save(Everything));
        Assert.False(log.IsMemoryOnly);
        Assert.False(File.Exists(FilePath));

        Directory.Delete(log.TemporaryPath);
        Assert.True(log.Save(Everything)); // no use since: still to be written, as at exit
        Assert.Equal(new ItemUse(1, Start), Log().Of(item));
    }

    [Fact]
    public void A_save_that_throws_something_unexpected_keeps_the_counts_for_the_next_save()
    {
        // 0.12.2, review 29 N5: only IOException and UnauthorizedAccessException kept the counts to be written.
        var item = Guid.NewGuid();
        var log = Log();
        log.Count(item);
        log.BeforeReplace = () => throw new InvalidOperationException("something this code did not expect");

        Assert.False(log.Save(Everything));
        Assert.False(log.IsMemoryOnly);
        Assert.False(File.Exists(FilePath));
        Assert.False(File.Exists(log.TemporaryPath));

        log.BeforeReplace = null;
        Assert.True(log.Save(Everything)); // no use since: still to be written
        Assert.Equal(new ItemUse(1, Start), Log().Of(item));
    }

    [Fact]
    public void A_replace_that_fails_keeps_the_counts_and_leaves_no_temporary_file()
    {
        // Review 28: only the temporary file's write was failed, never the move.
        var item = Guid.NewGuid();
        var log = Log();
        log.Count(item);
        Directory.CreateDirectory(FilePath); // usage.json cannot be replaced

        Assert.False(log.Save(Everything));
        Assert.False(File.Exists(log.TemporaryPath));
        Assert.Empty(Directory.GetFiles(_folder, "*.tmp"));

        Directory.Delete(FilePath);
        Assert.True(log.Save(Everything));
        Assert.Equal(new ItemUse(1, Start), Log().Of(item));
    }

    [Fact]
    public void A_block_another_copy_writes_just_before_the_replace_is_kept()
    {
        // Review 28 U6: two copies on one portable folder replaced the file without reading it again.
        var item = Guid.NewGuid();
        var log = Log();
        log.Count(item);
        Assert.True(log.Save(Everything));

        log.Count(item);
        var writes = 0;
        log.BeforeReplace = () =>
        {
            if (writes++ == 0)
            {
                // Another PC's copy writes its block in the meantime, as this app would.
                var other = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(FilePath))!;
                other["pcs"]!["pc-b"] = System.Text.Json.Nodes.JsonNode.Parse("""{"since":"2026-01-01T00:00:00Z","items":{}}""");
                File.WriteAllText(FilePath, other.ToJsonString());
                File.SetLastWriteTimeUtc(FilePath, DateTime.UtcNow.AddSeconds(5));
            }
        };

        Assert.True(log.Save(Everything));
        Assert.Equal(2, writes); // written again with what the other copy wrote
        Assert.Equal("2026-01-01T00:00:00Z", Block("pc-b").GetProperty("since").GetString());
        Assert.Equal(2, Log().Of(item)!.Count);
    }

    [Fact]
    public void No_probe_is_left_in_a_folder_that_can_be_written()
    {
        // Review 28 U4: the probe was deleted by hand, and a failed delete meant memory only and a probe left for ever.
        var log = Log();
        Assert.False(log.IsMemoryOnly);
        Assert.Empty(Directory.GetFiles(_folder, "*.probe"));
    }

    [Fact]
    public void Entries_this_version_cannot_read_and_unknown_properties_are_kept_through_saves_and_a_join()
    {
        // Review 28 U7: they were dropped at the next save; nothing checked them through a join.
        var item = Guid.NewGuid();
        File.WriteAllText(FilePath, """{"version":1,"pcs":{"pc-a":{"since":"2026-11-01T00:00:00Z","items":{"not-an-id":{"count":4,"last":"2026-11-02T00:00:00Z"},"ZERO":{"count":0,"last":"2026-11-02T00:00:00Z"},"NOLAST":{"count":2},"ITEM":{"count":3,"last":"2026-11-10T00:00:00Z","pinned":true}},"future":1}}}"""
            .Replace("ZERO", Guid.NewGuid().ToString("D"), StringComparison.Ordinal)
            .Replace("NOLAST", Guid.NewGuid().ToString("D"), StringComparison.Ordinal)
            .Replace("ITEM", item.ToString("D"), StringComparison.Ordinal));
        string before;
        using (var document = JsonDocument.Parse(File.ReadAllText(FilePath)))
        {
            before = document.RootElement.GetProperty("pcs").GetProperty("pc-a").GetProperty("items").ToString();
        }

        UsageLog log;
        using (Hold(FilePath))
        {
            log = Log(); // read at the first save, and joined
            log.Count(item);
        }

        Assert.True(log.Save(Everything));
        Assert.Equal(new ItemUse(4, Start), log.Of(item));
        var items = Block("pc-a").GetProperty("items");
        Assert.Equal(4, items.EnumerateObject().Count());
        Assert.Equal(4, items.GetProperty("not-an-id").GetProperty("count").GetInt32());
        Assert.True(items.GetProperty(item.ToString("D")).GetProperty("pinned").GetBoolean());
        Assert.Equal(1, Block("pc-a").GetProperty("future").GetInt32());
        Assert.Equal(4, items.GetProperty(item.ToString("D")).GetProperty("count").GetInt32()); // the counted one changed
        using var read = JsonDocument.Parse(before);
        foreach (var entry in read.RootElement.EnumerateObject().Where(entry => entry.Name != item.ToString("D")))
        {
            Assert.True(JsonElement.DeepEquals(entry.Value, items.GetProperty(entry.Name)), entry.Name); // the others did not
        }
    }

    [Fact]
    public void A_clock_set_back_does_not_move_a_last_use_back()
    {
        var item = Guid.NewGuid();
        var log = Log();
        log.Count(item);
        _now = Start.AddDays(-30); // the clock set back a month
        log.Count(item);
        Assert.Equal(new ItemUse(2, Start), log.Of(item));
    }

    [Fact]
    public void No_file_over_the_limit_is_written_the_oldest_uses_of_items_gone_from_the_bar_go_first()
    {
        // Review 28 U5: past 1 MB every later start took the file as not this app's.
        var onBar = new HashSet<Guid>();
        var log = new UsageLog(FilePath, () => _now) { MaxBytes = 4096 };
        log.Load();
        var gone = new List<Guid>();
        for (var i = 0; i < 60; i++)
        {
            _now = Start.AddMinutes(i);
            var id = Guid.NewGuid();
            (i % 10 == 0 ? (ICollection<Guid>)onBar : gone).Add(id);
            log.Count(id);
        }

        Assert.True(log.Save(onBar.Contains));
        Assert.True(new FileInfo(FilePath).Length <= 4096);
        var kept = new UsageLog(FilePath, () => _now) { MaxBytes = 4096 };
        kept.Load();
        Assert.All(onBar, id => Assert.NotNull(kept.Of(id)));
        var keptGone = gone.Where(id => kept.Of(id) is not null).ToList();
        Assert.NotEmpty(keptGone);
        Assert.Equal(gone.TakeLast(keptGone.Count), keptGone); // the newest of them stay

        // Other PCs' blocks alone near the limit: this PC's would make it too large, so nothing is written.
        var big = "{\"version\":1,\"pcs\":{\"pc-b\":{\"since\":\"2026-01-01T00:00:00Z\",\"items\":{},\"note\":\"" + new string('x', 5000) + "\"}}}";
        File.WriteAllText(FilePath, big);
        var full = new UsageLog(FilePath, () => _now) { MaxBytes = new FileInfo(FilePath).Length + 40 }; // it can be read
        full.Load();
        Assert.False(full.IsMemoryOnly);
        full.Count(Guid.NewGuid());
        Assert.False(full.Save(Everything)); // on the bar, so nothing to drop
        Assert.Equal(big, File.ReadAllText(FilePath));
    }

    [Fact]
    public void A_file_that_cannot_be_read_at_the_start_is_read_again_at_the_first_save_and_its_counts_joined()
    {
        var item = Guid.NewGuid();
        var other = Guid.NewGuid();
        File.WriteAllText(FilePath, """{"version":1,"pcs":{"pc-a":{"since":"2026-11-01T00:00:00Z","items":{"ITEM":{"count":3,"last":"2026-11-10T00:00:00Z"},"OTHER":{"count":2,"last":"2026-11-12T00:00:00Z"}}}}}"""
            .Replace("ITEM", item.ToString("D"), StringComparison.Ordinal).Replace("OTHER", other.ToString("D"), StringComparison.Ordinal));

        UsageLog log;
        using (Hold(FilePath))
        {
            log = Log(); // locked: counted in memory meanwhile
            Assert.False(log.IsMemoryOnly);
            log.Count(item);
            Assert.Equal(1, log.Of(item)!.Count);
        }

        Assert.True(log.Save(Everything));
        Assert.False(log.IsMemoryOnly);
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero), log.Since); // the file's, not this start's
        Assert.Equal(new ItemUse(4, Start), log.Of(item)); // 3 from the file, 1 since
        Assert.Equal(new ItemUse(2, new DateTimeOffset(2026, 11, 12, 0, 0, 0, TimeSpan.Zero)), log.Of(other));

        var again = Log();
        Assert.Equal(new ItemUse(4, Start), again.Of(item));
        Assert.Equal(2, again.Of(other)!.Count);
    }

    [Fact]
    public void A_file_still_unreadable_at_the_first_save_is_left_alone_and_counting_goes_on_in_memory()
    {
        var item = Guid.NewGuid();
        var text = """{"version":1,"pcs":{"pc-a":{"since":"2026-11-01T00:00:00Z","items":{"ITEM":{"count":3,"last":"2026-11-10T00:00:00Z"}}}}}"""
            .Replace("ITEM", item.ToString("D"), StringComparison.Ordinal);
        File.WriteAllText(FilePath, text);

        UsageLog log;
        using (Hold(FilePath))
        {
            log = Log();
            log.Count(item);
            Assert.Equal(1, log.Of(item)!.Count); // the uses counted here go on
            Assert.False(log.Save(Everything));
            Assert.True(log.IsMemoryOnly); // tried once more, then given up
        }

        log.Count(item);
        Assert.Equal(2, log.Of(item)!.Count);
        Assert.False(log.Save(Everything)); // free again, but left alone for this session
        Assert.Equal(text, File.ReadAllText(FilePath));
    }

    [Fact]
    public void A_folder_that_cannot_be_written_at_the_start_keeps_the_counts_in_memory()
    {
        var blocker = Path.Combine(_folder, "portable");
        File.WriteAllText(blocker, "a file where the data folder would be");
        var log = new UsageLog(Path.Combine(blocker, UsageLog.FileName), () => _now);
        log.Load();

        Assert.True(log.IsMemoryOnly);
        log.Count(Guid.NewGuid());
        Assert.False(log.Save(Everything));
        Assert.Equal(["portable"], Directory.EnumerateFileSystemEntries(_folder).Select(Path.GetFileName)); // no probe left
    }

    [Fact]
    public void A_file_damaged_after_the_start_is_kept_beside_it_before_the_counts_are_written()
    {
        // Review 28 U2, at a save: a file that is not the JSON this app writes is not lost, and the counts are written.
        var item = Guid.NewGuid();
        var log = Log();
        log.Count(item);
        File.WriteAllText(FilePath, "{ not json");

        Assert.True(log.Save(Everything));
        Assert.False(log.IsMemoryOnly);
        Assert.Equal("{ not json", File.ReadAllText(Assert.Single(Directory.GetFiles(_folder, UsageLog.FileName + ".corrupt-*"))));
        Assert.Equal(1, Log().Of(item)!.Count);

        // A file over the limit is not this app's: never written over, and the counts wait.
        var big = new byte[UsageLog.MaxFileBytes + 1];
        File.WriteAllBytes(FilePath, big);
        log.Count(item);
        Assert.False(log.Save(Everything));
        Assert.Equal(big, File.ReadAllBytes(FilePath));
        File.Delete(FilePath);
        Assert.True(log.Save(Everything));
        Assert.Equal(2, Log().Of(item)!.Count);
    }

    /// <summary>The block of <paramref name="pc"/> in the file as it is now.</summary>
    private JsonElement Block(string pc)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(FilePath));
        return document.RootElement.GetProperty("pcs").GetProperty(pc).Clone();
    }

    /// <summary>Opens <paramref name="path"/> so that nothing else can read it (on Linux .NET takes an exclusive lock).</summary>
    private static FileStream Hold(string path) => new(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

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
        Assert.False(UsageRules.IsCounted(shortcut, LaunchOutcome.Minimized)); // the program in front minimised (0.12.2, N2)
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
