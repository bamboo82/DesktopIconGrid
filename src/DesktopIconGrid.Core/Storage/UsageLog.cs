using System.Text.Json;
using System.Text.Json.Serialization;
using DesktopIconGrid.Core.Diagnostics;
using DesktopIconGrid.Core.Launching;
using DesktopIconGrid.Core.Models;

namespace DesktopIconGrid.Core.Storage;

/// <summary>One shortcut's uses on this PC: how many, and the last one.</summary>
public sealed record ItemUse(int Count, DateTimeOffset Last);

/// <summary>
/// How often each shortcut was started or brought forward by the bar on this PC, and when last (0.12.0 WP4). Kept in
/// <see cref="FileName"/> next to the layout, one block per PC by <see cref="ThisDevice.Id"/>, as <c>this-pc.json</c>
/// keeps its settings, so a portable copy counts each PC on its own. It stays on this PC: sync, restore, import,
/// layout history and export read and write their own files by name, and this is none of them. Only item ids and
/// times (UTC) are stored, no path, name or parameter.
/// <list type="bullet">
/// <item>Read once, at start (<see cref="Load"/>). A file over <see cref="MaxFileBytes"/> or one that is not this
/// app's is left alone, and counting goes on in memory, with one line in the log. One that cannot be read just then is
/// read again at the first save, and its counts are joined to those counted meanwhile.</item>
/// <item>Written by <see cref="Save"/> (the host calls it at most every 10 s after a change, and at exit, never on the
/// UI thread), through a temporary file, beside the other PCs' blocks as the file has them then. A write that fails
/// keeps the counts to be written at the next save (release check U1).</item>
/// <item>A folder that cannot be written at the start (a read-only portable copy) keeps the counts in memory for the
/// session, without a notice.</item>
/// </list>
/// </summary>
public sealed class UsageLog
{
    public const string FileName = "usage.json";

    /// <summary>A larger file is not one of this app's: it is left alone.</summary>
    public const long MaxFileBytes = 1024 * 1024;

    /// <summary>The version written into the file.</summary>
    public const int FormatVersion = 1;

    /// <summary>How long after its last use the count of an item no longer on the bar is kept (an Undo, a sync that brings it back).</summary>
    public static readonly TimeSpan KeepRemoved = TimeSpan.FromDays(400);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly object _gate = new();
    private readonly object _saving = new();
    private readonly Func<DateTimeOffset> _clock;
    private readonly Func<string?> _thisPc;
    private readonly Dictionary<Guid, ItemUse> _uses = [];
    private Dictionary<Guid, Dictionary<string, JsonElement>?> _entryExtras = [];
    private Dictionary<string, JsonElement>? _blockExtras;
    private string? _pc;
    private bool _memoryOnly;
    private bool _dirty;

    /// <summary>The file could not be read at the start: read again, and joined, at the first save.</summary>
    private bool _readAgain;

    /// <param name="filePath">The file; <see langword="null"/> counts in memory only (tests, and a bar without a data folder).</param>
    /// <param name="clock">The time; UTC now by default.</param>
    /// <param name="thisPc">
    /// This PC's key, or <see langword="null"/> when it is not known; by default <see cref="ThisDevice.Id"/> when it is
    /// this machine's own (<see cref="ThisDevice.IsOfThisMachine"/>).
    /// </param>
    public UsageLog(string? filePath, Func<DateTimeOffset>? clock = null, Func<string?>? thisPc = null)
    {
        FilePath = filePath;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _thisPc = thisPc ?? (() => ThisDevice.IsOfThisMachine && !string.IsNullOrWhiteSpace(ThisDevice.Id) ? ThisDevice.Id : null);
        Since = _clock();
        _memoryOnly = filePath is null;
    }

    public string? FilePath { get; }

    /// <summary>When counting began on this PC: the first start of 0.12.0 here (or of this run, in memory only).</summary>
    public DateTimeOffset Since { get; private set; }

    /// <summary>Whether the counts live in memory only for this run.</summary>
    public bool IsMemoryOnly
    {
        get
        {
            lock (_gate)
            {
                return _memoryOnly;
            }
        }
    }

    /// <summary>Raised on the counting thread after a use was counted, so the host saves within its interval.</summary>
    public event EventHandler? Changed;

