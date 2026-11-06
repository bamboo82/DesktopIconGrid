using DesktopIconGrid.Core.Diagnostics;
using DesktopIconGrid.Core.Models;

namespace DesktopIconGrid.Core.Sync;

public enum AutoUploadOutcome
{
    /// <summary>Not connected, not linked, or auto-upload is off.</summary>
    Skipped,

    Uploaded,

    /// <summary>Another device saved the linked copy after this device last synced; nothing was uploaded.</summary>
    Conflict,

    /// <summary>The linked copy could not be read (a sync program may be writing it); nothing was uploaded, try again later.</summary>
    Postponed,
}

public sealed record AutoUploadResult(AutoUploadOutcome Outcome, RemoteProfile? Remote = null)
{
    /// <summary>The copies a sync program kept of the linked copy (<see cref="RemoteProfile.IsConflictCopy"/>), to point out.</summary>
    public IReadOnlyList<RemoteProfile> ConflictCopies { get; init; } = [];
}

/// <summary>
/// The linked copy when another device saved it since this device last synced (<see langword="null"/> otherwise), and the
/// copies a sync program kept of it (<see cref="RemoteProfile.IsConflictCopy"/>).
/// </summary>
public sealed record LinkedCopyCheck(RemoteProfile? Newer, IReadOnlyList<RemoteProfile> ConflictCopies);

/// <summary>
/// Orchestrates the sync: listing the named settings copies, saving the current layout as a copy, loading one, and
/// keeping track of the copy this device is linked to. The copies live on Google Drive (after connecting an account)
/// or, when <see cref="SyncSettings.SyncFolder"/> is set, as files in that folder with no sign-in
/// (<see cref="FolderSyncClient"/>). The Google account also serves Google Drive Buckets, whichever is used for copies.
/// One operation on the copies or the link runs at a time, with the storage chosen at its start (see
/// <see cref="ExclusiveAsync{T}"/>).
/// </summary>
public sealed class SyncService
{
    private static readonly TimeSpan ClockTolerance = TimeSpan.FromSeconds(2);

    private readonly GoogleAuthorization _auth;
    private readonly IDriveClient _drive;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private FolderSyncClient? _folder;
    private readonly Action _saveSettings;
    private readonly Func<DateTimeOffset> _clock;
    private readonly string? _appVersion;

    public SyncService(
        GoogleAuthorization auth,
        IDriveClient drive,
        SyncSettings settings,
        Action saveSettings,
        string? appVersion = null,
        Func<DateTimeOffset>? clock = null,
        IDriveFiles? files = null)
    {
        _auth = auth;
        _drive = drive;
        Settings = settings;
        _saveSettings = saveSettings;
        _appVersion = appVersion;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        Files = files ?? drive as IDriveFiles;
    }

    public SyncSettings Settings { get; }

    /// <summary>Browsing and transfers for arbitrary Drive files; <see langword="null"/> when the client cannot do that.</summary>
    public IDriveFiles? Files { get; }

    /// <summary>True when the stored sign-in covers the whole Drive (needed by Google Drive Buckets).</summary>
    public bool HasFullDriveAccess => _auth.HasScope(GoogleAuthorization.FullDriveScope);

    /// <summary>Signs in (again) with the full Drive scope unless it was already granted.</summary>
    public async Task EnsureFullDriveAccessAsync(CancellationToken cancellationToken)
    {
        if (HasFullDriveAccess)
        {
            return;
        }

        await _auth.AuthorizeAsync(cancellationToken, [GoogleAuthorization.FullDriveScope]).ConfigureAwait(false);
        var user = await _drive.GetUserAsync(cancellationToken).ConfigureAwait(false);
        Settings.AccountEmail = user.EmailAddress;
        SaveSettings();
        if (!HasFullDriveAccess)
        {
            AppLog.Default.Warning("Sync", "Full Google Drive access was not granted on the consent screen");
            throw new GoogleAuthException("Access to your whole Google Drive was not granted. Sign in again and tick the Drive permission.");
        }

        AppLog.Default.Info("Sync", $"Full Google Drive access granted for {user.EmailAddress ?? "the account"}");
    }

