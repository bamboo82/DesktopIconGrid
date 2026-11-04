using DesktopIconGrid.Core.Models;

namespace DesktopIconGrid.Core.Sync;

public enum AutoUploadOutcome
{
    /// <summary>Not connected, not linked, or auto-upload is off.</summary>
    Skipped,

    Uploaded,

    /// <summary>Another device saved the linked copy after this device last synced; nothing was uploaded.</summary>
    Conflict,
}

public sealed record AutoUploadResult(AutoUploadOutcome Outcome, RemoteProfile? Remote = null);

/// <summary>
/// Orchestrates the Google Drive sync: connecting an account, listing the named settings copies,
/// saving the current layout as a copy, loading one, and keeping track of the copy this device is linked to.
/// </summary>
public sealed class SyncService
{
    private static readonly TimeSpan ClockTolerance = TimeSpan.FromSeconds(2);

    private readonly GoogleAuthorization _auth;
    private readonly IDriveClient _drive;
    private readonly Action _saveSettings;
    private readonly Func<DateTimeOffset> _clock;
    private readonly string? _appVersion;

    public SyncService(
        GoogleAuthorization auth,
        IDriveClient drive,
        SyncSettings settings,
        Action saveSettings,
        string? appVersion = null,
        Func<DateTimeOffset>? clock = null)
    {
        _auth = auth;
        _drive = drive;
        Settings = settings;
        _saveSettings = saveSettings;
        _appVersion = appVersion;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public SyncSettings Settings { get; }

    public bool HasCredentials => Settings.Credentials.IsComplete;

    public bool IsConnected => _auth.IsAuthorized;

    /// <summary>Connected and linked to a named copy.</summary>
    public bool IsLinked => IsConnected && !string.IsNullOrWhiteSpace(Settings.LinkedProfile);

    public async Task<DriveUser> ConnectAsync(CancellationToken cancellationToken)
    {
        await _auth.AuthorizeAsync(cancellationToken).ConfigureAwait(false);
        var user = await _drive.GetUserAsync(cancellationToken).ConfigureAwait(false);
        Settings.AccountEmail = user.EmailAddress;
        SaveSettings();
        return user;
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        await _auth.RevokeAsync(cancellationToken).ConfigureAwait(false);
        Settings.AccountEmail = null;
        Settings.LinkedProfile = null;
        Settings.LastSyncedAt = null;
        SaveSettings();
    }

    public async Task<IReadOnlyList<RemoteProfile>> ListProfilesAsync(CancellationToken cancellationToken)
    {
        var files = await _drive.ListFilesAsync(cancellationToken).ConfigureAwait(false);
        return files
            .Select(RemoteProfile.FromFile)
            .OfType<RemoteProfile>()
            .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>Saves the layout as the copy called <paramref name="name"/>, replacing an existing copy with that name, and links this device to it.</summary>
    public async Task<RemoteProfile> SaveProfileAsync(string name, LauncherLayout layout, CancellationToken cancellationToken)
    {
        name = NormalizeProfileName(name);
        ArgumentNullException.ThrowIfNull(layout);

        var existing = (await ListProfilesAsync(cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

        var now = _clock();
        var profile = SyncProfile.FromLayout(existing?.Name ?? name, layout, Settings.DeviceName, now, _appVersion);
        var file = await _drive.UploadJsonAsync(
            profile.Name + RemoteProfile.FileExtension,
            profile.ToJson(),
            profile.ToAppProperties(),
            existing?.FileId,
            cancellationToken).ConfigureAwait(false);

        Settings.LinkedProfile = profile.Name;
        Settings.LastSyncedAt = now;
        SaveSettings();

        return RemoteProfile.FromFile(file) ?? new RemoteProfile(profile.Name, file.Id, now, Settings.DeviceName, file.Size);
    }

    /// <summary>Downloads a copy and links this device to it. The caller applies the returned layout.</summary>
    public async Task<SyncProfile> LoadProfileAsync(RemoteProfile remote, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(remote);
        var json = await _drive.DownloadTextAsync(remote.FileId, cancellationToken).ConfigureAwait(false);
        var profile = SyncProfile.FromJson(json);
        if (string.IsNullOrWhiteSpace(profile.Name))
        {
            profile.Name = remote.Name;
        }

        Settings.LinkedProfile = remote.Name;
        Settings.LastSyncedAt = remote.SavedAt ?? profile.SavedAt;
        SaveSettings();
        return profile;
    }

    public async Task DeleteProfileAsync(RemoteProfile remote, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(remote);
        await _drive.DeleteAsync(remote.FileId, cancellationToken).ConfigureAwait(false);
        if (string.Equals(Settings.LinkedProfile, remote.Name, StringComparison.OrdinalIgnoreCase))
        {
            Settings.LinkedProfile = null;
            Settings.LastSyncedAt = null;
            SaveSettings();
        }
    }

    public void Unlink()
    {
        Settings.LinkedProfile = null;
        Settings.LastSyncedAt = null;
        SaveSettings();
    }

    /// <summary>The Drive entry for the linked copy, or <see langword="null"/> when not linked or the copy was deleted.</summary>
    public async Task<RemoteProfile?> FindLinkedProfileAsync(CancellationToken cancellationToken)
    {
        if (!IsLinked)
        {
            return null;
        }

        var profiles = await ListProfilesAsync(cancellationToken).ConfigureAwait(false);
        return profiles.FirstOrDefault(p => string.Equals(p.Name, Settings.LinkedProfile, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The linked copy, when another device saved it after this device last synced.</summary>
    public async Task<RemoteProfile?> CheckForNewerCopyAsync(CancellationToken cancellationToken)
    {
        var remote = await FindLinkedProfileAsync(cancellationToken).ConfigureAwait(false);
        return remote is not null && IsNewerThanLocal(remote, Settings) ? remote : null;
    }

    /// <summary>
    /// Uploads the layout to the linked copy unless another device saved a newer version in the meantime,
    /// in which case nothing is written and the conflict is reported.
    /// </summary>
    public async Task<AutoUploadResult> AutoUploadAsync(LauncherLayout layout, CancellationToken cancellationToken)
    {
        if (!IsLinked || !Settings.AutoUpload)
        {
            return new AutoUploadResult(AutoUploadOutcome.Skipped);
        }

        var remote = await FindLinkedProfileAsync(cancellationToken).ConfigureAwait(false);
        if (remote is not null && IsNewerThanLocal(remote, Settings))
        {
            return new AutoUploadResult(AutoUploadOutcome.Conflict, remote);
        }

        var saved = await SaveProfileAsync(Settings.LinkedProfile!, layout, cancellationToken).ConfigureAwait(false);
        return new AutoUploadResult(AutoUploadOutcome.Uploaded, saved);
    }

    /// <summary>True when the copy was saved by a different device after this device's last sync.</summary>
    public static bool IsNewerThanLocal(RemoteProfile remote, SyncSettings settings)
    {
        ArgumentNullException.ThrowIfNull(remote);
        ArgumentNullException.ThrowIfNull(settings);
        if (remote.SavedAt is not { } savedAt)
        {
            return false;
        }

        if (string.Equals(remote.SavedBy, settings.DeviceName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return settings.LastSyncedAt is null || savedAt > settings.LastSyncedAt.Value + ClockTolerance;
    }

    public static string NormalizeProfileName(string name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            throw new ArgumentException("Give the settings copy a name.", nameof(name));
        }

        if (trimmed.Length > 80)
        {
            throw new ArgumentException("The name is too long (80 characters at most).", nameof(name));
        }

        if (trimmed.IndexOfAny(['/', '\\']) >= 0 || trimmed.EndsWith(RemoteProfile.FileExtension, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The name cannot contain slashes or end with .json.", nameof(name));
        }

        return trimmed;
    }

    public void SaveSettings() => _saveSettings();
}