    public static string GetDefaultFilePath() => Path.Combine(Path.GetDirectoryName(LayoutStorage.GetDefaultFilePath())!, FileName);

    /// <summary>
    /// Reads this PC's block. A PC without one starts counting now; its block, with <see cref="Since"/>, is written at
    /// the next save. Without this PC's own id (<see cref="ThisDevice.IsOfThisMachine"/> false) nothing is read or
    /// written, as for this PC's settings. Counting goes on in memory only when the folder cannot be written (a
    /// read-only portable copy, silently) or the file is not this app's (left alone); a file that cannot be read just
    /// now (a scanner, a sync client) is read again at the first save (release check U1).
    /// </summary>
    public void Load()
    {
        lock (_gate)
        {
            _uses.Clear();
            _entryExtras = [];
            _blockExtras = null;
            Since = _clock();
            _dirty = false;
            _readAgain = false;
            _pc = _thisPc() is { } pc && !string.IsNullOrWhiteSpace(pc) ? pc : null;
            if (FilePath is null)
            {
                _memoryOnly = true;
                return;
            }

            if (_pc is null)
            {
                _memoryOnly = true;
                AppLog.Default.Info("Usage", "This PC's own id could not be read: uses are counted until the program ends");
                return;
            }

            if (!CanWriteFolder())
            {
                _memoryOnly = true; // a read-only folder: counted for the session, silently
                AppLog.Default.Debug("Usage", "The data folder cannot be written: uses are counted until the program ends");
                return;
            }

            _memoryOnly = false;
            var (state, file) = Read(logProblems: true);
            switch (state)
            {
                case FileState.NotOurs:
                    _memoryOnly = true; // left alone: counting goes on in memory
                    return;
                case FileState.Unreadable:
                    _readAgain = true; // counted in memory meanwhile, and joined to the file's at the first save
                    _dirty = true;
                    return;
                case FileState.Read when file?.Pcs is not null && file.Pcs.TryGetValue(_pc, out var block) && block is not null:
                    Adopt(block, merge: false);
                    return;
                default:
                    _dirty = true; // this PC's block begins now
                    return;
            }
        }
    }

