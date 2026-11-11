using System.Text.Json;
using DesktopIconGrid.Core.Diagnostics;
using DesktopIconGrid.Core.Storage;

namespace DesktopIconGrid.Core.Sync;

/// <summary>Another PC that shares the sync folder and can be sent files.</summary>
public sealed record InboxDevice(string Id, string Name);

/// <summary>Why a file could not be sent, for a message in the user's language.</summary>
public enum SendProblem
{
    /// <summary>The target is this PC itself, or no device id.</summary>
    NotAnotherPc,

    /// <summary>The target PC has no inbox in the sync folder: it does not take part (yet).</summary>
    NoInbox,

    /// <summary>A folder, or a file that is gone.</summary>
    NotAFile,

    /// <summary>Larger than <see cref="FolderInbox.MaxFileSize"/>.</summary>
    TooLarge,
}

/// <summary>A file was not sent, for a reason the user can act on.</summary>
public sealed class InboxSendException(SendProblem problem, string message) : IOException(message)
{
    public SendProblem Problem { get; } = problem;
}

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

    /// <summary>The most bytes taken in one look (the first file always counts, whatever its size).</summary>
    public const long MaxBytesPerLook = 4L * 1024 * 1024 * 1024;

    /// <summary>What receiving leaves free on the destination drive.</summary>
    public const long FreeSpaceFloor = 1L * 1024 * 1024 * 1024;

    /// <summary>Whether a file of <paramref name="fileSize"/> may be taken with <paramref name="freeBytes"/> free on the destination drive.</summary>
    public static bool HasRoom(long freeBytes, long fileSize) => freeBytes - fileSize >= FreeSpaceFloor;

    private const string PartSuffix = ".part";

    /// <summary>What an inbox file is renamed to when it was delivered but could not be deleted, so it is not delivered again.</summary>
    private const string TakenSuffix = ".taken";

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

    /// <summary>
    /// Whether a PC takes part (announces itself and takes what is sent to it): only with a sync folder, and only when
    /// its user asked for it, by the setting or by putting a Send to PC icon on the bar on this PC (the owner's decision
    /// on review 24 I1; an icon that came with a synced bar does not count, see <see cref="IconMark"/>). Without either the sync folder is left as it is and nothing arrives.
    /// </summary>
    public static bool TakesPart(bool usesSyncFolder, bool receiveSetting, bool hasSendIcon) =>
        usesSyncFolder && (receiveSetting || hasSendIcon);

    /// <summary>
    /// The local mark of a Send to PC icon that was added or saved on this PC, for <see cref="SpecialIcons.CommandTrust"/>
    /// (kept per PC, never synced). Only a marked icon turns receiving on: the bar is synced, and an icon that arrived
    /// from another PC must not make this one take files (the owner's decision).
    /// </summary>
    public static string IconMark(Guid iconId) =>
        Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"send-to-device-icon/1\n{iconId:N}")));

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
            throw new InboxSendException(SendProblem.NotAnotherPc, "That is no other PC of this sync folder.");
        }

        var target = Path.Combine(_root, targetId.ToLowerInvariant());
        if (!Directory.Exists(target))
        {
            throw new InboxSendException(SendProblem.NoInbox, "That PC has no inbox in the sync folder (yet).");
        }

        var source = new FileInfo(filePath);
        if (!source.Exists)
        {
            throw new InboxSendException(SendProblem.NotAFile, "Only files can be sent, not folders.");
        }

        if (source.Length > MaxFileSize)
        {
            throw new InboxSendException(SendProblem.TooLarge, $"{source.Name} is larger than 2 GB.");
        }

        var name = FileNames.Safe(source.Name);
        if (name.EndsWith(PartSuffix, StringComparison.OrdinalIgnoreCase) || name.EndsWith(TakenSuffix, StringComparison.OrdinalIgnoreCase))
        {
            name += "_"; // the inbox's own two endings (arriving, set aside): a file sent under one would never be delivered
        }

        var final = Unique(target, $"{_deviceId}~{name}");
        var part = final + PartSuffix; // a sync program may upload it half-written; the receiver skips .part
        File.Copy(source.FullName, part, overwrite: true);
        File.SetAttributes(part, FileAttributes.Normal); // read-only or hidden does not travel: the receiver must be able to mark, see and delete it
        File.Move(part, final);
        return name;
    }

    /// <summary>
    /// The files waiting for this PC (at most <see cref="MaxPerLook"/>), oldest first. <paramref name="skip"/> names
    /// inbox paths to pass over (delivered already but not removable); they do not use up the look's places.
    /// </summary>
    public IReadOnlyList<InboxFile> Pending(Func<string, bool>? skip = null)
    {
        var mine = Path.Combine(_root, _deviceId);
        var files = new List<InboxFile>();
        long taken = 0;
        try
        {
            if (!Directory.Exists(mine))
            {
                return files;
            }

            foreach (var info in new DirectoryInfo(mine).EnumerateFiles().OrderBy(file => file.LastWriteTimeUtc))
            {
                var separator = info.Name.IndexOf('~');
                if (info.Name.EndsWith(TakenSuffix, StringComparison.OrdinalIgnoreCase))
                {
                    RemoveOldTaken(info);
                    continue;
                }

                if (info.Name.EndsWith(PartSuffix, StringComparison.OrdinalIgnoreCase)
                    || skip?.Invoke(info.FullName) == true
                    || separator != 32
                    || !IsDeviceId(info.Name[..separator])
                    || info.Length > MaxFileSize)
                {
                    continue; // device.json, a file still arriving, or something this app did not put there
                }

                var sender = info.Name[..separator].ToLowerInvariant();
                if (files.Count > 0 && taken + info.Length > MaxBytesPerLook)
                {
                    break; // the rest waits for the next look
                }

                taken += info.Length;
                files.Add(new InboxFile(info.FullName, PlainName(info.Name[(separator + 1)..]), sender, NameOf(Path.Combine(_root, sender), sender), info.Length));
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
    /// Delivers a waiting file into <paramref name="destinationFolder"/> under a name that is free there, once
    /// (review 24 R1): it is copied under a temporary name, made a plain file (a read-only or hidden one could be
    /// neither marked nor seen), handed to <paramref name="mark"/> (the mark of a downloaded file), and only then
    /// given its name. The inbox copy is deleted, or renamed so that it is never delivered again when it cannot be.
    /// </summary>
    /// <returns>Where the file is now, whether it was marked, and whether its inbox copy is gone (deleted or set aside).</returns>
    public static (string Path, bool Marked, bool Cleared) Receive(InboxFile file, string destinationFolder, Func<string, bool>? mark = null)
    {
        ArgumentNullException.ThrowIfNull(file);
        Directory.CreateDirectory(destinationFolder);
        var destination = Unique(destinationFolder, PlainName(file.Name));
        var arriving = destination + PartSuffix;
        bool marked;
        try
        {
            File.Copy(file.Path, arriving, overwrite: true);
            File.SetAttributes(arriving, FileAttributes.Normal);
            marked = mark?.Invoke(arriving) ?? false;
            File.Move(arriving, destination);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nothing half-arrived stays behind: the next look delivers it under the same name, not "name (2)" (review 25 L2).
            try
            {
                File.SetAttributes(arriving, FileAttributes.Normal);
                File.Delete(arriving);
            }
            catch (Exception again) when (again is IOException or UnauthorizedAccessException)
            {
                // Held by a scanner: it is a .part file, which nothing opens and Unique() steps around.
            }

            throw;
        }

        return (destination, marked, Clear(file.Path));
    }

    /// <summary>A delivered file that could only be set aside is removed once it is a day old, when it can be.</summary>
    private static void RemoveOldTaken(FileInfo taken)
    {
        if (DateTime.UtcNow - taken.LastWriteTimeUtc < TimeSpan.FromDays(1))
        {
            return;
        }

        try
        {
            taken.Attributes = FileAttributes.Normal;
            taken.Delete();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Still not removable: it stays set aside.
        }
    }

    /// <summary>Deletes a delivered inbox file; when that fails, renames it out of the way. False when neither worked.</summary>
    private static bool Clear(string path)
    {
        try
        {
            File.SetAttributes(path, FileAttributes.Normal);
            File.Delete(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try
            {
                File.Move(path, path + TakenSuffix, overwrite: true);
                return true;
            }
            catch (Exception again) when (again is IOException or UnauthorizedAccessException)
            {
                AppLog.Default.Warning("Inbox", $"A delivered file could not be removed from the inbox: {again.Message}");
                return false;
            }
        }
    }

    /// <summary>
    /// A received file's name: safe for Windows, never a path, and never <c>desktop.ini</c>, which Explorer would read
    /// as the Downloads folder's own settings.
    /// </summary>
    private static string PlainName(string name)
    {
        var safe = FileNames.Safe(name);
        return string.Equals(safe, "desktop.ini", StringComparison.OrdinalIgnoreCase) ? safe + ".received" : safe;
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

    /// <summary>
    /// The name of the PC <paramref name="id"/> as it says itself in its inbox now, or <paramref name="stored"/> (made
    /// fit to show) when it has none there. A name kept with a synced icon is not trusted to be current or clean.
    /// </summary>
    public string NameOfDevice(string id, string? stored) =>
        IsDeviceId(id) && File.Exists(Path.Combine(_root, id.ToLowerInvariant(), DeviceFile))
            ? NameOf(Path.Combine(_root, id.ToLowerInvariant()), id)
            : ShownName(stored, IsDeviceId(id) ? id : "????????");

    /// <summary>A device name fit to show: no control or invisible characters, at most 64, the id's start when nothing is left.</summary>
    public static string ShownName(string? name, string id) => CleanName(name, id);

    private static string CleanName(string? name, string id)
    {
        var kept = new string((name ?? string.Empty).Where(c => !char.IsControl(c) && char.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.Format).ToArray()).Trim();
        return kept.Length == 0 ? id[..8] : kept.Length > SyncSettings.MaxDeviceNameLength ? kept[..SyncSettings.MaxDeviceNameLength] : kept;
    }

    private sealed record DeviceInfo(string? Name);
}
