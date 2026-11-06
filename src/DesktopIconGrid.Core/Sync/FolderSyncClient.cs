using System.Globalization;
using System.Text;
using System.Text.Json;
using DesktopIconGrid.Core.Resources;
using DesktopIconGrid.Core.Storage;

namespace DesktopIconGrid.Core.Sync;

/// <summary>
/// The settings copies as <c>.json</c> files in a folder the user chose, usually one that OneDrive, Dropbox, Google
/// Drive for desktop or a network share keeps in step across PCs: the same copies, device names, linked copy and
/// conflict rule as on Google Drive, without a sign-in.
/// </summary>
/// <remarks>
/// A copy's id is its file name. Its time is the file's last-write time, which the sync programs carry from PC to PC,
/// in place of Drive's clock; who saved it is read from the copy itself. Conflict copies the sync programs make
/// (<c>Work-PC2.json</c>, <c>Work (1).json</c>) are listed under their file names, so they can be told apart, loaded
/// or deleted. A file that cannot be read at the moment (still being synced) is listed without a device, so it never
/// looks like this device's own copy and is never overwritten as one.
/// </remarks>
public sealed class FolderSyncClient : IDriveClient
{
    /// <summary>Files bigger than this are not read for the listing; a copy is a few kilobytes.</summary>
    public const long MaxCopySize = 16 * 1024 * 1024;

    /// <summary>Set ("true") on a listed copy that could not be read (see <see cref="RemoteProfile.Unreadable"/>).</summary>
    public const string UnreadableProperty = "unreadable";

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    public FolderSyncClient(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        Folder = Path.GetFullPath(folder.Trim());
    }

    public string Folder { get; }

    public Task<DriveUser> GetUserAsync(CancellationToken cancellationToken) => Task.FromResult(new DriveUser(null, Folder));

    public Task<IReadOnlyList<DriveFile>> ListFilesAsync(CancellationToken cancellationToken) =>
        Task.Run<IReadOnlyList<DriveFile>>(() => List(cancellationToken), cancellationToken);

    public Task<DriveFile> UploadJsonAsync(
        string fileName, string json, IReadOnlyDictionary<string, string> appProperties, string? existingFileId, CancellationToken cancellationToken) =>
        Task.Run(() => Write(fileName, json, appProperties, existingFileId), cancellationToken);

    public Task<string> DownloadTextAsync(string fileId, CancellationToken cancellationToken) =>
        Task.Run(() => Read(PathOf(fileId)), cancellationToken);

    public Task DeleteAsync(string fileId, CancellationToken cancellationToken) =>
        Task.Run(() => File.Delete(PathOf(fileId)), cancellationToken);

    /// <summary>
    /// The name a copy is listed under: the name inside it when the file is named after it, otherwise the file name
    /// (a conflict copy such as <c>Work (1).json</c> says "Work" inside).
    /// </summary>
    public static string DisplayName(string fileName, string? nameInside)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        var stem = fileName.EndsWith(RemoteProfile.FileExtension, StringComparison.OrdinalIgnoreCase)
            ? fileName[..^RemoteProfile.FileExtension.Length]
            : fileName;
        return !string.IsNullOrWhiteSpace(nameInside) && string.Equals(FileNames.Safe(nameInside), stem, StringComparison.OrdinalIgnoreCase)
            ? nameInside.Trim()
            : stem;
    }

    private List<DriveFile> List(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(Folder))
        {
            throw new DirectoryNotFoundException(string.Format(CultureInfo.CurrentCulture, CoreStrings.Sync_FolderMissing, Folder));
        }

        var files = new List<DriveFile>();
        foreach (var path in Directory.EnumerateFiles(Folder, "*" + RemoteProfile.FileExtension))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var info = new FileInfo(path);
                if (!info.Name.EndsWith(RemoteProfile.FileExtension, StringComparison.OrdinalIgnoreCase) || info.Length > MaxCopySize)
                {
                    continue;
                }

                var properties = ReadProperties(info);
                if (properties is not null)
                {
                    files.Add(new DriveFile(info.Name, info.Name, new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero), info.Length, properties));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Gone or locked between the listing and the look at it (a sync program at work): the others are still listed.
            }
        }

        return files;
    }

    /// <summary>
    /// What the listing shows of a copy, read from the copy; <see langword="null"/> for JSON that is not a copy. A file
    /// that cannot be read or parsed right now is kept, without a device.
    /// </summary>
    private static Dictionary<string, string>? ReadProperties(FileInfo info)
    {
        var properties = new Dictionary<string, string> { ["app"] = "DesktopIconGrid" };
        try
        {
            using var stream = new FileStream(info.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var document = JsonDocument.Parse(stream);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("layout", out var layout) || layout.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            string? Text(string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            properties["profile"] = DisplayName(info.Name, Text("name"));
            Add(properties, "device", Text("savedBy"));
            Add(properties, "deviceId", Text("savedById"));
            Add(properties, "savedAt", Text("savedAt"));
            Add(properties, "revision", Text("revision"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            properties["profile"] = DisplayName(info.Name, null);
            properties[UnreadableProperty] = "true";
        }

        return properties;
    }

    private DriveFile Write(string fileName, string json, IReadOnlyDictionary<string, string> appProperties, string? existingFileId)
    {
        Directory.CreateDirectory(Folder);
        var name = existingFileId ?? FreeFileName(fileName);
        var path = PathOf(name);
        var tempPath = Path.Combine(Folder, "~" + Path.GetRandomFileName() + ".tmp");
        try
        {
            using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                stream.Write(Utf8.GetBytes(json));
                stream.Flush(flushToDisk: true);
            }

            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Left for the next write's directory listing to ignore (not a .json file).
                }
            }
        }

        var info = new FileInfo(path);
        var properties = new Dictionary<string, string>(appProperties);
        properties["profile"] = DisplayName(name, appProperties.TryGetValue("profile", out var inside) ? inside : null);
        return new DriveFile(name, name, new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero), info.Length, properties);
    }

    /// <summary>
    /// A file name for a new copy: the copy's name made safe for Windows, numbered when another copy's name became the
    /// same file name (<c>a:b</c> and <c>a?b</c> are both <c>a_b.json</c>).
    /// </summary>
    private string FreeFileName(string fileName)
    {
        var stem = fileName.EndsWith(RemoteProfile.FileExtension, StringComparison.OrdinalIgnoreCase)
            ? fileName[..^RemoteProfile.FileExtension.Length]
            : fileName;
        var safe = FileNames.Safe(stem);
        var candidate = safe + RemoteProfile.FileExtension;
        for (var number = 2; File.Exists(Path.Combine(Folder, candidate)); number++)
        {
            candidate = $"{safe} ({number}){RemoteProfile.FileExtension}";
        }

        return candidate;
    }

    private static string Read(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Utf8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    /// <summary>The path of a copy in the folder; anything but a plain file name is refused.</summary>
    private string PathOf(string fileId)
    {
        if (string.IsNullOrWhiteSpace(fileId) || fileId.IndexOfAny(['/', '\\']) >= 0 || fileId is "." or ".." || fileId.Contains(':', StringComparison.Ordinal))
        {
            throw new ArgumentException($"\"{fileId}\" is not a copy in the sync folder.", nameof(fileId));
        }

        return Path.Combine(Folder, fileId);
    }

    private static void Add(Dictionary<string, string> properties, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            properties[key] = value;
        }
    }
}
