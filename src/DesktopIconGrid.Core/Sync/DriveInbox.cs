using System.Net;
using System.Text;
using System.Text.Json;
using DesktopIconGrid.Core.Diagnostics;

namespace DesktopIconGrid.Core.Sync;

/// <summary>
/// The inbox of <see cref="FolderInbox"/> on Google Drive (0.11.0): the same layout under the app's own folder
/// (<c>DesktopIconGrid/inbox/&lt;device id&gt;/</c> with a <c>device.json</c>, sent files named
/// <c>&lt;sender id&gt;~&lt;name&gt;</c>), the same names, limits and refusals. The app created those files, so the
/// basic <c>drive.file</c> permission is enough. The account is the trust boundary: whoever is signed in to it with
/// this app's credentials can send.
/// </summary>
/// <remarks>
/// The ids of the inbox folders are remembered, and looked up again after Drive answers "not found" (a folder deleted
/// on Drive, review 27 S4). Of two folders of one name, made by two PCs at the same moment, every PC uses the older.
/// </remarks>
public sealed class DriveInbox : IInbox
{
    private readonly IDriveFiles _files;
    private readonly string _deviceId;
    private readonly string _deviceName;
    private string? _inboxId;
    private string? _myFolderId;

    /// <summary>What this PC's <c>device.json</c> on Drive says, as far as this run knows: read once, then remembered.</summary>
    private string? _announced;

    public DriveInbox(IDriveFiles files, string deviceId, string deviceName)
    {
        ArgumentNullException.ThrowIfNull(files);
        if (!FolderInbox.IsDeviceId(deviceId))
        {
            throw new ArgumentException("Not a device id.", nameof(deviceId));
        }

        _files = files;
        _deviceId = deviceId.ToLowerInvariant();
        _deviceName = FolderInbox.ShownName(deviceName, deviceId);
    }