    /// <summary>Counts one use of the item <paramref name="id"/>, now.</summary>
    public void Count(Guid id)
    {
        lock (_gate)
        {
            var count = _uses.TryGetValue(id, out var use) ? use.Count : 0;
            _uses[id] = new ItemUse(count == int.MaxValue ? count : count + 1, _clock());
            _dirty = true;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The uses of the item <paramref name="id"/>, or <see langword="null"/> when it was not used on this PC.</summary>
    public ItemUse? Of(Guid id)
    {
        lock (_gate)
        {
            return _uses.GetValueOrDefault(id);
        }
    }

    /// <summary>Every item's uses, as they are now.</summary>
    public IReadOnlyDictionary<Guid, ItemUse> Snapshot()
    {
        lock (_gate)
        {
            return new Dictionary<Guid, ItemUse>(_uses);
        }
    }

    /// <summary>
    /// Writes this PC's block when something changed. First the uses of items no longer on the bar
    /// (<paramref name="isOnBar"/> false) whose last use is more than <see cref="KeepRemoved"/> ago are dropped. False
    /// when nothing was written: nothing changed, the counts live in memory only, or the file could not be read or
    /// written just now. Then the counts stay to be written at the next save and at exit (release check U1).
    /// </summary>
    public bool Save(Func<Guid, bool> isOnBar)
    {
        ArgumentNullException.ThrowIfNull(isOnBar);
        lock (_saving)
        {
            bool readAgain;
            lock (_gate)
            {
                if (!_dirty || _memoryOnly || FilePath is null || _pc is null)
                {
                    return false;
                }

                readAgain = _readAgain;
            }

            if (readAgain && !ReadAgain())
            {
                return false;
            }

            UsageBlock block;
            string pc;
            lock (_gate)
            {
                var now = _clock();
                foreach (var gone in _uses.Where(use => !isOnBar(use.Key) && now - use.Value.Last > KeepRemoved).Select(use => use.Key).ToList())
                {
                    _uses.Remove(gone);
                    _entryExtras.Remove(gone);
                }

                block = new UsageBlock
                {
                    Since = Since.UtcDateTime,
                    Items = _uses.ToDictionary(
                        use => use.Key.ToString("D"),
                        use => (UsageEntry?)new UsageEntry { Count = use.Value.Count, Last = use.Value.Last.UtcDateTime, ExtensionData = _entryExtras.GetValueOrDefault(use.Key) },
                        StringComparer.Ordinal),
                    ExtensionData = _blockExtras,
                };
                pc = _pc;
                _dirty = false;
            }

            var (state, file) = Read(logProblems: false);
            if (state is FileState.Unreadable or FileState.NotOurs)
            {
                MarkDirty(); // locked, or replaced, just now: never overwritten, tried again at the next save
                return false;
            }

            file ??= new UsageFile();
            file.Version = Math.Max(file.Version, FormatVersion);
            file.Pcs ??= new Dictionary<string, UsageBlock?>(StringComparer.Ordinal);
            file.Pcs[pc] = block;
            var temporary = FilePath + ".tmp";
            try
            {
                File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(file, Json));
                File.Move(temporary, FilePath, overwrite: true);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Locked by a scanner or a sync client for a moment: written at the next save, or at exit.
                AppLog.Default.Debug("Usage", $"The uses could not be written yet: {ex.Message}");
                MarkDirty();
                return false;
            }
        }
    }

    /// <summary>
    /// The first save after a start whose read failed: the file's block of this PC joined to what was counted since. False
    /// when it still cannot be read (then counting goes on in memory only, with one line in the log) or is not this
    /// app's.
    /// </summary>
    private bool ReadAgain()
    {
        var (state, file) = Read(logProblems: true);
        lock (_gate)
        {
            _readAgain = false;
            switch (state)
            {
                case FileState.Unreadable or FileState.NotOurs:
                    _memoryOnly = true;
                    AppLog.Default.Info("Usage", "The usage file still cannot be read; it is left alone and uses are counted until the program ends");
                    return false;
                case FileState.Read when file?.Pcs is not null && file.Pcs.TryGetValue(_pc!, out var block) && block is not null:
                    Adopt(block, merge: true);
                    return true;
                default:
                    return true;
            }
        }
    }

    /// <summary>Takes this PC's block as read: as the counts, or (<paramref name="merge"/>) added to those counted since the start.</summary>
    private void Adopt(UsageBlock block, bool merge)
    {
        if (block.Since is { } since)
        {
            Since = Utc(since);
        }

        foreach (var (key, entry) in block.Items ?? [])
        {
            if (!Guid.TryParse(key, out var id) || entry is not { Count: > 0, Last: { } last })
            {
                continue;
            }

            var read = new ItemUse(entry.Count, Utc(last));
            _uses[id] = merge && _uses.TryGetValue(id, out var counted)
                ? new ItemUse((int)Math.Min(int.MaxValue, (long)read.Count + counted.Count), counted.Last > read.Last ? counted.Last : read.Last)
                : read;
            _entryExtras[id] = entry.ExtensionData;
        }

        _blockExtras = block.ExtensionData;
    }

    /// <summary>Whether the file's folder takes a file at all, tried with a probe that is deleted at once.</summary>
    private bool CanWriteFolder()
    {
        var folder = Path.GetDirectoryName(FilePath!)!;
        var probe = Path.Combine(folder, $"{FileName}.{Guid.NewGuid():N}.probe");
        try
        {
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(probe, []);
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void MarkDirty()
    {
        lock (_gate)
        {
            _dirty = true;
        }
    }

    /// <summary>What a read of the file found.</summary>
    private enum FileState
    {
        Missing,
        Read,

        /// <summary>Too large, or not this app's JSON: left alone for good.</summary>
        NotOurs,

        /// <summary>There, but it could not be read just now (a lock).</summary>
        Unreadable,
    }

    /// <summary>The file, if it is there and this app's.</summary>
    private (FileState State, UsageFile? File) Read(bool logProblems)
    {
        try
        {
            var info = new FileInfo(FilePath!);
            if (!info.Exists)
            {
                return (FileState.Missing, null);
            }

            if (info.Length > MaxFileBytes)
            {
                Log(logProblems, $"The usage file is {info.Length} bytes, more than this app writes; it is left alone and uses are counted until the program ends");
                return (FileState.NotOurs, null);
            }

            return (FileState.Read, JsonSerializer.Deserialize<UsageFile>(File.ReadAllBytes(FilePath!), Json));
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
        {
            Log(logProblems, $"The usage file is not this app's; it is left alone and uses are counted until the program ends: {ex.Message}");
            return (FileState.NotOurs, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log(logProblems, $"The usage file could not be read just now: {ex.Message}");
            return (FileState.Unreadable, null);
        }
    }

    private static void Log(bool once, string message)
    {
        if (once)
        {
            AppLog.Default.Info("Usage", message);
        }
    }

    private static DateTimeOffset Utc(DateTime time) => time.Kind switch
    {
        DateTimeKind.Utc => new DateTimeOffset(time, TimeSpan.Zero),
        DateTimeKind.Local => new DateTimeOffset(time.ToUniversalTime(), TimeSpan.Zero),
        _ => new DateTimeOffset(DateTime.SpecifyKind(time, DateTimeKind.Utc), TimeSpan.Zero),
    };

    /// <summary>The file: each PC's block by its id, and what a newer version wrote.</summary>
    private sealed class UsageFile
    {
        public int Version { get; set; } = FormatVersion;

        public Dictionary<string, UsageBlock?>? Pcs { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtensionData { get; set; }
    }

    private sealed class UsageBlock
    {
        public DateTime? Since { get; set; }

        public Dictionary<string, UsageEntry?>? Items { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtensionData { get; set; }
    }

    private sealed class UsageEntry
    {
        public int Count { get; set; }

        public DateTime? Last { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtensionData { get; set; }
    }
}

/// <summary>What counts as a use, and what "not used in 90 days" means (0.12.0 WP4).</summary>
public static class UsageRules
{
    /// <summary>How long without a use makes a shortcut "not used in 90 days".</summary>
    public static readonly TimeSpan UnusedAfter = TimeSpan.FromDays(90);

    /// <summary>
    /// Whether a use is counted: a shortcut, started or brought forward. A failed or cancelled start (an approval or a
    /// question cancelled) is none; special icons, groups and separators have no count.
    /// </summary>
    public static bool IsCounted(LauncherItem item, LaunchOutcome outcome) => item is ShortcutItem && outcome == LaunchOutcome.Started;

    /// <summary>
    /// Whether a shortcut is "not used in 90 days": its last use is longer ago, or it was never used while counting has
    /// run for that long on this PC. Before that a shortcut never used is not listed: nobody knows yet.
    /// </summary>
    public static bool IsUnused(ItemUse? use, DateTimeOffset since, DateTimeOffset now) =>
        use is null ? now - since >= UnusedAfter : now - use.Last > UnusedAfter;

    /// <summary>When "not used in 90 days" is complete: counting has run that long.</summary>
    public static DateTimeOffset CompleteFrom(DateTimeOffset since) => since + UnusedAfter;
}

/// <summary>How the names table words a last use.</summary>
public enum LastUseKind
{
    Never,
    Today,
    Yesterday,
    DaysAgo,
    Date,
}

/// <summary>A last use as the names table words it: today, yesterday, so many days ago up to 30, then its date, in local time.</summary>
/// <param name="Kind">Which wording.</param>
/// <param name="Days">Calendar days between the use and today, in local time.</param>
/// <param name="Local">The use in local time (for the date and the tooltip).</param>
public readonly record struct LastUse(LastUseKind Kind, int Days, DateTime Local)
{
    /// <summary>The wording for <paramref name="last"/> seen at <paramref name="now"/> in the time zone <paramref name="zone"/>.</summary>
    public static LastUse Of(DateTimeOffset? last, DateTimeOffset now, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        if (last is not { } when)
        {
            return new LastUse(LastUseKind.Never, 0, default);
        }

        var local = TimeZoneInfo.ConvertTime(when, zone).DateTime;
        var days = (TimeZoneInfo.ConvertTime(now, zone).Date - local.Date).Days;
        return days switch
        {
            <= 0 => new LastUse(LastUseKind.Today, 0, local), // also a use the clock puts after now
            1 => new LastUse(LastUseKind.Yesterday, 1, local),
            <= 30 => new LastUse(LastUseKind.DaysAgo, days, local),
            _ => new LastUse(LastUseKind.Date, days, local),
        };
    }
}
