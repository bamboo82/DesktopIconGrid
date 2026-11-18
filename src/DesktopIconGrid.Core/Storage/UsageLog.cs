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
/// <item>Read once, at start (<see cref="Load"/>). A file over <see cref="MaxFileBytes"/> or one that cannot be read
/// is left alone, and counting goes on in memory, with one line in the log.</item>
/// <item>Written by <see cref="Save"/> (the host calls it at most every 10 s after a change, and at exit, never on the
/// UI thread), through a temporary file, beside the other PCs' blocks as the file has them then.</item>
/// <item>A folder that cannot be written (a read-only portable copy) keeps the counts in memory for the session,
/// without a notice.</item>
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
    /// written, as for this PC's settings.
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

            var (file, readable) = Read(logProblems: true);
            if (!readable)
            {
                _memoryOnly = true; // left alone: counting goes on in memory
                return;
            }

            _memoryOnly = false;
            if (file?.Pcs is not null && file.Pcs.TryGetValue(_pc, out var block) && block is not null)
            {
                if (block.Since is { } since)
                {
                    Since = Utc(since);
                }

                foreach (var (key, entry) in block.Items ?? [])
                {
                    if (Guid.TryParse(key, out var id) && entry is { Count: > 0, Last: { } last })
                    {
                        _uses[id] = new ItemUse(entry.Count, Utc(last));
                        _entryExtras[id] = entry.ExtensionData;
                    }
                }

                _blockExtras = block.ExtensionData;
            }
            else
            {
                _dirty = true; // this PC's block begins now
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
    /// when nothing was written: nothing changed, the counts live in memory only, or the file could not be read now (a
    /// lock; tried again at the next save) or written (from then on in memory only).
    /// </summary>
    public bool Save(Func<Guid, bool> isOnBar)
    {
        ArgumentNullException.ThrowIfNull(isOnBar);
        lock (_saving)
        {
            UsageBlock block;
            string pc;
            lock (_gate)
            {
                if (!_dirty || _memoryOnly || FilePath is null || _pc is null)
                {
                    return false;
                }

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

            var (file, readable) = Read(logProblems: false);
            if (!readable)
            {
                MarkDirty();
                return false;
            }

            file ??= new UsageFile();
            file.Version = Math.Max(file.Version, FormatVersion);
            file.Pcs ??= new Dictionary<string, UsageBlock?>(StringComparer.Ordinal);
            file.Pcs[pc] = block;
            var temporary = FilePath + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(file, Json));
                File.Move(temporary, FilePath, overwrite: true);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A read-only folder (a portable copy on a locked drive): counted in memory for the session, silently.
                AppLog.Default.Debug("Usage", $"The uses could not be written, so they are kept until the program ends: {ex.Message}");
                lock (_gate)
                {
                    _memoryOnly = true;
                }

                return false;
            }
        }
    }

    private void MarkDirty()
    {
        lock (_gate)
        {
            _dirty = true;
        }
    }

    /// <summary>
    /// The file, or <see langword="null"/> when it is missing. <c>Readable</c> is false when it is there but too large,
    /// not this app's, or cannot be read now: then it is not written over.
    /// </summary>
    private (UsageFile? File, bool Readable) Read(bool logProblems)
    {
        try
        {
            var info = new FileInfo(FilePath!);
            if (!info.Exists)
            {
                return (null, true);
            }

            if (info.Length > MaxFileBytes)
            {
                Log(logProblems, $"The usage file is {info.Length} bytes, more than this app writes; it is left alone and uses are counted until the program ends");
                return (null, false);
            }

            return (JsonSerializer.Deserialize<UsageFile>(File.ReadAllBytes(FilePath!), Json), true);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            Log(logProblems, $"The usage file could not be read; it is left alone and uses are counted until the program ends: {ex.Message}");
            return (null, false);
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
