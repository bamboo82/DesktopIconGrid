using System.Text.Json;
using DesktopIconGrid.Core.Diagnostics;
using DesktopIconGrid.Core.Models;

namespace DesktopIconGrid.Core.SpecialIcons;

/// <summary>
/// The command tiles approved to run on this PC, by <see cref="CommandTileSettings.Hash"/> and <see cref="ThisDevice.Id"/>.
/// Kept in a file of the local data folder that sync never uploads, so a tile that arrives with a copy from another PC
/// (or from a restored or imported layout) stays paused until someone approves it here. Saving the tile's properties
/// on this PC approves what was saved.
/// </summary>
public sealed class CommandTrust
{
    public const string FileName = "command-trust.json";

    /// <summary>The most approvals kept; the oldest go first.</summary>
    public const int MaxEntries = 500;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly object _gate = new();
    private readonly string? _filePath;
    private readonly Func<DateTimeOffset> _clock;
    private List<Entry> _entries;

    /// <summary>Approved while the device id is not this PC's own (<see cref="ThisDevice.IsOfThisMachine"/>): kept until the program ends.</summary>
    private readonly HashSet<string> _session = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="filePath">The file; <see langword="null"/> keeps approvals in memory only (tests).</param>
    public CommandTrust(string? filePath, Func<DateTimeOffset>? clock = null)
    {
        _filePath = filePath;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _entries = Load(filePath);
    }

    /// <summary>Raised after an approval changes (on the caller's thread).</summary>
    public event EventHandler? Changed;

    public bool IsApproved(string hash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hash);
        var device = Device;
        lock (_gate)
        {
            if (!ThisDevice.IsOfThisMachine)
            {
                // The id travels with the data folder, so the file says nothing about this PC (review 21).
                return _session.Contains(hash);
            }

            return _entries.Any(e => e.Device == device && string.Equals(e.Hash, hash, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// Approves <paramref name="hash"/> on this PC and saves; false when it holds only until the app ends: the file could
    /// not be written, or this PC's own id is not known (<see cref="ThisDevice.IsOfThisMachine"/>).
    /// </summary>
    public bool Approve(string hash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hash);
        var device = Device;
        bool saved;
        if (!ThisDevice.IsOfThisMachine)
        {
            lock (_gate)
            {
                _session.Add(hash);
            }

            AppLog.Default.Info("Commands", "Approved until the program ends: this PC's own id could not be read, so nothing is saved as approved here");
            Changed?.Invoke(this, EventArgs.Empty);
            return false;
        }

        lock (_gate)
        {
            _entries.RemoveAll(e => e.Device == device && string.Equals(e.Hash, hash, StringComparison.OrdinalIgnoreCase));
            _entries.Add(new Entry(device, hash.ToLowerInvariant(), _clock()));
            if (_entries.Count > MaxEntries)
            {
                _entries = _entries.OrderByDescending(e => e.Approved).Take(MaxEntries).ToList();
            }

            saved = Save();
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return saved;
    }

    /// <summary>
    /// Takes back an approval on this PC and saves (a Send to PC icon whose box "This PC also receives files" was unticked,
    /// review 27); false when the file could not be written. Without this PC's own id only this run's approval is taken
    /// back, as only such an approval was given (<see cref="ThisDevice.IsOfThisMachine"/>).
    /// </summary>
    public bool Revoke(string hash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hash);
        var device = Device;
        var changed = false;
        var saved = true;
        lock (_gate)
        {
            changed = _session.Remove(hash);
            if (ThisDevice.IsOfThisMachine && _entries.RemoveAll(e => e.Device == device && string.Equals(e.Hash, hash, StringComparison.OrdinalIgnoreCase)) > 0)
            {
                changed = true;
                saved = Save();
            }
        }

        if (changed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }

        return saved;
    }

    private static string Device => ThisDevice.Id ?? string.Empty;

    private static List<Entry> Load(string? filePath)
    {
        if (filePath is null || !File.Exists(filePath))
        {
            return [];
        }

        try
        {
            var file = JsonSerializer.Deserialize<TrustFile>(File.ReadAllBytes(filePath), Json);
            return file is { Version: 1, Approved: { } approved }
                ? approved.Where(e => e is { Device: not null, Hash.Length: 64 }).ToList()
                : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Nothing approved: tiles stay paused, which is the safe way to be wrong.
            AppLog.Default.Warning("Commands", $"The approved commands could not be read: {ex.Message}");
            return [];
        }
    }

    private bool Save()
    {
        if (_filePath is null)
        {
            return true;
        }

        var temporary = _filePath + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(new TrustFile(1, _entries), Json));
            File.Move(temporary, _filePath, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Default.Warning("Commands", $"The approved commands could not be saved: {ex.Message}");
            return false;
        }
    }

    private sealed record Entry(string Device, string Hash, DateTimeOffset Approved);

    private sealed record TrustFile(int Version, List<Entry> Approved);
}
