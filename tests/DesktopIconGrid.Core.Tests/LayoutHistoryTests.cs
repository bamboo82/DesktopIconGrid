using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Core.Storage;

namespace DesktopIconGrid.Core.Tests;

/// <summary>Layout history: earlier layouts kept before a save, one per ten minutes, the newest twenty.</summary>
public sealed class LayoutHistoryTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("dig-history-").FullName;
    private DateTimeOffset _now = new(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private LayoutStorage Storage() => new(Path.Combine(_folder, "layout.json"), () => _now);

    private static LauncherLayout Layout(params string[] names) => new()
    {
        Items = [.. names.Select(n => (LauncherItem)new ShortcutItem { Name = n, Location = $@"C:\{n}.exe" }), new GroupItem { Name = "G", Items = [new ShortcutItem { Name = "Inner", Location = "x" }] }],
    };

    [Fact]
    public void A_save_keeps_the_layout_it_replaces_once_per_ten_minutes()
    {
        var storage = Storage();
        storage.Save(Layout("a")); // nothing to keep yet
        Assert.Empty(storage.History.List());

        storage.Save(Layout("a", "b")); // keeps "a"
        _now += TimeSpan.FromMinutes(3);
        storage.Save(Layout("a", "b", "c")); // too soon: nothing kept
        _now += TimeSpan.FromMinutes(10);
        storage.Save(Layout("d")); // keeps a, b, c

        var entries = storage.History.List();
        Assert.Equal([(4, 1), (2, 1)], entries.Select(e => (e.Shortcuts, e.Groups)));
        Assert.Equal(_now, entries[0].SavedAt);
        Assert.Equal(["a", "b", "c", "G"], LayoutHistory.Read(entries[0].FilePath)!.Items.Select(i => i.Name));
    }

    [Fact]
    public void Only_the_newest_twenty_are_kept_and_a_forced_record_ignores_the_interval()
    {
        var storage = Storage();
        storage.Save(Layout("first"));
        for (var i = 0; i < 25; i++)
        {
            _now += TimeSpan.FromSeconds(1);
            storage.History.Record(storage.FilePath, force: true);
        }

        Assert.Equal(LayoutHistory.MaxEntries, storage.History.List().Count);
    }

    [Fact]
    public void A_kept_file_that_cannot_be_read_is_left_out()
    {
        var storage = Storage();
        storage.Save(Layout("a"));
        storage.History.Record(storage.FilePath, force: true);
        File.WriteAllText(Path.Combine(storage.History.Directory, "layout-20261110-080000-000.json"), "{ not json");
        File.WriteAllText(Path.Combine(storage.History.Directory, "notes.json"), "{}");

        Assert.Single(storage.History.List());
    }
}
