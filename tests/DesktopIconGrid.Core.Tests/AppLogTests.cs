using DesktopIconGrid.Core.Diagnostics;

namespace DesktopIconGrid.Core.Tests;

public class AppLogTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Entries_are_kept_newest_last_within_capacity_and_raise_events()
    {
        var log = new AppLog(capacity: 10, clock: () => Now);
        var raised = new List<LogEntry>();
        log.EntryAdded += (_, e) => raised.Add(e);

        for (var i = 0; i < 15; i++)
        {
            log.Info("Test", $"message {i}");
        }

        var entries = log.Snapshot();
        Assert.Equal(10, entries.Count);
        Assert.Equal("message 5", entries[0].Message);
        Assert.Equal("message 14", entries[^1].Message);
        Assert.Equal(15, raised.Count);
        Assert.Equal(LogLevel.Info, raised[0].Level);
        Assert.Equal(Now, raised[0].Time);
    }

    [Fact]
    public void Error_appends_exception_details_and_format_is_readable()
    {
        var log = new AppLog(clock: () => Now);

        log.Error("Sync", "Upload failed", new InvalidOperationException("boom"));

        var entry = Assert.Single(log.Snapshot());
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Contains("InvalidOperationException: boom", entry.Message);
        Assert.Contains("[Error  ] Sync: Upload failed", entry.Format());
    }

    [Fact]
    public void File_is_appended_and_rotated()
    {
        var directory = Path.Combine(Path.GetTempPath(), "DesktopIconGrid.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(directory, "app.log");
            var log = new AppLog(clock: () => Now);
            log.AttachFile(path, maxFileBytes: 16 * 1024);

            for (var i = 0; i < 400; i++)
            {
                log.Info("Test", new string('x', 60) + i);
            }

            Assert.True(File.Exists(path));
            Assert.True(File.Exists(Path.Combine(directory, "app.previous.log")));
            Assert.True(new FileInfo(path).Length < 16 * 1024 + 200);
            Assert.Contains("[Info   ] Test:", File.ReadAllText(path));

            log.Clear();
            Assert.Empty(log.Snapshot());
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
