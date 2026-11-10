using System.Text.Json;
using DesktopIconGrid.Core.Diagnostics;
using DesktopIconGrid.Core.Storage;

namespace DesktopIconGrid.Core.Sync;

/// <summary>Another PC that shares the sync folder and can be sent files.</summary>
public sealed record InboxDevice(string Id, string Name);

/// <summary>A file waiting in this PC's inbox.</summary>
/// <param name="Path">Where it lies in the sync folder.</param>
/// <param name="Name">The name it was sent under, made safe for Windows; never a path.</param>
public sealed record InboxFile(string Path, string Name, string SenderId, string SenderName, long Size);

/// <summary>
/// Sending files between the PCs that share a sync folder (0.10.0): each PC has a folder
/// <c>inbox\&lt;device id&gt;</c> with a <c>device.json</c> that says its name; a sent file is copied there as
/// <c>&lt;sender id&gt;~&lt;name&gt;</c>, and the receiving PC moves it to its Downloads folder. The sync folder is the
/// trust boundary, as for the layout copies: whoever can write there can send. Nothing received is opened, names are
/// made safe and never treated as paths, and the caller marks the file as downloaded.
/// </summary>
public sealed class FolderInbox
{
    public const string FolderName = "inbox";
    public const string DeviceFile = "device.json";

    /// <summary>The largest file sent or received; a sync program would take hours over more.</summary>
    public const long MaxFileSize = 2L * 1024 * 1024 * 1024;

    /// <summary>The most files taken in one look, so a flooded inbox cannot fill the disk in one go.</summary>
    public const int MaxPerLook = 20;

    private const string PartSuffix = ".part";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly string _root;
    private readonly string _deviceId;
    private readonly string _deviceName;

    public FolderInbox(string syncFolder, string deviceId, string deviceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(syncFolder);
        if (!IsDeviceId(deviceId))
        {
            throw new ArgumentException("Not a device id.", nameof(deviceId));
        }

        _root = Path.Combine(syncFolder, FolderName);
        _deviceId = deviceId;
        _deviceName = CleanName(deviceName, deviceId);
    }

    /// <summary>A device id as the app makes them: 32 hex digits. Anything else is never used as a folder name.</summary>
    public static bool IsDeviceId(string? id) => id is { Length: 32 } && id.All(char.IsAsciiHexDigit);

    /// <summary>Creates this PC's inbox and says its name there, so other PCs can offer it.</summary>
    public void Announce()
    {
        var folder = Directory.CreateDirectory(Path.Combine(_root, _deviceId)).FullName;
        var file = Path.Combine(folder, DeviceFile);
        var json = JsonSerializer.Serialize(new DeviceInfo(_deviceName), Json);
        if (!File.Exists(file) || File.ReadAllText(file) != json)
        {
            File.WriteAllText(file, json);
        }
    }

