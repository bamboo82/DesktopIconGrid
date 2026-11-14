using System.Text.Json;
using System.Text.Json.Serialization;
using DesktopIconGrid.Core.Diagnostics;
using DesktopIconGrid.Core.Models;

namespace DesktopIconGrid.Core.Storage;

/// <summary>
/// Settings that belong to this PC and never travel with the bar (review 27 B4). The voice hotkey takes a key
/// combination away from every program and opens the microphone, and the voice language names a recogniser of this PC,
/// so neither may arrive with a synced, restored or imported layout and act on its own. They are kept in a file of
/// their own next to the layout (<see cref="FileName"/>), which no sync, restore, import or layout history reads or
/// writes, one set per PC (<see cref="LocalSettingsStore"/>): a portable copy's file goes with the drive, but each PC
/// it is used on has its own.
/// </summary>
public sealed class LocalSettings
{
    public const string FileName = "this-pc.json";

    /// <summary>A file larger than this is no file of this app's and is not read.</summary>
    public const long MaxFileBytes = 64 * 1024;

    /// <summary>The voice hotkey (<c>Ctrl+Alt+V</c>); <see langword="null"/> for none, the default.</summary>
    public string? VoiceHotkey { get; set; }

    /// <summary>The culture voice launch listens in (<c>zh-TW</c>); <see langword="null"/> follows the bar's language.</summary>
    public string? VoiceLanguage { get; set; }

    /// <summary>Properties written by a newer version, kept through a save.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    public LocalSettings Clone() => new()
    {
        VoiceHotkey = VoiceHotkey,
        VoiceLanguage = VoiceLanguage,
        ExtensionData = ExtensionData is null ? null : new Dictionary<string, JsonElement>(ExtensionData),
    };

    /// <summary>Trims, and drops what no version writes: a hotkey over 100 characters, a language over 20.</summary>
    public void Normalize()
    {
        VoiceHotkey = string.IsNullOrWhiteSpace(VoiceHotkey) || VoiceHotkey.Trim().Length > 100 ? null : VoiceHotkey.Trim();
        VoiceLanguage = string.IsNullOrWhiteSpace(VoiceLanguage) || VoiceLanguage.Trim().Length > 20 ? null : VoiceLanguage.Trim();
    }
}

/// <summary>
/// Reads and writes <see cref="LocalSettings"/>, one set per PC, by <see cref="ThisDevice.Id"/> as
/// <see cref="SpecialIcons.CommandTrust"/> keys its approvals (the review's answer on WP3): a portable copy carried to
/// another PC does not register its microphone hotkey there. Without this PC's own id
/// (<see cref="ThisDevice.IsOfThisMachine"/> false) nothing is read or written, and what is set holds until the
/// program ends, as approvals do.
/// </summary>
/// <param name="filePath">The file; <see langword="null"/> keeps the settings in memory only (tests).</param>
public sealed class LocalSettingsStore(string? filePath)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string? FilePath { get; } = filePath;

    public static string GetDefaultFilePath() => Path.Combine(Path.GetDirectoryName(LayoutStorage.GetDefaultFilePath())!, LocalSettings.FileName);

    /// <summary>This PC's settings; the defaults when the file is missing, too large or cannot be read, or has none for this PC.</summary>
    public LocalSettings Load()
    {
        if (FilePath is null || ThisPc() is not { } pc)
        {
            return new LocalSettings();
        }

        var (file, _) = Read();
        if (file?.Pcs is not null && file.Pcs.TryGetValue(pc, out var mine) && mine is not null)
        {
            mine.Normalize();
            return mine;
        }

        return new LocalSettings();
    }

    /// <summary>
    /// Saves this PC's settings beside the other PCs' (to a temporary file, then in its place). False when they are not
    /// kept past this run: the file could not be written, or this PC's own id is not known.
    /// </summary>
    public bool Save(LocalSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (FilePath is null)
        {
            return true;
        }

        if (ThisPc() is not { } pc)
        {
            AppLog.Default.Info("Settings", "This PC's own id could not be read: its voice settings hold until the program ends");
            return false;
        }

        var (file, readable) = Read();
        if (!readable)
        {
            return false; // the other PCs' settings are in it and could not be read: not written over
        }

        file ??= new LocalSettingsFile();
        file.Pcs ??= new Dictionary<string, LocalSettings?>(StringComparer.Ordinal);
        file.Pcs[pc] = settings;
        file.ExtensionData?.Remove("voiceHotkey"); // a development build of 0.11.0 kept one set for every PC
        file.ExtensionData?.Remove("voiceLanguage");
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
            AppLog.Default.Warning("Settings", $"This PC's settings could not be saved: {ex.Message}");
            return false;
        }
    }

    /// <summary>The key of this PC's settings, or <see langword="null"/> when this PC's own id is not known.</summary>
    private static string? ThisPc() => ThisDevice.IsOfThisMachine && !string.IsNullOrWhiteSpace(ThisDevice.Id) ? ThisDevice.Id : null;

    /// <summary>
    /// The file: <see langword="null"/> when it is missing, too large or not this app's (and then written anew), with
    /// <c>Readable</c> false only when it is there and could not be read now (a lock), so a save does not overwrite it.
    /// </summary>
    private (LocalSettingsFile? File, bool Readable) Read()
    {
        try
        {
            var info = new FileInfo(FilePath!);
            if (!info.Exists)
            {
                return (null, true);
            }

            if (info.Length > LocalSettings.MaxFileBytes)
            {
                AppLog.Default.Warning("Settings", $"This PC's settings file is {info.Length} bytes, more than any this app writes; it is not read");
                return (null, true);
            }

            return (JsonSerializer.Deserialize<LocalSettingsFile>(File.ReadAllBytes(FilePath!), Json), true);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
        {
            AppLog.Default.Warning("Settings", $"This PC's settings could not be read: {ex.Message}");
            return (null, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Default.Warning("Settings", $"This PC's settings could not be read: {ex.Message}");
            return (null, false);
        }
    }

    /// <summary>The file: each PC's settings by its id, and what a newer version wrote.</summary>
    private sealed class LocalSettingsFile
    {
        public Dictionary<string, LocalSettings?>? Pcs { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtensionData { get; set; }
    }
}
