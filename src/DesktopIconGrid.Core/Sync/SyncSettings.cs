using System.Text.Json;
using DesktopIconGrid.Core.Storage;

namespace DesktopIconGrid.Core.Sync;

/// <summary>Device-local sync state. Lives next to the layout file and is never uploaded.</summary>
public sealed class SyncSettings
{
    public const int MaxDeviceNameLength = 64;

    public string DeviceName { get; set; } = DefaultDeviceName();

    public string? ClientId { get; set; }

    public string? ClientSecret { get; set; }

    /// <summary>E-mail of the connected Google account, for display only.</summary>
    public string? AccountEmail { get; set; }

    /// <summary>The copy this device uploads to and checks for updates. <see langword="null"/> when not linked.</summary>
    public string? LinkedProfile { get; set; }

    /// <summary>When the linked copy was last uploaded from or downloaded to this device.</summary>
    public DateTimeOffset? LastSyncedAt { get; set; }

    /// <summary>Upload changes to the linked copy automatically.</summary>
    public bool AutoUpload { get; set; } = true;

    /// <summary>Offer to load the linked copy at startup when another device saved a newer version.</summary>
    public bool CheckAtStartup { get; set; } = true;

    public OAuthClientCredentials Credentials => new(ClientId?.Trim() ?? string.Empty, ClientSecret?.Trim() ?? string.Empty);

    public static string DefaultDeviceName()
    {
        var name = Environment.MachineName;
        return string.IsNullOrWhiteSpace(name) ? "This device" : name;
    }

    public void Normalize()
    {
        DeviceName = string.IsNullOrWhiteSpace(DeviceName) ? DefaultDeviceName() : DeviceName.Trim();
        if (DeviceName.Length > MaxDeviceNameLength)
        {
            DeviceName = DeviceName[..MaxDeviceNameLength];
        }

        ClientId = string.IsNullOrWhiteSpace(ClientId) ? null : ClientId.Trim();
        ClientSecret = string.IsNullOrWhiteSpace(ClientSecret) ? null : ClientSecret.Trim();
        LinkedProfile = string.IsNullOrWhiteSpace(LinkedProfile) ? null : LinkedProfile.Trim();
    }
}

/// <summary>Loads and saves <see cref="SyncSettings"/> as JSON.</summary>
public sealed class SyncSettingsStore
{
    public const string FileName = "sync.json";

    public SyncSettingsStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        FilePath = Path.GetFullPath(filePath);
    }

    public string FilePath { get; }

    public static string GetDefaultFilePath() => Path.Combine(Path.GetDirectoryName(LayoutStorage.GetDefaultFilePath())!, FileName);

    /// <summary>Returns defaults when the file is missing or unreadable.</summary>
    public SyncSettings Load()
    {
        SyncSettings? settings = null;
        if (File.Exists(FilePath))
        {
            try
            {
                settings = JsonSerializer.Deserialize<SyncSettings>(File.ReadAllText(FilePath), LayoutStorage.JsonOptions);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                settings = null;
            }
        }

        settings ??= new SyncSettings();
        settings.Normalize();
        return settings;
    }

    public void Save(SyncSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Normalize();
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var tempPath = FilePath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(settings, LayoutStorage.JsonOptions));
        File.Move(tempPath, FilePath, overwrite: true);
    }
}