    /// <summary>
    /// Says this PC's name, creating its folders when missing. The <c>device.json</c> there is read once per run and
    /// written only when it says something else (a renamed PC whose name kept its length was not corrected, review 27
    /// S4); then what it says is remembered.
    /// </summary>
    public Task AnnounceAsync(CancellationToken cancellationToken) => Guarded(async () =>
    {
        var json = JsonSerializer.Serialize(new FolderInbox.DeviceInfo(_deviceName), FolderInbox.Json);
        if (_announced == json)
        {
            return true;
        }

        var mine = (await MyFolderAsync(create: true, cancellationToken).ConfigureAwait(false))!;
        var existing = (await _files.ListChildrenAsync(mine, cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(entry => !entry.IsFolder && entry.Name == FolderInbox.DeviceFile);
        if (existing is { Size: > 0 and < 4096 } && await ReadTextAsync(existing.Id, cancellationToken).ConfigureAwait(false) == json)
        {
            _announced = json;
            return true;
        }

        var bytes = Encoding.UTF8.GetBytes(json);
        using var content = new MemoryStream(bytes);
        await _files.UploadAsync(mine, FolderInbox.DeviceFile, "application/json", content, bytes.Length, existing?.Id, null, cancellationToken).ConfigureAwait(false);
        _announced = json;
        return true;
    });

    public Task<bool> WithdrawAsync(CancellationToken cancellationToken) => Guarded(async () =>
    {
        var removed = false;
        if (await MyFolderAsync(create: false, cancellationToken).ConfigureAwait(false) is { } mine)
        {
            foreach (var file in (await _files.ListChildrenAsync(mine, cancellationToken).ConfigureAwait(false)).Where(entry => !entry.IsFolder && entry.Name == FolderInbox.DeviceFile))
            {
                await _files.DeleteEntryAsync(file.Id, cancellationToken).ConfigureAwait(false);
                removed = true;
            }
        }

        _announced = null;
        return removed;
    });

    public Task<IReadOnlyList<InboxDevice>> DevicesAsync(CancellationToken cancellationToken) => Guarded<IReadOnlyList<InboxDevice>>(async () =>
    {
        var devices = new List<InboxDevice>();
        if (await InboxFolderAsync(create: false, cancellationToken).ConfigureAwait(false) is not { } inbox)
        {
            return devices;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in Oldest((await _files.ListChildrenAsync(inbox, cancellationToken).ConfigureAwait(false))
                     .Where(entry => entry.IsFolder && FolderInbox.IsDeviceId(entry.Name) && !string.Equals(entry.Name, _deviceId, StringComparison.OrdinalIgnoreCase))))
        {
            // Only a PC that says its name receives (review 27 S1); of two folders of one PC, the older.
            if (seen.Add(folder.Name) && await NameInAsync(folder.Id, folder.Name, cancellationToken).ConfigureAwait(false) is { } name)
            {
                devices.Add(new InboxDevice(folder.Name.ToLowerInvariant(), name));
            }
        }

        return devices.OrderBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    });

    public Task<string> SendAsync(string targetId, string filePath, CancellationToken cancellationToken) => Guarded(async () =>
    {
        if (!FolderInbox.IsDeviceId(targetId) || string.Equals(targetId, _deviceId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InboxSendException(SendProblem.NotAnotherPc, "That is no other PC of this account.");
        }

        var source = new FileInfo(filePath);
        if (!source.Exists)
        {
            throw new InboxSendException(SendProblem.NotAFile, "Only files can be sent, not folders.");
        }

        if (source.Length > FolderInbox.MaxFileSize)
        {
            throw new InboxSendException(SendProblem.TooLarge, $"{source.Name} is larger than 2 GB.");
        }

        if (await DeviceFolderAsync(targetId, cancellationToken).ConfigureAwait(false) is not { } target
            || !(await _files.ListChildrenAsync(target, cancellationToken).ConfigureAwait(false)).Any(entry => !entry.IsFolder && entry.Name == FolderInbox.DeviceFile))
        {
            // No inbox, or one whose PC took its name back when it stopped receiving: nothing sent there would arrive.
            throw new InboxSendException(SendProblem.NoInbox, "That PC is not receiving files.");
        }

        var inboxName = FolderInbox.InboxName(_deviceId, source.Name);
        await using var content = source.OpenRead();
        await _files.UploadAsync(target, inboxName, "application/octet-stream", content, source.Length, null, null, cancellationToken).ConfigureAwait(false);
        return inboxName[(inboxName.IndexOf('~') + 1)..];
    });

    public Task<IReadOnlyList<InboxFile>> PendingAsync(Func<string, bool>? skip, CancellationToken cancellationToken) => Guarded<IReadOnlyList<InboxFile>>(async () =>
    {
        var files = new List<InboxFile>();
        if (await MyFolderAsync(create: false, cancellationToken).ConfigureAwait(false) is not { } mine)
        {
            return files;
        }

        long taken = 0;
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in (await _files.ListChildrenAsync(mine, cancellationToken).ConfigureAwait(false)).OrderBy(entry => entry.ModifiedTime))
        {
            if (entry.IsFolder
                || entry.IsGoogleFormat
                || entry.Size is not { } size
                || size > FolderInbox.MaxFileSize
                || skip?.Invoke(entry.Id) == true
                || !FolderInbox.TryReadInboxName(entry.Name, out var sender, out var plain))
            {
                continue; // device.json, something this app did not put there, or a file passed over
            }

            if (files.Count > 0 && taken + size > FolderInbox.MaxBytesPerLook)
            {
                break;
            }

            if (!names.TryGetValue(sender, out var senderName))
            {
                senderName = names[sender] = await NameOfDeviceAsync(sender, null, cancellationToken).ConfigureAwait(false);
            }

            taken += size;
            files.Add(new InboxFile(entry.Id, plain, sender, senderName, size));
            if (files.Count == FolderInbox.MaxPerLook)
            {
                break;
            }
        }

        return files;
    });

    public async Task<(string Path, bool Marked, bool Cleared)> ReceiveAsync(InboxFile file, string destinationFolder, Func<string, bool>? mark, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);
        Directory.CreateDirectory(destinationFolder);
        var destination = FolderInbox.Unique(destinationFolder, FolderInbox.PlainName(file.Name));
        var arriving = destination + FolderInbox.PartSuffix;
        bool marked;
        try
        {
            await using (var stream = File.Create(arriving))
            {
                await _files.DownloadAsync(file.Path, stream, null, cancellationToken).ConfigureAwait(false);
            }

            marked = mark?.Invoke(arriving) ?? false;
            File.Move(arriving, destination);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DriveException or HttpRequestException or OperationCanceledException)
        {
            try
            {
                File.Delete(arriving); // nothing half-arrived stays behind
            }
            catch (Exception again) when (again is IOException or UnauthorizedAccessException)
            {
                // A .part file, which nothing opens.
            }

            throw;
        }

        bool cleared;
        try
        {
            await _files.DeleteEntryAsync(file.Path, cancellationToken).ConfigureAwait(false);
            cleared = true;
        }
        catch (Exception ex) when (ex is DriveException or HttpRequestException or IOException)
        {
            AppLog.Default.Warning("Inbox", $"A delivered file could not be removed from Drive: {ex.Message}");
            cleared = false; // the caller remembers it, so it is not delivered twice in this run
        }

        return (destination, marked, cleared);
    }

    public Task<string> NameOfDeviceAsync(string id, string? stored, CancellationToken cancellationToken) => Guarded(async () =>
    {
        if (FolderInbox.IsDeviceId(id)
            && await DeviceFolderAsync(id, cancellationToken).ConfigureAwait(false) is { } folder
            && await NameInAsync(folder, id, cancellationToken).ConfigureAwait(false) is { } name)
        {
            return name;
        }

        return FolderInbox.ShownName(stored, FolderInbox.IsDeviceId(id) ? id : "????????");
    });

    /// <summary>
    /// The name a PC says in its folder, made fit to show (the id's start when it cannot be read), or
    /// <see langword="null"/> when it says none: a PC that does not receive.
    /// </summary>
    private async Task<string?> NameInAsync(string deviceFolderId, string id, CancellationToken cancellationToken)
    {
        var file = (await _files.ListChildrenAsync(deviceFolderId, cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(entry => !entry.IsFolder && entry.Name == FolderInbox.DeviceFile);
        if (file is null)
        {
            return null;
        }

        try
        {
            if (file.Size is > 0 and < 4096 && await ReadTextAsync(file.Id, cancellationToken).ConfigureAwait(false) is { } json)
            {
                return FolderInbox.ShownName(JsonSerializer.Deserialize<FolderInbox.DeviceInfo>(json, FolderInbox.Json)?.Name, id);
            }
        }
        catch (Exception ex) when (ex is JsonException or DriveException or HttpRequestException or IOException)
        {
            // Unnamed.
        }

        return id[..8];
    }

    private async Task<string> ReadTextAsync(string fileId, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await _files.DownloadAsync(fileId, buffer, null, cancellationToken).ConfigureAwait(false);
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private async Task<string?> InboxFolderAsync(bool create, CancellationToken cancellationToken)
    {
        if (_inboxId is not null)
        {
            return _inboxId;
        }

        var app = await _files.AppFolderIdAsync(cancellationToken).ConfigureAwait(false);
        var found = await InboxFoldersAsync(app, cancellationToken).ConfigureAwait(false);
        if (found.Count == 0)
        {
            if (!create)
            {
                return null;
            }

            // Another PC may make one at the same moment: everyone keeps the older, and one just made in vain goes.
            var made = await _files.CreateFolderAsync(app, FolderInbox.FolderName, cancellationToken).ConfigureAwait(false);
            found = await InboxFoldersAsync(app, cancellationToken).ConfigureAwait(false);
            if (found.Count > 0 && found[0].Id != made.Id)
            {
                try
                {
                    await _files.DeleteEntryAsync(made.Id, cancellationToken).ConfigureAwait(false);
                    found.RemoveAll(entry => entry.Id == made.Id);
                }
                catch (Exception ex) when (ex is DriveException or HttpRequestException or IOException or NotSupportedException)
                {
                    // An empty extra folder is harmless: the older one is used.
                }
            }
            else if (found.Count == 0)
            {
                found = [made];
            }
        }

        if (found.Count > 1)
        {
            AppLog.Default.Warning("Inbox", $"{found.Count} \"{FolderInbox.FolderName}\" folders are on Drive; the oldest is used");
        }

        return _inboxId = found[0].Id;
    }

    private async Task<List<DriveEntry>> InboxFoldersAsync(string appFolderId, CancellationToken cancellationToken) =>
        Oldest((await _files.ListChildrenAsync(appFolderId, cancellationToken).ConfigureAwait(false)).Where(entry => entry.IsFolder && entry.Name == FolderInbox.FolderName));

    private async Task<string?> MyFolderAsync(bool create, CancellationToken cancellationToken)
    {
        if (_myFolderId is not null)
        {
            return _myFolderId;
        }

        if (await InboxFolderAsync(create, cancellationToken).ConfigureAwait(false) is not { } inbox)
        {
            return null;
        }

        var found = await DeviceFolderInAsync(inbox, _deviceId, cancellationToken).ConfigureAwait(false);
        if (found is null && !create)
        {
            return null;
        }

        return _myFolderId = found ?? (await _files.CreateFolderAsync(inbox, _deviceId, cancellationToken).ConfigureAwait(false)).Id;
    }

    private async Task<string?> DeviceFolderAsync(string deviceId, CancellationToken cancellationToken) =>
        await InboxFolderAsync(create: false, cancellationToken).ConfigureAwait(false) is { } inbox
            ? await DeviceFolderInAsync(inbox, deviceId, cancellationToken).ConfigureAwait(false)
            : null;

    private async Task<string?> DeviceFolderInAsync(string inboxId, string deviceId, CancellationToken cancellationToken) =>
        Oldest((await _files.ListChildrenAsync(inboxId, cancellationToken).ConfigureAwait(false))
                .Where(entry => entry.IsFolder && string.Equals(entry.Name, deviceId, StringComparison.OrdinalIgnoreCase)))
            .FirstOrDefault()?.Id;

    /// <summary>The oldest first, by when they were made and then by id, so that every PC picks the same one.</summary>
    private static List<DriveEntry> Oldest(IEnumerable<DriveEntry> entries) =>
        entries.OrderBy(entry => entry.CreatedTime ?? entry.ModifiedTime ?? DateTimeOffset.MaxValue).ThenBy(entry => entry.Id, StringComparer.Ordinal).ToList();

    /// <summary>A call whose "not found" (a folder deleted on Drive) makes the next call look the folders up again.</summary>
    private async Task<T> Guarded<T>(Func<Task<T>> call)
    {
        try
        {
            return await call().ConfigureAwait(false);
        }
        catch (DriveException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            _inboxId = _myFolderId = _announced = null;
            AppLog.Default.Info("Inbox", "A folder of the inbox is gone on Drive; it is looked up again at the next look");
            throw;
        }
    }
}