    /// <summary>The other PCs with an inbox in this folder, by name.</summary>
    public IReadOnlyList<InboxDevice> Devices()
    {
        var devices = new List<InboxDevice>();
        try
        {
            if (!Directory.Exists(_root))
            {
                return devices;
            }

            foreach (var folder in Directory.EnumerateDirectories(_root))
            {
                var id = Path.GetFileName(folder);
                if (IsDeviceId(id) && !string.Equals(id, _deviceId, StringComparison.OrdinalIgnoreCase))
                {
                    devices.Add(new InboxDevice(id.ToLowerInvariant(), NameOf(folder, id)));
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Default.Warning("Inbox", $"The other PCs could not be listed: {ex.Message}");
        }

        return devices.OrderBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>Copies <paramref name="filePath"/> into the inbox of <paramref name="targetId"/>; the name it has there.</summary>
    public string Send(string targetId, string filePath)
    {
        if (!IsDeviceId(targetId) || string.Equals(targetId, _deviceId, StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException("That is no other PC of this sync folder.");
        }

        var target = Path.Combine(_root, targetId.ToLowerInvariant());
        if (!Directory.Exists(target))
        {
            throw new IOException("That PC has no inbox in the sync folder (yet).");
        }

        var source = new FileInfo(filePath);
        if (!source.Exists)
        {
            throw new FileNotFoundException("Only files can be sent, not folders.", filePath);
        }

        if (source.Length > MaxFileSize)
        {
            throw new IOException($"{source.Name} is larger than 2 GB.");
        }

        var name = FileNames.Safe(source.Name);
        var final = Unique(target, $"{_deviceId}~{name}");
        var part = final + PartSuffix; // a sync program may upload it half-written; the receiver skips .part
        File.Copy(source.FullName, part, overwrite: true);
        File.Move(part, final);
        return name;
    }

    /// <summary>The files waiting for this PC (at most <see cref="MaxPerLook"/>), oldest first.</summary>
    public IReadOnlyList<InboxFile> Pending()
    {
        var mine = Path.Combine(_root, _deviceId);
        var files = new List<InboxFile>();
        try
        {
            if (!Directory.Exists(mine))
            {
                return files;
            }

            foreach (var info in new DirectoryInfo(mine).EnumerateFiles().OrderBy(file => file.LastWriteTimeUtc))
            {
                var separator = info.Name.IndexOf('~');
                if (info.Name.EndsWith(PartSuffix, StringComparison.OrdinalIgnoreCase)
                    || separator != 32
                    || !IsDeviceId(info.Name[..separator])
                    || info.Length > MaxFileSize)
                {
                    continue; // device.json, a file still arriving, or something this app did not put there
                }

                var sender = info.Name[..separator].ToLowerInvariant();
                files.Add(new InboxFile(info.FullName, FileNames.Safe(info.Name[(separator + 1)..]), sender, NameOf(Path.Combine(_root, sender), sender), info.Length));
                if (files.Count == MaxPerLook)
                {
                    break;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Default.Warning("Inbox", $"The inbox could not be read: {ex.Message}");
        }

        return files;
    }

    /// <summary>
    /// Moves a waiting file into <paramref name="destinationFolder"/> under a name that is free there, and returns where
    /// it is now. The caller marks it as downloaded.
    /// </summary>
    public static string Receive(InboxFile file, string destinationFolder)
    {
        ArgumentNullException.ThrowIfNull(file);
        Directory.CreateDirectory(destinationFolder);
        var destination = Unique(destinationFolder, FileNames.Safe(file.Name));
        File.Copy(file.Path, destination, overwrite: false);
        File.Delete(file.Path);
        return destination;
    }

    private static string Unique(string folder, string name)
    {
        var path = Path.Combine(folder, name);
        var stem = Path.GetFileNameWithoutExtension(name);
        var extension = Path.GetExtension(name);
        for (var n = 2; File.Exists(path) || File.Exists(path + PartSuffix); n++)
        {
            path = Path.Combine(folder, $"{stem} ({n}){extension}");
        }

        return path;
    }

    private static string NameOf(string deviceFolder, string id)
    {
        try
        {
            var file = Path.Combine(deviceFolder, DeviceFile);
            if (File.Exists(file) && new FileInfo(file).Length < 4096)
            {
                return CleanName(JsonSerializer.Deserialize<DeviceInfo>(File.ReadAllText(file), Json)?.Name, id);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Unnamed.
        }

        return id[..8];
    }

    /// <summary>A device name fit to show: no control or invisible characters, at most 64, the id's start when nothing is left.</summary>
    private static string CleanName(string? name, string id)
    {
        var kept = new string((name ?? string.Empty).Where(c => !char.IsControl(c) && char.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.Format).ToArray()).Trim();
        return kept.Length == 0 ? id[..8] : kept.Length > SyncSettings.MaxDeviceNameLength ? kept[..SyncSettings.MaxDeviceNameLength] : kept;
    }

    private sealed record DeviceInfo(string? Name);
}
