using System.Globalization;
using System.Text.Json;
using DesktopIconGrid.Core.Models;

namespace DesktopIconGrid.Core.Storage;

/// <summary>One kept copy of the layout: when it was saved, and what it holds.</summary>
public sealed record LayoutHistoryEntry(string FilePath, DateTimeOffset SavedAt, int Shortcuts, int Groups);

/// <summary>
/// Earlier layouts, kept in <c>history\</c> next to <c>layout.json</c> so a change can be undone later: before a save
/// overwrites the file, the file as it was is copied there, at most once per <see cref="Interval"/> (a session of
/// dragging icons is one entry, not fifty), and only the newest <see cref="MaxEntries"/> are kept. The history stays on
/// this PC and is never synced.
/// </summary>
public sealed class LayoutHistory(string directory, Func<DateTimeOffset>? clock = null)
{
    public const int MaxEntries = 20;

    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);

    private const string Prefix = "layout-";
    private const string Format = "yyyyMMdd-HHmmss-fff";

    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);

    public string Directory { get; } = directory ?? throw new ArgumentNullException(nameof(directory));

    /// <summary>
    /// Keeps <paramref name="layoutFile"/> as it is now, unless the newest entry is younger than <see cref="Interval"/>
    /// (always with <paramref name="force"/>, as before a restore), then drops the entries beyond <see cref="MaxEntries"/>.
    /// A file that is not there yet, or a copy that fails, keeps nothing: the history never stops a save.
    /// </summary>
    public void Record(string layoutFile, bool force = false)
    {
        ArgumentNullException.ThrowIfNull(layoutFile);
        try
        {
            if (!File.Exists(layoutFile))
            {
                return;
            }

            var now = _clock();
            var newest = Files().FirstOrDefault();
            if (!force && newest.Path is not null && now - newest.SavedAt < Interval)
            {
                return;
            }

            System.IO.Directory.CreateDirectory(Directory);
            var target = Path.Combine(Directory, Prefix + now.UtcDateTime.ToString(Format, CultureInfo.InvariantCulture) + ".json");
            File.Copy(layoutFile, target, overwrite: true);
            foreach (var old in Files().Skip(MaxEntries))
            {
                File.Delete(old.Path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Diagnostics.AppLog.Default.Warning("Layout", $"An earlier layout could not be kept in {Directory}: {ex.Message}");
        }
    }

    /// <summary>The kept layouts, newest first, with what each holds; one that cannot be read is left out.</summary>
    public IReadOnlyList<LayoutHistoryEntry> List()
    {
        var entries = new List<LayoutHistoryEntry>();
        foreach (var (path, savedAt) in Files())
        {
            if (Read(path) is { } layout)
            {
                var shortcuts = layout.Items.OfType<ShortcutItem>().Count() + layout.Items.OfType<GroupItem>().Sum(g => g.Items.OfType<ShortcutItem>().Count());
                entries.Add(new LayoutHistoryEntry(path, savedAt, shortcuts, layout.Items.OfType<GroupItem>().Count()));
            }
        }

        return entries;
    }

    /// <summary>The layout a kept file holds, or <see langword="null"/> when it cannot be read.</summary>
    public static LauncherLayout? Read(string filePath)
    {
        try
        {
            var layout = JsonSerializer.Deserialize<LauncherLayout>(File.ReadAllText(filePath), LayoutStorage.JsonOptions);
            if (layout is not null)
            {
                layout.Items ??= [];
                layout.Version = LauncherLayout.FormatOf(layout.Version);
            }

            return layout;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>The kept files and when each was saved (from its name), newest first.</summary>
    private List<(string Path, DateTimeOffset SavedAt)> Files()
    {
        if (!System.IO.Directory.Exists(Directory))
        {
            return [];
        }

        var files = new List<(string, DateTimeOffset)>();
        foreach (var path in System.IO.Directory.EnumerateFiles(Directory, Prefix + "*.json"))
        {
            var stamp = Path.GetFileNameWithoutExtension(path)[Prefix.Length..];
            if (DateTime.TryParseExact(stamp, Format, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var savedAt))
            {
                files.Add((path, new DateTimeOffset(savedAt, TimeSpan.Zero)));
            }
        }

        return files.OrderByDescending(f => f.Item2).ToList();
    }
}
