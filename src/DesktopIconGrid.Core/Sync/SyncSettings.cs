using System.Text.Json;
using System.Text.Json.Serialization;
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

    /// <summary>The <see cref="MachineKey"/> of the PC these settings belong to (see <see cref="AdoptMachine"/>).</summary>
    public string? MachineFingerprint { get; set; }

    public string? ClientId { get; set; }

    /// <summary>The OAuth client secret in clear, in memory. On disk it is stored as <see cref="ClientSecretProtected"/> when a protector is available.</summary>
    public string? ClientSecret { get; set; }

    /// <summary>
    /// The client secret as written by an <see cref="ISecretProtector"/>; present in <c>sync.json</c> instead of the clear
    /// value. This PC's, as versions before 0.8.1 read it; see <see cref="ClientSecretsByPc"/>.
    /// </summary>
    public string? ClientSecretProtected { get; set; }

    /// <summary>
    /// The protected client secret of each PC that uses these settings, by <see cref="MachineKey"/>. A protector ties a
    /// value to one PC and user (DPAPI), so settings carried from PC to PC (a portable copy) keep every PC's own instead
    /// of each PC erasing the others'. A value whose PC is not known (from 0.8.0) is kept under a name of its own.
    /// </summary>
    public Dictionary<string, string>? ClientSecretsByPc { get; set; }

    /// <summary>The stored value this PC's protector read the client secret from, so a save replaces that one and no other.</summary>
    [JsonIgnore]
    internal string? OwnSecretValue { get; set; }

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

    /// <summary>The <see cref="SyncProfile.Revision"/> this device last uploaded or downloaded for the linked copy.</summary>
    public string? LastRevision { get; set; }

    /// <summary>Upload changes to the linked copy automatically.</summary>
    public bool AutoUpload { get; set; } = true;

    /// <summary>Offer to load the linked copy at startup when another device saved a newer version.</summary>
    public bool CheckAtStartup { get; set; } = true;

    /// <summary>
    /// This PC takes files other PCs send it through the sync folder (0.10.0), even without a Send to PC icon of its
    /// own. Off unless the user turns it on: a PC does not start receiving files because it was updated.
    /// </summary>
    public bool ReceiveFiles { get; set; }

    /// <summary>
    /// These settings stand in for a <c>sync.json</c> that is there but could not be read (another program held it).
    /// They are kept in memory only: <see cref="SyncSettingsStore.Save"/> never writes them over the file, so the link,
    /// the folder, the Google client and the device id in it are all there at the next start.
    /// </summary>
    [JsonIgnore]
    public bool IsStandIn { get; internal set; }

    public OAuthClientCredentials Credentials => new(ClientId?.Trim() ?? string.Empty, ClientSecret?.Trim() ?? string.Empty);

    public static string NewDeviceId() => Guid.NewGuid().ToString("N");

    public SyncSettings Clone() => (SyncSettings)MemberwiseClone();

    /// <summary>
    /// Ties these settings to the PC they are used on. Settings made on another PC (a data folder copied over, a portable
    /// copy on a stick, a cloned virtual machine) get a sync identity of their own here, a new device id and this PC's
    /// name, so the two PCs never take each other's saves for their own. Settings without a fingerprint (from before
    /// 0.8.0) are taken as this PC's. Returns whether anything changed, so they should be saved.
    /// </summary>
    public bool AdoptMachine(string? machineKey)
    {
        if (string.IsNullOrEmpty(machineKey) || string.Equals(MachineFingerprint, machineKey, StringComparison.Ordinal))
        {
            return false;
        }

        if (MachineFingerprint is not null)
        {
            AppLog.Default.Info("Sync", "These sync settings were made on another PC: this PC gets a device id and name of its own");
            DeviceId = NewDeviceId();
            DeviceName = DefaultDeviceName();
        }

        MachineFingerprint = machineKey;
        return true;
    }

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
        ClientSecretsByPc = ClientSecretsByPc?.Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
            .ToDictionary(pair => pair.Key.Trim(), pair => pair.Value.Trim(), StringComparer.Ordinal) is { Count: > 0 } secrets ? secrets : null;
        SyncFolder = string.IsNullOrWhiteSpace(SyncFolder) ? null : SyncFolder.Trim();
        MachineFingerprint = string.IsNullOrWhiteSpace(MachineFingerprint) ? null : MachineFingerprint.Trim();
        LinkedProfile = string.IsNullOrWhiteSpace(LinkedProfile) ? null : LinkedProfile.Trim();
        LinkedFileId = string.IsNullOrWhiteSpace(LinkedFileId) ? null : LinkedFileId.Trim();
        LastRevision = string.IsNullOrWhiteSpace(LastRevision) ? null : LastRevision.Trim();
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

    private bool _toldStandInKept;

    /// <param name="machineKey">This PC's <see cref="MachineKey"/>, under which its protected client secret is kept (see <see cref="SyncSettings.ClientSecretsByPc"/>).</param>
    public SyncSettingsStore(string filePath, ISecretProtector? protector = null, string? machineKey = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        FilePath = Path.GetFullPath(filePath);
        Protector = protector;
        MachineKey = string.IsNullOrWhiteSpace(machineKey) ? null : machineKey;
    }

    public string? MachineKey { get; }

    public string FilePath { get; }

    public ISecretProtector? Protector { get; }

    public static string GetDefaultFilePath() => Path.Combine(Path.GetDirectoryName(LayoutStorage.GetDefaultFilePath())!, FileName);

    /// <summary>
    /// Returns defaults when the file is missing or unreadable. A file that is locked for a moment (a virus scanner, a
    /// sync program) is tried again; when it stays locked, the defaults are a stand-in (<see cref="SyncSettings.IsStandIn"/>)
    /// that is never saved over it. One that is not valid JSON is kept beside it as <c>sync.json.unreadable</c>, and its
    /// device id is kept when it can still be found in it, so this PC does not become another device.
    /// </summary>
    public SyncSettings Load()
    {
        SyncSettings? settings = null;
        var standIn = false;
        if (File.Exists(FilePath))
        {
            string? text = null;
            try
            {
                text = ReadWithRetry(FilePath);
                settings = JsonSerializer.Deserialize<SyncSettings>(text, LayoutStorage.JsonOptions);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                standIn = File.Exists(FilePath);
                AppLog.Default.Warning("Sync", $"{FileName} could not be read; until the next start, sync uses settings kept in memory only, never saved over it: {ex.Message}");
            }
            catch (JsonException ex)
            {
                settings = Salvage(text ?? string.Empty);
                AppLog.Default.Warning("Sync", $"{FileName} was not readable JSON and was kept as {FileName}.unreadable; its device id was {(settings is null ? "lost" : "kept")}: {ex.Message}");
                try
                {
                    File.Copy(FilePath, FilePath + ".unreadable", overwrite: true);
                }
                catch (Exception copyError) when (copyError is IOException or UnauthorizedAccessException)
                {
                    AppLog.Default.Warning("Sync", $"{FileName}.unreadable could not be written: {copyError.Message}");
                }
            }
        }

        settings ??= new SyncSettings();
        settings.IsStandIn = standIn;
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
            else
            {
                // This PC's own first, then the one older versions wrote, then any other: a PC's protector reads only its own.
                foreach (var stored in StoredSecrets(settings))
                {
                    if (Protector.Unprotect(stored) is { } secret)
                    {
                        settings.ClientSecret = secret;
                        settings.OwnSecretValue = stored;
                        break;
                    }
                }

                if (settings.ClientSecret is null && (settings.ClientSecretProtected is not null || settings.ClientSecretsByPc is not null))
                {
                    AppLog.Default.Warning("Sync", "The stored Google client secret was protected on another PC or for another user; enter it again here (the other PC keeps its own)");
                }
            }
        }

        return settings;
    }

    /// <summary>
    /// Writes the settings. Stand-in settings (<see cref="SyncSettings.IsStandIn"/>) are not written while the file they
    /// stand in for is there: they would replace the real settings with blank ones.
    /// </summary>
    public void Save(SyncSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.IsStandIn)
        {
            if (File.Exists(FilePath))
            {
                if (!_toldStandInKept)
                {
                    _toldStandInKept = true;
                    AppLog.Default.Warning("Sync", $"Sync settings not saved: {FileName} could not be read at start, and these would replace it");
                }

                return;
            }

            settings.IsStandIn = false; // the file is gone, so these are the settings now
        }

        settings.Normalize();
        var written = settings;
        if (Protector is not null)
        {
            written = settings.Clone();
            var secrets = new Dictionary<string, string>(settings.ClientSecretsByPc ?? [], StringComparer.Ordinal);
            foreach (var key in secrets.Where(pair => pair.Value == settings.OwnSecretValue || pair.Key == MachineKey).Select(pair => pair.Key).ToList())
            {
                secrets.Remove(key); // this PC's old value, wherever it was
            }

            var legacy = settings.ClientSecretProtected == settings.OwnSecretValue ? null : settings.ClientSecretProtected; // another PC's
            var protectedHere = settings.ClientSecret is { } secret ? Protector.Protect(secret) : null;
            if (protectedHere is not null && legacy is not null && !secrets.ContainsValue(legacy))
            {
                secrets[UnknownPcKey(legacy)] = legacy; // from 0.8.0, its PC not known: kept for it beside this PC's
            }

            if (MachineKey is not null && protectedHere is not null)
            {
                secrets[MachineKey] = protectedHere;
            }

            written.ClientSecretProtected = protectedHere ?? legacy;
            written.ClientSecretsByPc = secrets.Count > 0 ? secrets : null;
            written.ClientSecret = null;
            settings.ClientSecretProtected = written.ClientSecretProtected;
            settings.ClientSecretsByPc = written.ClientSecretsByPc;
            settings.OwnSecretValue = protectedHere;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var tempPath = FilePath + ".tmp";
        using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        {
            stream.Write(System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(written, LayoutStorage.JsonOptions)));
            stream.Flush(flushToDisk: true); // on disk before it replaces the old file: a power cut leaves one or the other
        }

        File.Move(tempPath, FilePath, overwrite: true);
    }

    /// <summary>The protected client secrets to try, this PC's own first.</summary>
    private IEnumerable<string> StoredSecrets(SyncSettings settings)
    {
        if (MachineKey is not null && settings.ClientSecretsByPc?.TryGetValue(MachineKey, out var own) == true)
        {
            yield return own;
        }

        if (settings.ClientSecretProtected is { } legacy)
        {
            yield return legacy;
        }

        foreach (var other in settings.ClientSecretsByPc?.Where(pair => pair.Key != MachineKey).Select(pair => pair.Value) ?? [])
        {
            yield return other;
        }
    }

    private static string UnknownPcKey(string value) =>
        "unknown-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)))[..12].ToLowerInvariant();

    private static string ReadWithRetry(string path)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return File.ReadAllText(path);
            }
            catch (IOException) when (attempt < 3 && File.Exists(path))
            {
                Thread.Sleep(100);
            }
        }
    }

    /// <summary>The device id (and the PC it was made on) from a file that is no longer valid JSON, or <see langword="null"/>.</summary>
    private static SyncSettings? Salvage(string text)
    {
        static string? Value(string text, string name) =>
            System.Text.RegularExpressions.Regex.Match(text, "\"" + name + "\"\\s*:\\s*\"([^\"]{1,128})\"", System.Text.RegularExpressions.RegexOptions.IgnoreCase) is { Success: true } match
                ? match.Groups[1].Value
                : null;

        return Value(text, "deviceId") is { } deviceId
            ? new SyncSettings { DeviceId = deviceId, DeviceName = Value(text, "deviceName") ?? SyncSettings.DefaultDeviceName(), MachineFingerprint = Value(text, "machineFingerprint") }
            : null;
    }
}