    public bool HasCredentials => Settings.Credentials.IsComplete;

    /// <summary>The copies are kept in <see cref="SyncSettings.SyncFolder"/> rather than on Google Drive.</summary>
    public bool UsesFolder => Settings.SyncFolder is not null;

    /// <summary>The copies can be reached: a folder is chosen, or the Google account is connected.</summary>
    public bool IsConnected => UsesFolder || IsGoogleConnected;

    /// <summary>A Google account is connected (for copies on Google Drive, and for buckets).</summary>
    public bool IsGoogleConnected => _auth.IsAuthorized;

    /// <summary>Where the copies are read and written: the chosen folder, or Google Drive.</summary>
    private IDriveClient Copies
    {
        get
        {
            if (Settings.SyncFolder is not { } folder)
            {
                return _drive;
            }

            if (_folder is null || !string.Equals(_folder.Folder, Path.GetFullPath(folder), StringComparison.OrdinalIgnoreCase))
            {
                _folder = new FolderSyncClient(folder);
            }

            return _folder;
        }
    }

    private string StorageName => Settings.SyncFolder ?? "Google Drive";

    /// <summary>
    /// Runs <paramref name="operation"/> alone, on the storage in use when it starts. An upload that listed one folder so
    /// finishes there before the copies move elsewhere, instead of writing that folder's file name into the next one,
    /// and nothing links this device again to a copy it was unlinked from while the operation waited for the network.
    /// </summary>
    private async Task<T> ExclusiveAsync<T>(Func<IDriveClient, Task<T>> operation, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await operation(Copies).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private Task ExclusiveAsync(Func<IDriveClient, Task> operation, CancellationToken cancellationToken) =>
        ExclusiveAsync(async copies =>
        {
            await operation(copies).ConfigureAwait(false);
            return true;
        }, cancellationToken);

    /// <summary>
    /// Keeps the copies in <paramref name="folder"/> from now on, creating it when needed, on a worker: a network share
    /// that is off can take a minute to answer. The device is unlinked: the copy it was linked to is elsewhere. The
    /// copies themselves are not moved. An operation that is running (an upload) finishes first.
    /// </summary>
    public async Task UseFolderAsync(string folder, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        var full = Path.GetFullPath(folder.Trim());
        await ExclusiveAsync(async _ =>
        {
            await Task.Run(() => Directory.CreateDirectory(full), cancellationToken).ConfigureAwait(false);
            if (string.Equals(Settings.SyncFolder, full, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            Settings.SyncFolder = full;
            ForgetLink();
            SaveSettings();
            AppLog.Default.Info("Sync", $"Settings copies are kept in the folder {full}");
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Keeps the copies on Google Drive again; the device is unlinked from the folder's copy. A running upload finishes first.</summary>
    public Task UseGoogleDriveAsync(CancellationToken cancellationToken = default) => ExclusiveAsync(_ =>
    {
        if (Settings.SyncFolder is not null)
        {
            Settings.SyncFolder = null;
            ForgetLink();
            SaveSettings();
            AppLog.Default.Info("Sync", "Settings copies are kept on Google Drive");
        }

        return Task.CompletedTask;
    }, cancellationToken);

    /// <summary>Connected and linked to a named copy.</summary>
    public bool IsLinked => IsConnected && !string.IsNullOrWhiteSpace(Settings.LinkedProfile);

    public async Task<DriveUser> ConnectAsync(CancellationToken cancellationToken)
    {
        await _auth.AuthorizeAsync(cancellationToken).ConfigureAwait(false);
        var user = await _drive.GetUserAsync(cancellationToken).ConfigureAwait(false);
        Settings.AccountEmail = user.EmailAddress;
        SaveSettings();
        AppLog.Default.Info("Sync", $"Connected to Google Drive as {user.EmailAddress ?? "an account"}");
        return user;
    }

    /// <summary>Signs out of Google. A link to a copy on Google Drive goes with it; one to a copy in a folder stays.</summary>
    public async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        await _auth.RevokeAsync(cancellationToken).ConfigureAwait(false);
        await ExclusiveAsync(_ =>
        {
            Settings.AccountEmail = null;
            if (!UsesFolder)
            {
                ForgetLink();
            }

            SaveSettings();
            return Task.CompletedTask;
        }, cancellationToken).ConfigureAwait(false);
        AppLog.Default.Info("Sync", "Disconnected from Google Drive");
    }

    public Task<IReadOnlyList<RemoteProfile>> ListProfilesAsync(CancellationToken cancellationToken) =>
        ExclusiveAsync(copies => ListAsync(copies, cancellationToken), cancellationToken);

    private static async Task<IReadOnlyList<RemoteProfile>> ListAsync(IDriveClient copies, CancellationToken cancellationToken)
    {
        var files = await copies.ListFilesAsync(cancellationToken).ConfigureAwait(false);
        return files
            .Select(RemoteProfile.FromFile)
            .OfType<RemoteProfile>()
            .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>Saves the layout as the copy called <paramref name="name"/>, replacing an existing copy with that name, and links this device to it.</summary>
    public Task<RemoteProfile> SaveProfileAsync(string name, LauncherLayout layout, CancellationToken cancellationToken) =>
        SaveProfileAsync(name, layout, fileId: null, cancellationToken);

    /// <summary>
    /// Saves the layout as the copy called <paramref name="name"/> and links this device to it. The copy that is
    /// replaced is <paramref name="fileId"/> when given, otherwise the linked copy when its name matches, otherwise
    /// the first copy with that name; duplicate names on Drive therefore never swap files.
    /// </summary>
    public async Task<RemoteProfile> SaveProfileAsync(string name, LauncherLayout layout, string? fileId, CancellationToken cancellationToken)
    {
        name = NormalizeProfileName(name);
        ArgumentNullException.ThrowIfNull(layout);
        return await ExclusiveAsync(copies => SaveAsync(copies, name, layout, fileId, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    private async Task<RemoteProfile> SaveAsync(IDriveClient copies, string name, LauncherLayout layout, string? fileId, CancellationToken cancellationToken)
    {
        var profiles = await ListAsync(copies, cancellationToken).ConfigureAwait(false);
        fileId ??= string.Equals(Settings.LinkedProfile, name, StringComparison.OrdinalIgnoreCase) ? Settings.LinkedFileId : null;
        var existing = (fileId is null ? null : profiles.FirstOrDefault(p => string.Equals(p.FileId, fileId, StringComparison.Ordinal)))
                       ?? profiles.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

        var now = _clock();
        var profile = SyncProfile.FromLayout(existing?.Name ?? name, layout, Settings.DeviceName, now, _appVersion, Settings.DeviceId);
        var file = await copies.UploadJsonAsync(
            profile.Name + RemoteProfile.FileExtension,
            profile.ToJson(),
            profile.ToAppProperties(),
            existing?.FileId,
            cancellationToken).ConfigureAwait(false);

        Settings.LinkedProfile = profile.Name;
        Settings.LinkedFileId = file.Id;
        Settings.LastSyncedAt = file.ModifiedTime ?? now; // Drive's clock (or the file's time in a folder), not this PC's
        Settings.LastRevision = profile.Revision;
        SaveSettings();
        AppLog.Default.Info("Sync", $"Saved settings copy \"{profile.Name}\" to {StorageName} ({layout.Items.Count} items, {(existing is null ? "new" : "replaced")})");

        return RemoteProfile.FromFile(file) ?? new RemoteProfile(profile.Name, file.Id, now, Settings.DeviceName, file.Size);
    }

    /// <summary>
    /// Downloads a copy and links this device to it. The caller applies the returned layout. A sync program's conflict
    /// copy (<see cref="RemoteProfile.IsConflictCopy"/>) is loaded, but the device stays linked to the copy it is a copy
    /// of, the one the other PCs upload to; with the conflict copy's revision remembered, the next upload asks before it
    /// replaces another PC's save there.
    /// </summary>
    public async Task<SyncProfile> LoadProfileAsync(RemoteProfile remote, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(remote);
        return await ExclusiveAsync(copies => LoadAsync(copies, remote, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    private async Task<SyncProfile> LoadAsync(IDriveClient copies, RemoteProfile remote, CancellationToken cancellationToken)
    {
        var json = await copies.DownloadTextAsync(remote.FileId, cancellationToken).ConfigureAwait(false);
        var profile = SyncProfile.FromJson(json);
        if (string.IsNullOrWhiteSpace(profile.Name))
        {
            profile.Name = remote.Name;
        }

        var linkedName = remote.Name;
        var linkedFileId = remote.FileId;
        if (remote.IsConflictCopy)
        {
            var original = (await ListAsync(copies, cancellationToken).ConfigureAwait(false))
                .FirstOrDefault(p => !p.IsConflictCopy && string.Equals(p.Name, profile.Name, StringComparison.OrdinalIgnoreCase));
            linkedName = profile.Name;
            linkedFileId = original?.FileId; // none left: the next upload writes it again
        }

        Settings.LinkedProfile = linkedName;
        Settings.LinkedFileId = linkedFileId;
        Settings.LastSyncedAt = remote.SavedAt ?? profile.SavedAt;
        Settings.LastRevision = profile.Revision ?? remote.Revision;
        SaveSettings();
        AppLog.Default.Info("Sync", $"Loaded settings copy \"{remote.Name}\" (saved by {profile.SavedBy} on {profile.SavedAt.LocalDateTime:g}, {profile.Layout.Items.Count} items)");
        return profile;
    }

    public async Task DeleteProfileAsync(RemoteProfile remote, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(remote);
        await ExclusiveAsync(async copies =>
        {
            await copies.DeleteAsync(remote.FileId, cancellationToken).ConfigureAwait(false);
            AppLog.Default.Info("Sync", $"Deleted settings copy \"{remote.Name}\" from {StorageName}");
            if (string.Equals(Settings.LinkedFileId, remote.FileId, StringComparison.Ordinal)
                || (Settings.LinkedFileId is null && string.Equals(Settings.LinkedProfile, remote.Name, StringComparison.OrdinalIgnoreCase)))
            {
                ForgetLink();
                SaveSettings();
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Unlinks this device from its copy, after an upload that is running.</summary>
    public Task UnlinkAsync(CancellationToken cancellationToken = default) => ExclusiveAsync(_ =>
    {
        ForgetLink();
        SaveSettings();
        return Task.CompletedTask;
    }, cancellationToken);

    private void ForgetLink()
    {
        Settings.LinkedProfile = null;
        Settings.LinkedFileId = null;
        Settings.LastSyncedAt = null;
        Settings.LastRevision = null;
    }

    /// <summary>The Drive entry for the linked copy, or <see langword="null"/> when not linked or the copy was deleted.</summary>
    public async Task<RemoteProfile?> FindLinkedProfileAsync(CancellationToken cancellationToken) =>
        (await ExclusiveAsync(copies => FindLinkedAsync(copies, cancellationToken), cancellationToken).ConfigureAwait(false)).Linked;

    /// <summary>The linked copy and the copies a sync program kept of it, read while the link cannot change.</summary>
    private async Task<(RemoteProfile? Linked, IReadOnlyList<RemoteProfile> ConflictCopies)> FindLinkedAsync(IDriveClient copies, CancellationToken cancellationToken)
    {
        if (!IsLinked)
        {
            return (null, []);
        }

        var profiles = await ListAsync(copies, cancellationToken).ConfigureAwait(false);
        var linked = profiles.FirstOrDefault(p => Settings.LinkedFileId is not null && string.Equals(p.FileId, Settings.LinkedFileId, StringComparison.Ordinal))
                     ?? profiles.FirstOrDefault(p => !p.IsConflictCopy && string.Equals(p.Name, Settings.LinkedProfile, StringComparison.OrdinalIgnoreCase));
        var conflictCopies = profiles
            .Where(p => p.IsConflictCopy && p != linked && string.Equals(p.NameInside, Settings.LinkedProfile, StringComparison.OrdinalIgnoreCase))
            .ToList();
        return (linked, conflictCopies);
    }

    /// <summary>The linked copy, when another device saved it after this device last synced (one that cannot be read is asked about later).</summary>
    public async Task<RemoteProfile?> CheckForNewerCopyAsync(CancellationToken cancellationToken) =>
        (await CheckLinkedCopyAsync(cancellationToken).ConfigureAwait(false)).Newer;

    /// <summary>What the startup check reports: the newer linked copy, if any, and the copies a sync program kept of it.</summary>
    public Task<LinkedCopyCheck> CheckLinkedCopyAsync(CancellationToken cancellationToken) => ExclusiveAsync(async copies =>
    {
        var (remote, conflictCopies) = await FindLinkedAsync(copies, cancellationToken).ConfigureAwait(false);
        return new LinkedCopyCheck(remote is { Unreadable: false } && IsNewerThanLocal(remote, Settings) ? remote : null, conflictCopies);
    }, cancellationToken);

    /// <summary>
    /// Uploads the layout to the linked copy unless another device saved a newer version in the meantime,
    /// in which case nothing is written and the conflict is reported.
    /// </summary>
    public Task<AutoUploadResult> AutoUploadAsync(LauncherLayout layout, CancellationToken cancellationToken) => ExclusiveAsync(async copies =>
    {
        if (!IsLinked || !Settings.AutoUpload)
        {
            return new AutoUploadResult(AutoUploadOutcome.Skipped);
        }

        var (remote, conflictCopies) = await FindLinkedAsync(copies, cancellationToken).ConfigureAwait(false);
        if (remote is { Unreadable: true })
        {
            AppLog.Default.Warning("Sync", $"Auto-upload postponed: \"{remote.Name}\" cannot be read right now");
            return new AutoUploadResult(AutoUploadOutcome.Postponed, remote) { ConflictCopies = conflictCopies };
        }

        if (remote is not null && IsNewerThanLocal(remote, Settings))
        {
            AppLog.Default.Warning("Sync", $"Auto-upload skipped: \"{remote.Name}\" was saved by {remote.SavedBy ?? "another device"} after this device last synced");
            return new AutoUploadResult(AutoUploadOutcome.Conflict, remote) { ConflictCopies = conflictCopies };
        }

        var saved = await SaveAsync(copies, NormalizeProfileName(Settings.LinkedProfile!), layout, remote?.FileId ?? Settings.LinkedFileId, cancellationToken).ConfigureAwait(false);
        return new AutoUploadResult(AutoUploadOutcome.Uploaded, saved) { ConflictCopies = conflictCopies };
    }, cancellationToken);

    /// <summary>
    /// True when the copy was saved by a different device after this device's last sync. With revisions on both sides
    /// that means another revision than the one this device last wrote or read: in a folder, a sync program carries the
    /// writer's own file time, a write while offline moves this device's last sync forward before the sync program has
    /// caught up, and a clock can be wrong. On Google Drive a later time counts as well, since the time there is Drive's
    /// own: an update from 0.7.0 names only the properties it knows, and Drive keeps the others, so the copy still
    /// shows the revision of the save before it. Without revisions (copies or settings from before 0.8.0) the times
    /// decide, as before.
    /// </summary>
    public static bool IsNewerThanLocal(RemoteProfile remote, SyncSettings settings)
    {
        ArgumentNullException.ThrowIfNull(remote);
        ArgumentNullException.ThrowIfNull(settings);
        var sameDevice = remote.SavedById is not null
            ? string.Equals(remote.SavedById, settings.DeviceId, StringComparison.Ordinal)
            : string.Equals(remote.SavedBy, settings.DeviceName, StringComparison.OrdinalIgnoreCase);
        if (sameDevice)
        {
            return false;
        }

        var savedLater = remote.SavedAt is { } savedAt && (settings.LastSyncedAt is null || savedAt > settings.LastSyncedAt.Value + ClockTolerance);
        if (remote.Revision is { } revision && settings.LastRevision is { } last)
        {
            var onDrive = settings.SyncFolder is null;
            return !string.Equals(revision, last, StringComparison.Ordinal) || (onDrive && savedLater);
        }

        return savedLater;
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
