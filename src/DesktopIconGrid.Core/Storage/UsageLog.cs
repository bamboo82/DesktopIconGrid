using System.Text.Json;
using System.Text.Json.Nodes;
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
/// <item>Read once, at start (<see cref="Load"/>). A file over <see cref="MaxFileBytes"/> is not this app's: it is
/// left alone, and counting goes on in memory, with one line in the log. One that is not JSON this app can read
/// (empty, NUL bytes after a power cut, this PC's block not an object) is kept beside it as
/// <c>usage.json.corrupt-&lt;date&gt;</c>, as the layout does, and this PC counts anew (0.12.1, review 28 U2). One
/// that cannot be read just then is read again at the first save, and its counts are joined to those counted
/// meanwhile.</item>
/// <item>The file is read and written as a tree: other PCs' blocks, what a newer version wrote, and entries this
/// version cannot read go back as they were, at every level (review 28 U7).</item>
/// <item>Written by <see cref="Save"/> (the host calls it at most every 10 s after a change, and at exit, never on the
/// UI thread) through a temporary file of this instance's own, on the disk before it replaces the file, and read
/// again just before that, so a copy on another PC that wrote meanwhile keeps its block (review 28 U6). A write that
/// fails keeps the counts to be written at the next save. No file over <see cref="MaxFileBytes"/> is written: the
/// oldest uses of items gone from the bar go first (review 28 U5).</item>
/// <item>A folder that cannot be written at the start (a read-only portable copy) keeps the counts in memory for the
/// session, without a notice.</item>
/// </list>
/// </summary>
public sealed class UsageLog
{
    public const string FileName = "usage.json";

    /// <summary>A larger file is not one of this app's: it is left alone, and none is written.</summary>
    public const long MaxFileBytes = 1024 * 1024;

    /// <summary>The version written into the file.</summary>
    public const int FormatVersion = 1;

    /// <summary>How long after its last use the count of an item no longer on the bar is kept (an Undo, a sync that brings it back).</summary>
    public static readonly TimeSpan KeepRemoved = TimeSpan.FromDays(400);

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly object _gate = new();
    private readonly object _saving = new();
    private readonly Func<DateTimeOffset> _clock;
    private readonly Func<string?> _thisPc;

    /// <summary>The temporary file's own part: two copies on one portable folder never share one (review 28 U6).</summary>
    private readonly string _instance = Guid.NewGuid().ToString("N")[..12];

    private readonly Dictionary<Guid, ItemUse> _uses = [];

    /// <summary>The key each item had in the file, so it is written back under it.</summary>
    private readonly Dictionary<Guid, string> _keys = [];

    /// <summary>This PC's block as read: what it holds besides the counts read here, written back with them.</summary>
    private JsonObject? _block;

    private DateTimeOffset _since;
    private string? _pc;
    private bool _memoryOnly;
    private bool _dirty;

    /// <summary>The file could not be read at the start: read again, and joined, at the first save.</summary>
    private bool _readAgain;

