using System.Text.Json;
using DesktopIconGrid.Core.Diagnostics;
using DesktopIconGrid.Core.Storage;

namespace DesktopIconGrid.Core.Sync;

/// <summary>Device-local sync state. Lives next to the layout file and is never uploaded.</summary>
public sealed class SyncSettings
{
    public const int MaxDeviceNameLength = 64;

    public string DeviceName { get; set; } = DefaultDeviceName();

    /// <summary>Random id of this installation; tells devices apart even when two PCs share a name.</summary>
    public string DeviceId { get; set; } = NewDeviceId();

    public string? ClientId { get; set; }

    /// <summary>The OAuth client secret in clear, in memory. On disk it is stored as <see cref="ClientSecretProtected"/> when a protector is available.</summary>
    public string? ClientSecret { get; set; }

    /// <summary>The client secret as written by an <see cref="ISecretProtector"/>; present in <c>sync.json</c> instead of the clear value.</summary>
    public string? ClientSecretProtected { get; set; }

    /// <summary>E-mail of the connected Google account, for display only.</summary>
    public string? AccountEmail { get; set; }

    /// <summary>
    /// A folder to keep the copies in instead of Google Drive (one that OneDrive, Dropbox or a network share keeps in
    /// step across PCs); <see langword="null"/> for Google Drive. See <see cref="FolderSyncClient"/>.
    /// </summary>
    public string? SyncFolder { get; set; }

    /// <summary>The copy this device uploads to and checks for updates. <see langword="null"/> when not linked.</summary>
    public string? LinkedProfile { get; set; }

    /// <summary>Drive file id of the linked copy, so it is found even when another copy has the same name.</summary>
    public string? LinkedFileId { get; set; }

    /// <summary>When the linked copy was last uploaded from or downloaded to this device.</summary>
    public DateTimeOffset? LastSyncedAt { get; set; }

    /// <summary>Upload changes to the linked copy automatically.</summary>
    public bool AutoUpload { get; set; } = true;

    /// <summary>Offer to load the linked copy at startup when another device saved a newer version.</summary>
    public bool CheckAtStartup { get; set; } = true;

    public OAuthClientCredentials Credentials => new(ClientId?.Trim() ?? string.Empty, ClientSecret?.Trim() ?? string.Empty);

    public static string NewDeviceId() => Guid.NewGuid().ToString("N");

    public SyncSettings Clone() => (SyncSettings)MemberwiseClone();

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
        ClientSecretProtected = string.IsNullOrWhiteSpace(ClientSecretProtected) ? null : ClientSecretProtected.Trim();
        SyncFolder = string.IsNullOrWhiteSpace(SyncFolder) ? null : SyncFolder.Trim();
        LinkedProfile = string.IsNullOrWhiteSpace(LinkedProfile) ? null : LinkedProfile.Trim();
        LinkedFileId = string.IsNullOrWhiteSpace(LinkedFileId) ? null : LinkedFileId.Trim();
        if (string.IsNullOrWhiteSpace(DeviceId))
        {
            DeviceId = NewDeviceId();
        }
    }
}

/// <summary>
/// Loads and saves <see cref="SyncSettings"/> as JSON. With a <see cref="Protector"/> the client secret is written
/// encrypted (<see cref="SyncSettings.ClientSecretProtected"/>) and a clear value left by an earlier version is
/// encrypted the first time the file is loaded.
/// </summary>
public sealed class SyncSettingsStore
{
    public const string FileName = "sync.json";

    public SyncSettingsStore(string filePath, ISecretProtector? protector = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        FilePath = Path.GetFullPath(filePath);
        Protector = protector;
    }

    public string FilePath { get; }

    public ISecretProtector? Protector { get; }

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

        if (Protector is not null)
        {
            if (settings.ClientSecret is not null)
            {
                // A clear value from an earlier version (or edited by hand): encrypt it now. A file that cannot be
                // written (read-only, locked share) must not stop the app; the value stays usable in memory.
                try
                {
                    Save(settings);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    AppLog.Default.Warning("Sync", $"The client secret could not be re-saved encrypted yet: {ex.Message}");
                }
            }
            else if (settings.ClientSecretProtected is { } sealedSecret)
            {
                settings.ClientSecret = Protector.Unprotect(sealedSecret);
                if (settings.ClientSecret is null)
                {
                    AppLog.Default.Warning("Sync", "The stored Google client secret could not be decrypted (another user or machine?); enter it again");
                    settings.ClientSecretProtected = null;
                }
            }
        }

        return settings;
    }

    public void Save(SyncSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Normalize();
        var written = settings;
        if (Protector is not null)
        {
            written = settings.Clone();
            written.ClientSecretProtected = settings.ClientSecret is { } secret ? Protector.Protect(secret) : null;
            written.ClientSecret = null;
            settings.ClientSecretProtected = written.ClientSecretProtected;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var tempPath = FilePath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(written, LayoutStorage.JsonOptions));
        File.Move(tempPath, FilePath, overwrite: true);
    }
}
