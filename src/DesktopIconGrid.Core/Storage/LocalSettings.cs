using System.Text.Json;
using System.Text.Json.Serialization;
using DesktopIconGrid.Core.Diagnostics;

namespace DesktopIconGrid.Core.Storage;

/// <summary>
/// Settings that belong to this PC and never travel with the bar (review 27 B4). The voice hotkey takes a key
/// combination away from every program and opens the microphone, and the voice language names a recogniser of this PC,
/// so neither may arrive with a synced, restored or imported layout and act on its own. They are kept in a file of
/// their own next to the layout (<see cref="FileName"/>), which no sync, restore, import or layout history reads or
/// writes. A portable copy keeps the file in its folder, so it goes with the drive, as the rest of that copy's data does.
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

/// <summary>Reads and writes <see cref="LocalSettings"/>.</summary>
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

    /// <summary>This PC's settings; the defaults when the file is missing, too large or cannot be read.</summary>
    public LocalSettings Load()
    {
        if (FilePath is null)
        {
            return new LocalSettings();
        }

        try
        {
            var file = new FileInfo(FilePath);
            if (!file.Exists)
            {
                return new LocalSettings();
            }

            if (file.Length > LocalSettings.MaxFileBytes)
            {
                AppLog.Default.Warning("Settings", $"This PC's settings file is {file.Length} bytes, more than any this app writes; it is not read");
                return new LocalSettings();
            }

            var settings = JsonSerializer.Deserialize<LocalSettings>(File.ReadAllBytes(FilePath), Json) ?? new LocalSettings();
            settings.Normalize();
            return settings;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException or InvalidOperationException)
        {
            AppLog.Default.Warning("Settings", $"This PC's settings could not be read: {ex.Message}");
            return new LocalSettings();
        }
    }

    /// <summary>Saves (to a temporary file, then in its place); false when the file could not be written.</summary>
    public bool Save(LocalSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (FilePath is null)
        {
            return true;
        }

        var temporary = FilePath + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(settings, Json));
            File.Move(temporary, FilePath, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Default.Warning("Settings", $"This PC's settings could not be saved: {ex.Message}");
            return false;
        }
    }
}
