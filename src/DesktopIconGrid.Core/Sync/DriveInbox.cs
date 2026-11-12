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
public sealed class DriveInbox : IInbox
{
    private readonly IDriveFiles _files;
    private readonly string _deviceId;
    private readonly string _deviceName;
    private string? _inboxId;
    private string? _myFolderId;

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

    public async Task AnnounceAsync(CancellationToken cancellationToken)
    {
        var mine = await MyFolderAsync(create: true, cancellationToken).ConfigureAwait(false);
        var json = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new FolderInbox.DeviceInfo(_deviceName), FolderInbox.Json));
        var existing = (await _files.ListChildrenAsync(mine!, cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(entry => !entry.IsFolder && entry.Name == FolderInbox.DeviceFile);
        if (existing is not null && existing.Size == json.Length)
        {
            return; // said already (a renamed PC of the same name length is corrected at the next change of length; names rarely change)
        }

        using var content = new MemoryStream(json);
        await _files.UploadAsync(mine!, FolderInbox.DeviceFile, "application/json", content, json.Length, existing?.Id, null, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<InboxDevice>> DevicesAsync(CancellationToken cancellationToken)
    {
        var devices = new List<InboxDevice>();
        if (await InboxFolderAsync(create: false, cancellationToken).ConfigureAwait(false) is not { } inbox)
        {
            return devices;
        }

        foreach (var folder in await _files.ListChildrenAsync(inbox, cancellationToken).ConfigureAwait(false))
        {
            if (folder.IsFolder && FolderInbox.IsDeviceId(folder.Name) && !string.Equals(folder.Name, _deviceId, StringComparison.OrdinalIgnoreCase))
            {
                devices.Add(new InboxDevice(folder.Name.ToLowerInvariant(), await NameInAsync(folder.Id, folder.Name, cancellationToken).ConfigureAwait(false)));
            }
        }

        return devices.OrderBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public async Task<string> SendAsync(string targetId, string filePath, CancellationToken cancellationToken)
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

        if (await DeviceFolderAsync(targetId, cancellationToken).ConfigureAwait(false) is not { } target)
        {
            throw new InboxSendException(SendProblem.NoInbox, "That PC has no inbox on Drive (yet).");
        }

        var inboxName = FolderInbox.InboxName(_deviceId, source.Name);
        await using var content = source.OpenRead();
        await _files.UploadAsync(target, inboxName, "application/octet-stream", content, source.Length, null, null, cancellationToken).ConfigureAwait(false);
        return inboxName[(inboxName.IndexOf('~') + 1)..];
    }

    public async Task<IReadOnlyList<InboxFile>> PendingAsync(Func<string, bool>? skip, CancellationToken cancellationToken)
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
    }

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

    public async Task<string> NameOfDeviceAsync(string id, string? stored, CancellationToken cancellationToken)
    {
        if (FolderInbox.IsDeviceId(id) && await DeviceFolderAsync(id, cancellationToken).ConfigureAwait(false) is { } folder)
        {
            return await NameInAsync(folder, id, cancellationToken).ConfigureAwait(false);
        }

        return FolderInbox.ShownName(stored, FolderInbox.IsDeviceId(id) ? id : "????????");
    }

    private async Task<string> NameInAsync(string deviceFolderId, string id, CancellationToken cancellationToken)
    {
        try
        {
            var file = (await _files.ListChildrenAsync(deviceFolderId, cancellationToken).ConfigureAwait(false))
                .FirstOrDefault(entry => !entry.IsFolder && entry.Name == FolderInbox.DeviceFile && entry.Size is > 0 and < 4096);
            if (file is not null)
            {
                using var buffer = new MemoryStream();
                await _files.DownloadAsync(file.Id, buffer, null, cancellationToken).ConfigureAwait(false);
                var info = JsonSerializer.Deserialize<FolderInbox.DeviceInfo>(buffer.ToArray(), FolderInbox.Json);
                return FolderInbox.ShownName(info?.Name, id);
            }
        }
        catch (Exception ex) when (ex is JsonException or DriveException or HttpRequestException or IOException)
        {
            // Unnamed.
        }

        return id[..8];
    }

    private async Task<string?> InboxFolderAsync(bool create, CancellationToken cancellationToken)
    {
        if (_inboxId is not null)
        {
            return _inboxId;
        }

        var app = await _files.AppFolderIdAsync(cancellationToken).ConfigureAwait(false);
        var found = (await _files.ListChildrenAsync(app, cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(entry => entry.IsFolder && entry.Name == FolderInbox.FolderName);
        if (found is null && !create)
        {
            return null;
        }

        return _inboxId = (found ?? await _files.CreateFolderAsync(app, FolderInbox.FolderName, cancellationToken).ConfigureAwait(false)).Id;
    }

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

        var found = (await _files.ListChildrenAsync(inbox, cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(entry => entry.IsFolder && string.Equals(entry.Name, _deviceId, StringComparison.OrdinalIgnoreCase));
        if (found is null && !create)
        {
            return null;
        }

        return _myFolderId = (found ?? await _files.CreateFolderAsync(inbox, _deviceId, cancellationToken).ConfigureAwait(false)).Id;
    }

    private async Task<string?> DeviceFolderAsync(string deviceId, CancellationToken cancellationToken)
    {
        if (await InboxFolderAsync(create: false, cancellationToken).ConfigureAwait(false) is not { } inbox)
        {
            return null;
        }

        return (await _files.ListChildrenAsync(inbox, cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(entry => entry.IsFolder && string.Equals(entry.Name, deviceId, StringComparison.OrdinalIgnoreCase))?.Id;
    }
}
