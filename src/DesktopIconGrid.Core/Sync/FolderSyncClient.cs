using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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

    /// <summary>The name saved inside a listed copy (see <see cref="RemoteProfile.NameInside"/>).</summary>
    public const string NameInsideProperty = "nameInside";

    /// <summary>
    /// Listing property: whether the file name the copy holds (<see cref="SyncProfile.File"/>) is the file's own
    /// ("true"), or another ("false": a sync program's copy, or a renamed file). Absent for a copy that holds none
    /// (saved by 0.10.0 or older, or through Google Drive).
    /// </summary>
    public const string OwnFileProperty = "ownFile";

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
    /// (a conflict copy such as <c>Work (1).json</c> says "Work" inside). A name Windows does not allow in a file name
    /// counts with the number <see cref="FreeFileName"/> gives it when another copy's name became the same file name:
    /// "Home? desk" in <c>Home_ desk (2).json</c>. A name that needs no change gets a number only when its own file was
    /// taken by something that is not a copy of it; the listing as a whole decides that (see
    /// <see cref="ListNumberedOriginals"/>), and here <c>Work (2).json</c> is a sync program's conflict copy.
    /// </summary>
    public static string DisplayName(string fileName, string? nameInside)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        var stem = fileName.EndsWith(RemoteProfile.FileExtension, StringComparison.OrdinalIgnoreCase)
            ? fileName[..^RemoteProfile.FileExtension.Length]
            : fileName;
        if (string.IsNullOrWhiteSpace(nameInside))
        {
            return stem;
        }

        var safe = FileNames.Safe(nameInside);
        if (string.Equals(safe, stem, StringComparison.OrdinalIgnoreCase))
        {
            return nameInside.Trim();
        }

        var numbered = !string.Equals(safe, nameInside.Trim(), StringComparison.Ordinal)
                       && stem.StartsWith(safe + " (", StringComparison.OrdinalIgnoreCase)
                       && stem.EndsWith(')')
                       && int.TryParse(stem.AsSpan(safe.Length + 2, stem.Length - safe.Length - 3), System.Globalization.NumberStyles.None, CultureInfo.InvariantCulture, out var number)
                       && number >= 2;
        return numbered ? nameInside.Trim() : stem;
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

        ListNumberedOriginals(files);
        return files;
    }

    /// <summary>
    /// Which file is the original of each copy's name, as the listing as a whole says. The original of "N" is the file
    /// <c>Safe(N).json</c> that says "N" inside, or that cannot be read right now (it may well be that copy: a sync program
    /// at work, a new PC still downloading). Only without one is a numbered file the original: the lowest-numbered
    /// <c>Safe(N) (k).json</c> that says "N", which <see cref="FreeFileName"/> gave the copy when its own file name was
    /// taken (another program's <c>config.json</c>). Any other numbered file that says "N" is a sync program's copy of it
    /// (Google Drive's numbering, Explorer's Keep both) and is listed under its file name, so the start-up check names it:
    /// <c>Home_ desk (2).json</c> next to a readable <c>Home_ desk.json</c> that says "Home: desk" too.
    /// </summary>
    private static void ListNumberedOriginals(List<DriveFile> files)
    {
        var unreadable = files.Where(f => f.AppProperties.ContainsKey(UnreadableProperty)).Select(f => StemOf(f.Name)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var named = files.Where(f => f.AppProperties.TryGetValue(NameInsideProperty, out var inside) && !string.IsNullOrWhiteSpace(inside));
        foreach (var copies in named.GroupBy(f => f.AppProperties[NameInsideProperty].Trim(), StringComparer.OrdinalIgnoreCase))
        {
            var safe = FileNames.Safe(copies.Key);
            // A copy that says which file it was written to is settled already (ReadProperties); one in its own file is
            // the original, so no file that does not say is taken for it by its number.
            var hasOwnFile = unreadable.Contains(safe)
                             || copies.Any(f => f.AppProperties.TryGetValue(OwnFileProperty, out var own) ? own == "true" : string.Equals(StemOf(f.Name), safe, StringComparison.OrdinalIgnoreCase));
            var numbered = copies
                .Where(file => !file.AppProperties.ContainsKey(OwnFileProperty))
                .Select(file => (File: file, Number: NumberAfter(StemOf(file.Name), safe)))
                .Where(entry => entry.Number >= 2)
                .OrderBy(entry => entry.Number)
                .ToList();
            for (var k = 0; k < numbered.Count; k++)
            {
                if (numbered[k].File.AppProperties is IDictionary<string, string> properties)
                {
                    properties["profile"] = k == 0 && !hasOwnFile ? copies.Key : StemOf(numbered[k].File.Name);
                }
            }
        }
    }

    private static string StemOf(string fileName) =>
        fileName.EndsWith(RemoteProfile.FileExtension, StringComparison.OrdinalIgnoreCase) ? fileName[..^RemoteProfile.FileExtension.Length] : fileName;

    /// <summary>The <c>k</c> of a stem written <c>{safe} (k)</c>, or 0.</summary>
    internal static int NumberAfter(string stem, string safe) =>
        stem.StartsWith(safe + " (", StringComparison.OrdinalIgnoreCase) && stem.EndsWith(')')
        && int.TryParse(stem.AsSpan(safe.Length + 2, stem.Length - safe.Length - 3), NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            ? number
            : 0;

    /// <summary>
    /// What the listing shows of a copy, read from the copy; <see langword="null"/> for JSON that is not a copy. A file
    /// that cannot be read right now (locked by a sync program at work), or that starts like a copy and does not parse
    /// (one being written), is kept as unreadable, without a device. Another program's file that does not parse
    /// (settings with comments, an empty file) is no copy.
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
            // The copy says which file it was written to (0.11.0 on): in that file it is the original of its name,
            // whatever number the file has; in any other file it is a copy some program made of it. Without it the
            // file names are compared, and the listing as a whole decides (ListNumberedOriginals).
            if (Text("file") is { Length: > 0 } written)
            {
                var own = string.Equals(written, info.Name, StringComparison.OrdinalIgnoreCase);
                properties[OwnFileProperty] = own ? "true" : "false";
                properties["profile"] = own && !string.IsNullOrWhiteSpace(Text("name")) ? Text("name")!.Trim() : DisplayName(info.Name, null);
            }
            else
            {
                properties["profile"] = DisplayName(info.Name, Text("name"));
            }

            Add(properties, NameInsideProperty, Text("name"));
            Add(properties, "device", Text("savedBy"));
            Add(properties, "deviceId", Text("savedById"));
            Add(properties, "savedAt", Text("savedAt"));
            Add(properties, "revision", Text("revision"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException || (ex is JsonException && StartsLikeACopy(info)))
        {
            properties["profile"] = DisplayName(info.Name, null);
            properties[UnreadableProperty] = "true";
        }
        catch (JsonException)
        {
            return null;
        }

        return properties;
    }

    /// <summary>The properties a copy holds (<see cref="SyncProfile"/>), one of which comes first in any copy.</summary>
    private static readonly string[] CopyProperties = ["format", "file", "name", "savedAt", "savedBy", "savedById", "appVersion", "revision", "layout"];

    /// <summary>
    /// Whether the file starts as a copy does, as far as it has been written: <c>{</c>, then one of a copy's own properties
    /// (every version writes <c>"format"</c> first). A file that cannot be read now counts as one.
    /// </summary>
    private static bool StartsLikeACopy(FileInfo info)
    {
        try
        {
            using var stream = new FileStream(info.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var buffer = new byte[256];
            var length = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
            var text = Utf8.GetString(buffer, 0, length).TrimStart('\uFEFF').TrimStart();
            if (!text.StartsWith('{'))
            {
                return false;
            }

            var rest = text[1..].TrimStart();
            if (rest.Length == 0 || rest[0] != '"')
            {
                return false; // nothing yet, or another program's (a comment, an array)
            }

            var end = rest.IndexOf('"', 1);
            return end < 0
                ? CopyProperties.Any(property => property.StartsWith(rest[1..], StringComparison.OrdinalIgnoreCase)) // cut short in the name
                : CopyProperties.Contains(rest[1..end], StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    private DriveFile Write(string fileName, string json, IReadOnlyDictionary<string, string> appProperties, string? existingFileId)
    {
        Directory.CreateDirectory(Folder);
        var name = existingFileId ?? FreeFileName(fileName);
        var path = PathOf(name);
        json = WithFileName(json, name);
        var tempPath = Path.Combine(Folder, "~" + Path.GetRandomFileName() + ".tmp");
        try
        {
            using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                stream.Write(Utf8.GetBytes(json));
                stream.Flush(flushToDisk: true);
            }

            try
            {
                File.Move(tempPath, path, overwrite: true);
            }
            catch (Exception ex) when (IsHeldByAnotherProgram(ex, path))
            {
                // A sync program holds the copy just now: the same as a copy that cannot be read, try again later.
                var copyName = appProperties.TryGetValue("profile", out var listed) ? listed : DisplayName(name, null);
                throw new CopyBusyException(string.Format(CultureInfo.CurrentCulture, CoreStrings.Sync_CopyBusy, copyName));
            }
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

        // Listed under the copy's own name: this is the file the copy was written to, numbered or not ("config", not
        // "config (2)", in the messages that name it).
        var info = new FileInfo(path);
        var properties = new Dictionary<string, string>(appProperties);
        properties["profile"] = appProperties.TryGetValue("profile", out var inside) && !string.IsNullOrWhiteSpace(inside) ? inside.Trim() : DisplayName(name, null);
        Add(properties, NameInsideProperty, inside);
        return new DriveFile(name, name, new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero), info.Length, properties);
    }

    /// <summary>
    /// The copy with the name of the file it is written to in it (<see cref="SyncProfile.File"/>), after
    /// <c>"format"</c>, which stays first. Text that is no JSON object is written as it is.
    /// </summary>
    internal static string WithFileName(string json, string fileName)
    {
        try
        {
            if (JsonNode.Parse(json) is not JsonObject copy)
            {
                return json;
            }

            var result = new JsonObject();
            foreach (var (key, value) in copy.ToList())
            {
                copy.Remove(key);
                if (!string.Equals(key, "file", StringComparison.Ordinal))
                {
                    result[key] = value;
                }

                if (result.Count == 1)
                {
                    result["file"] = fileName;
                }
            }

            return result.ToJsonString(LayoutStorage.JsonOptions);
        }
        catch (JsonException)
        {
            return json;
        }
    }

    /// <summary>
    /// Whether replacing the copy at <paramref name="path"/> failed because another program has it open: a sharing or lock
    /// violation, or access denied to a copy that is there and not read-only (Windows refuses to replace a file that is
    /// open; the folder took the new file a moment before, so it is not the folder's permissions).
    /// </summary>
    private static bool IsHeldByAnotherProgram(Exception ex, string path)
    {
        if (ex is IOException io)
        {
            return (io.HResult & 0xFFFF) is 32 or 33; // ERROR_SHARING_VIOLATION, ERROR_LOCK_VIOLATION
        }

        try
        {
            return ex is UnauthorizedAccessException && File.Exists(path) && !File.GetAttributes(path).HasFlag(FileAttributes.ReadOnly);
        }
        catch (Exception probe) when (probe is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Whether the file a copy called <paramref name="name"/> would be saved as is taken by a file that is not listed as
    /// that copy: another program's <c>config.json</c>, a copy too large to read. A new copy of that name then gets a
    /// number (<see cref="FreeFileName"/>) and is listed as a copy of a missing original.
    /// </summary>
    public bool FileNameIsTaken(string name) =>
        File.Exists(Path.Combine(Folder, FileNames.Safe(name.Trim()) + RemoteProfile.FileExtension));

    /// <summary>
    /// A file name for a new copy: the copy's name made safe for Windows, numbered when another copy's name became the
    /// same file name (<c>a:b</c> and <c>a?b</c> are both <c>a_b.json</c>), or another file holds it.
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