    private bool _tooLargeSaid;
    private bool _unexpectedSaid;

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
        _since = _clock();
        _memoryOnly = filePath is null;
    }

    public string? FilePath { get; }

    /// <summary>The largest file read or written: <see cref="MaxFileBytes"/>, smaller in tests.</summary>
    internal long MaxBytes { get; init; } = MaxFileBytes;

    /// <summary>Runs after the temporary file is written and before it replaces the file (tests: another copy writes then).</summary>
    internal Action? BeforeReplace { get; set; }

    /// <summary>This instance's temporary file.</summary>
    internal string TemporaryPath => $"{FilePath}.{_instance}.tmp";

    /// <summary>When counting began on this PC: the first start of 0.12.0 here (or of this run, in memory only).</summary>
    public DateTimeOffset Since
    {
        get
        {
            lock (_gate)
            {
                return _since; // a save or a join sets it on another thread (review 28 U7)
            }
        }
    }

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
    /// read-only portable copy, silently) or the file is over <see cref="MaxFileBytes"/> (left alone); a file that is
    /// not JSON this app can read is kept beside it and counting starts anew; one that cannot be read just now (a
    /// scanner, a sync client) is read again at the first save.
    /// </summary>
    public void Load()
    {
        lock (_gate)
        {
            _uses.Clear();
            _keys.Clear();
            _block = null;
            _since = _clock();
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
            _dirty = true; // written at the first save unless the file has this PC's block as it is
            var read = Read(logProblems: true);
            switch (read.State)
            {
                case FileState.TooLarge:
                    _memoryOnly = true; // left alone: counting goes on in memory
                    return;
                case FileState.Unreadable:
                    _readAgain = true; // counted in memory meanwhile, and joined to the file's at the first save
                    return;
                case FileState.Corrupt:
                    KeepAside(move: true); // a new file is written at the first save
                    return;
                case FileState.Read:
                    switch (BlockOf(read.Root!))
                    {
                        case JsonObject block:
                            Adopt(block, merge: false);
                            _dirty = false;
                            return;
                        case null:
                            return; // this PC's block begins now
                        default:
                            KeepAside(move: false); // this PC's block is not one: kept beside it, the others are written back
                            return;
                    }

                default:
                    return; // no file yet
            }
        }
    }

    /// <summary>Counts one use of the item <paramref name="id"/>, now.</summary>
    public void Count(Guid id)
    {
        lock (_gate)
        {
            var now = _clock();
            _uses[id] = _uses.TryGetValue(id, out var use)
                ? new ItemUse(use.Count == int.MaxValue ? use.Count : use.Count + 1, use.Last > now ? use.Last : now) // a clock set back does not move it back (U7)
                : new ItemUse(1, now);
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

            string pc;
            lock (_gate)
            {
                var now = _clock();
                foreach (var gone in _uses.Where(use => !isOnBar(use.Key) && now - use.Value.Last > KeepRemoved).Select(use => use.Key).ToList())
                {
                    Forget(gone);
                }

                pc = _pc;
                _dirty = false;
            }

            // Whatever ends this save, the counts stay to be written unless the file was replaced (0.12.2, review 29 N5:
            // only IOException and UnauthorizedAccessException set them back, so anything else lost them until the next use).
            var written = false;
            try
            {
                written = Write(pc, isOnBar);
                return written;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Delete(TemporaryPath);
                if (!_unexpectedSaid)
                {
                    _unexpectedSaid = true;
                    AppLog.Default.Warning("Usage", $"The uses could not be written ({ex.GetType().Name}): {ex.Message}; they are tried again at the next save");
                }

                return false;
            }
            finally
            {
                if (!written)
                {
                    MarkDirty();
                }
            }
        }
    }

    /// <summary>
    /// Reads the file, joins this PC's block to it and replaces it through this instance's temporary file. False when
    /// it could not be read or written just now; the caller keeps the counts to be written then.
    /// </summary>
    private bool Write(string pc, Func<Guid, bool> isOnBar)
    {
        var keptAside = false;
        for (var attempt = 0; ; attempt++)
        {
            var read = Read(logProblems: false);
            if (read.State is FileState.Unreadable or FileState.TooLarge)
            {
                return false; // locked just now, or not this app's: never written over, tried again at the next save
            }

            if (read.State == FileState.Corrupt && !keptAside)
            {
                if (!KeepAside(move: false))
                {
                    return false; // not written over before a copy of it is kept
                }

                keptAside = true;
            }

            byte[] bytes;
            lock (_gate)
            {
                bytes = Compose(read.State == FileState.Read ? read.Root! : new JsonObject(), pc, isOnBar);
            }

            if (bytes.Length > MaxBytes)
            {
                TooLarge(bytes.Length);
                return false;
            }

            var temporary = TemporaryPath;
            try
            {
                using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes);
                    stream.Flush(flushToDisk: true); // on the disk before it replaces the file (U2: NUL bytes after a power cut)
                }

                BeforeReplace?.Invoke();
                if (attempt == 0 && Stamp() != read.Stamp)
                {
                    Delete(temporary); // another copy wrote meanwhile: its block is read again, once
                    continue;
                }

                File.Move(temporary, FilePath!, overwrite: true);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Locked by a scanner or a sync client for a moment: written at the next save, or at exit.
                Delete(temporary);
                AppLog.Default.Debug("Usage", $"The uses could not be written yet: {ex.Message}");
                return false;
            }
        }
    }

    /// <summary>
    /// The first save after a start whose read failed: the file's block of this PC joined to what was counted since. False
    /// when it still cannot be read (then counting goes on in memory only, with one line in the log) or is over the
    /// limit.
    /// </summary>
    private bool ReadAgain()
    {
        var read = Read(logProblems: true);
        lock (_gate)
        {
            _readAgain = false;
            switch (read.State)
            {
                case FileState.Unreadable or FileState.TooLarge:
                    _memoryOnly = true;
                    AppLog.Default.Info("Usage", "The usage file still cannot be read; it is left alone and uses are counted until the program ends");
                    return false;
                case FileState.Read when BlockOf(read.Root!) is JsonObject block:
                    Adopt(block, merge: true);
                    return true;
                default:
                    return true; // none yet, or one the save keeps beside it
            }
        }
    }

    /// <summary>This PC's block in <paramref name="root"/>: an object, <see langword="null"/> when there is none, or what it is instead.</summary>
    private JsonNode? BlockOf(JsonObject root) => root["pcs"] is JsonObject pcs ? pcs[_pc!] : null;

    /// <summary>
    /// Takes this PC's block as read: its counts (or, with <paramref name="merge"/>, those added to the ones counted
    /// since the start) and the rest as it is. An entry this version cannot read (an id that is no id, no count, no last
    /// use) stays in the block and is written back unchanged.
    /// </summary>
    private void Adopt(JsonObject block, bool merge)
    {
        if (DateOf(block["since"]) is { } since)
        {
            _since = since;
        }

        if (block["items"] is JsonObject items)
        {
            foreach (var (key, node) in items)
            {
                if (!Guid.TryParse(key, out var id) || node is not JsonObject entry || CountOf(entry["count"]) is not { } count || DateOf(entry["last"]) is not { } last)
                {
                    continue;
                }

                _uses[id] = merge && _uses.TryGetValue(id, out var counted)
                    ? new ItemUse((int)Math.Min(int.MaxValue, (long)count + counted.Count), counted.Last > last ? counted.Last : last)
                    : new ItemUse(count, last);
                _keys[id] = key;
            }
        }

        _block = (JsonObject)block.DeepClone();
    }

    /// <summary>Drops an item's uses, here and in the block written back.</summary>
    private void Forget(Guid id)
    {
        _uses.Remove(id);
        if (_keys.Remove(id, out var key) && _block?["items"] is JsonObject items)
        {
            items.Remove(key);
        }
    }

    /// <summary>
    /// The file with this PC's block as counted here and the rest of <paramref name="root"/> as it was. Over the limit,
    /// the oldest uses of items gone from the bar are dropped until it fits (review 28 U5).
    /// </summary>
    private byte[] Compose(JsonObject root, string pc, Func<Guid, bool> isOnBar)
    {
        var version = root["version"] is JsonValue value && value.TryGetValue<int>(out var number) ? number : 0;
        root["version"] = Math.Max(version, FormatVersion);
        if (root["pcs"] is not JsonObject pcs)
        {
            pcs = [];
            root["pcs"] = pcs;
        }

        pcs[pc] = Block();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(root, Json);
        var gone = new Queue<Guid>(_uses.Where(use => !isOnBar(use.Key)).OrderBy(use => use.Value.Last).Select(use => use.Key));
        while (bytes.Length > MaxBytes && gone.Count > 0)
        {
            // About the bytes each entry takes, so the file is written again only a few times.
            var over = bytes.Length - MaxBytes;
            for (long freed = 0; freed < over && gone.Count > 0;)
            {
                var id = gone.Dequeue();
                freed += 96 + (_keys.GetValueOrDefault(id) ?? id.ToString("D")).Length;
                Forget(id);
            }

            pcs[pc] = Block();
            bytes = JsonSerializer.SerializeToUtf8Bytes(root, Json);
        }

        return bytes;
    }

    /// <summary>This PC's block: what it held as read, with the counts and the start of counting of here.</summary>
    private JsonObject Block()
    {
        var block = _block?.DeepClone() as JsonObject ?? [];
        block["since"] = _since.UtcDateTime;
        if (block["items"] is not JsonObject items)
        {
            items = [];
            block["items"] = items;
        }

        foreach (var (id, use) in _uses)
        {
            var key = _keys.GetValueOrDefault(id) ?? id.ToString("D");
            if (items[key] is not JsonObject entry)
            {
                entry = [];
                items[key] = entry;
            }

            entry["count"] = use.Count;
            entry["last"] = use.Last.UtcDateTime;
        }

        return block;
    }

    private void TooLarge(int length)
    {
        if (!_tooLargeSaid)
        {
            _tooLargeSaid = true;
            AppLog.Default.Warning("Usage", $"The uses would take {length} bytes, more than {MaxBytes}; they are not written while that lasts");
        }
    }

    /// <summary>
    /// Whether the file's folder takes a file at all, with a probe the system deletes when it is closed, as
    /// <see cref="DataLocation"/>'s: a delete that fails is not "cannot write", and no probe is left behind (review 28 U4).
    /// </summary>
    private bool CanWriteFolder()
    {
        var folder = Path.GetDirectoryName(FilePath!)!;
        try
        {
            Directory.CreateDirectory(folder);
            using (new FileStream(Path.Combine(folder, $"{FileName}.{Guid.NewGuid():N}.probe"), FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose))
            {
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Keeps the file beside it as <c>usage.json.corrupt-&lt;date&gt;</c>, as the layout does: moved when a new file
    /// takes its place, copied when the rest of it is written back. False when that could not be done.
    /// </summary>
    private bool KeepAside(bool move)
    {
        var aside = $"{FilePath}.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}";
        try
        {
            if (move)
            {
                File.Move(FilePath!, aside);
            }
            else
            {
                File.Copy(FilePath!, aside);
            }

            AppLog.Default.Info("Usage", $"The usage file could not be read as this app's; it is kept as {Path.GetFileName(aside)} and this PC counts anew");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Default.Debug("Usage", $"The usage file could not be kept as {Path.GetFileName(aside)}: {ex.Message}");
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

        /// <summary>Over the limit: not this app's, left alone for good.</summary>
        TooLarge,

        /// <summary>Not JSON this app can read: kept beside it, and a new one written.</summary>
        Corrupt,

        /// <summary>There, but it could not be read just now (a lock).</summary>
        Unreadable,
    }

    /// <summary>A read: what it found, the tree when it is the app's, and the file's length and time then.</summary>
    private readonly record struct ReadResult(FileState State, JsonObject? Root, (long Length, DateTime Written) Stamp);

    private (long Length, DateTime Written) Stamp()
    {
        var info = new FileInfo(FilePath!);
        return info.Exists ? (info.Length, info.LastWriteTimeUtc) : default;
    }

    private ReadResult Read(bool logProblems)
    {
        (long Length, DateTime Written) stamp = default;
        try
        {
            var info = new FileInfo(FilePath!);
            if (!info.Exists)
            {
                return new ReadResult(FileState.Missing, null, default);
            }

            stamp = (info.Length, info.LastWriteTimeUtc);
            if (info.Length > MaxBytes)
            {
                Log(logProblems, $"The usage file is {info.Length} bytes, more than this app writes; it is left alone and uses are counted until the program ends");
                return new ReadResult(FileState.TooLarge, null, stamp);
            }

            if (JsonNode.Parse(File.ReadAllBytes(FilePath!)) is JsonObject root && root["pcs"] is null or JsonObject)
            {
                Walk(root); // every object built now, so a property named twice is found here, not later
                return new ReadResult(FileState.Read, root, stamp);
            }

            Log(logProblems, "The usage file is not the JSON this app writes");
            return new ReadResult(FileState.Corrupt, null, stamp);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException or FormatException)
        {
            Log(logProblems, $"The usage file is not the JSON this app writes: {ex.Message}");
            return new ReadResult(FileState.Corrupt, null, stamp);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log(logProblems, $"The usage file could not be read just now: {ex.Message}");
            return new ReadResult(FileState.Unreadable, null, default);
        }
    }

    private static void Walk(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject members:
                foreach (var (_, member) in members)
                {
                    Walk(member);
                }

                break;
            case JsonArray items:
                foreach (var item in items)
                {
                    Walk(item);
                }

                break;
        }
    }

    private static int? CountOf(JsonNode? node) => node is JsonValue value && value.TryGetValue<int>(out var count) && count > 0 ? count : null;

    private static DateTimeOffset? DateOf(JsonNode? node) => node is JsonValue value && value.TryGetValue<DateTime>(out var time) ? Utc(time) : null;

    private static void Delete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Default.Debug("Usage", $"{Path.GetFileName(path)} could not be deleted: {ex.Message}");
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
}

/// <summary>What counts as a use, and what "not used in 90 days" means (0.12.0 WP4).</summary>
public static class UsageRules
{
    /// <summary>How long without a use makes a shortcut "not used in 90 days".</summary>
    public static readonly TimeSpan UnusedAfter = TimeSpan.FromDays(90);

    /// <summary>
    /// Whether a use is counted: a shortcut, started or brought forward. A failed or cancelled start (an approval or a
    /// question cancelled) is none, nor is a click that minimised the program in front (0.12.2, review 29 N2); special
    /// icons, groups and separators have no count.
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
