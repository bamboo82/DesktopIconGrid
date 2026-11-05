using System.IO;
using DesktopIconGrid.Core.Diagnostics;
using DesktopIconGrid.Core.Storage;
using DesktopIconGrid.Core.Sync;

namespace DesktopIconGrid.SpecialIcons;

public sealed record DriveTransferProgress(string FileName, long Bytes, long? Total);

/// <summary>Downloads Drive entries (files or whole folders) to disk or to the local cache.</summary>
public sealed class DriveDownloader(IDriveFiles files)
{
    /// <summary>Downloads an entry into <paramref name="destinationDirectory"/>; folders are recreated recursively. Returns the local path.</summary>
    public async Task<string?> DownloadEntryAsync(DriveEntry entry, string destinationDirectory, bool overwrite, IProgress<DriveTransferProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        Directory.CreateDirectory(destinationDirectory);

        if (entry.IsFolder)
        {
            var folder = overwrite
                ? Path.Combine(destinationDirectory, SafeName(entry.Name))
                : UniquePath(destinationDirectory, SafeName(entry.Name));
            Directory.CreateDirectory(folder);
            foreach (var child in await files.ListChildrenAsync(entry.Id, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await DownloadEntryAsync(child, folder, overwrite: true, progress, cancellationToken);
            }

            return folder;
        }

        if (!entry.CanDownload)
        {
            return null;
        }

        var fileName = SafeName(entry.DownloadFileName);
        var target = overwrite ? Path.Combine(destinationDirectory, fileName) : UniquePath(destinationDirectory, fileName);
        var temporary = target + ".part";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                if (entry.IsGoogleFormat)
                {
                    progress?.Report(new DriveTransferProgress(entry.Name, 0, null));
                    await files.ExportAsync(entry.Id, entry.Export!.Value.MimeType, stream, cancellationToken);
                }
                else
                {
                    await files.DownloadAsync(entry.Id, stream, new Progress<long>(b => progress?.Report(new DriveTransferProgress(entry.Name, b, entry.Size))), cancellationToken);
                }
            }

            File.Move(temporary, target, overwrite: true);
            MarkAsFromInternet(target, entry.Id);
            return target;
        }
        catch
        {
            TryDelete(temporary);
            throw;
        }
    }

    /// <summary>Extensions Windows treats as executable; the viewer reveals these instead of running them.</summary>
    public static readonly HashSet<string> ExecutableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".com", ".bat", ".cmd", ".scr", ".pif", ".msi", ".msp", ".lnk", ".url", ".hta", ".js", ".jse",
        ".vbs", ".vbe", ".wsf", ".wsh", ".ps1", ".psm1", ".reg", ".cpl", ".jar", ".appref-ms", ".application",
    };

    public static bool IsExecutable(string path) => ExecutableExtensions.Contains(Path.GetExtension(path));

    /// <summary>
    /// Writes the Zone.Identifier stream so SmartScreen and Office's Protected View treat the file as downloaded
    /// and can name Google Drive (and the file itself, when <paramref name="fileId"/> is known) as its source.
    /// </summary>
    public static void MarkAsFromInternet(string path, string? fileId = null)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var host = string.IsNullOrEmpty(fileId)
            ? "https://drive.google.com/"
            : $"https://drive.google.com/file/d/{Uri.EscapeDataString(fileId)}/view";
        try
        {
            File.WriteAllText(path + ":Zone.Identifier", $"[ZoneTransfer]\r\nZoneId=3\r\nReferrerUrl=https://drive.google.com/\r\nHostUrl={host}\r\n");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // Not NTFS (or the stream is refused); the file is still usable.
        }
    }

    /// <summary>
    /// Downloads into the cache and returns the local path. A previous copy is reused while it is still the file on
    /// Drive (same checksum, or same size and modification time; see <see cref="DriveCacheStamp"/>); a copy without a
    /// stamp, from before stamps existed, is downloaded again once.
    /// </summary>
    public async Task<string?> DownloadToCacheAsync(DriveEntry entry, string cacheDirectory, IProgress<DriveTransferProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var directory = Path.Combine(cacheDirectory, SafeName(entry.Id));
        var stampable = !entry.IsFolder && !entry.IsGoogleFormat;
        if (stampable)
        {
            var cached = Path.Combine(directory, SafeName(entry.DownloadFileName));
            if (File.Exists(cached)
                && ReadStamp(StampPath(cached)) is { } stamp
                && stamp.IsFreshFor(entry)
                && (entry.Size is not { } size || new FileInfo(cached).Length == size))
            {
                Touch(StampPath(cached)); // recency for DriveCache.Purge; the copy keeps its download time
                return cached;
            }
        }

        var path = await DownloadEntryAsync(entry, directory, overwrite: true, progress, cancellationToken);
        if (path is not null && stampable)
        {
            WriteStamp(StampPath(path), DriveCacheStamp.Of(entry));
        }

        return path;
    }

    /// <summary>The stamp kept next to a cached copy.</summary>
    internal static string StampPath(string cachedFile) => cachedFile + ".stamp";

    private static DriveCacheStamp? ReadStamp(string path)
    {
        try
        {
            return File.Exists(path) ? DriveCacheStamp.TryParse(File.ReadAllText(path)) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void WriteStamp(string path, DriveCacheStamp stamp)
    {
        try
        {
            File.WriteAllText(path, stamp.Serialize());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Default.Warning("Bucket", $"Cache stamp not written for {Path.GetFileName(path)}: {ex.Message}");
        }
    }

    private static void Touch(string path)
    {
        try
        {
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Recency is best effort; the copy is still reused.
        }
    }

    public static string UniquePath(string directory, string fileName)
    {
        var candidate = Path.Combine(directory, fileName);
        if (!File.Exists(candidate) && !Directory.Exists(candidate))
        {
            return candidate;
        }

        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        for (var i = 2; ; i++)
        {
            candidate = Path.Combine(directory, $"{stem} ({i}){extension}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
            {
                return candidate;
            }
        }
    }

    /// <summary>A name Windows accepts for the file or folder (see <see cref="FileNames.Safe"/>).</summary>
    public static string SafeName(string name) => FileNames.Safe(name);

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>The local cache of downloaded Drive files (used for drag-out and opening).</summary>
public static class DriveCache
{
    /// <summary>Removes cached entries not touched for longer than <paramref name="maxAge"/>.</summary>
    public static void Purge(string cacheDirectory, TimeSpan maxAge)
    {
        if (!Directory.Exists(cacheDirectory))
        {
            return;
        }

        var cutoff = DateTime.UtcNow - maxAge;
        foreach (var directory in Directory.EnumerateDirectories(cacheDirectory))
        {
            try
            {
                var newest = Directory.EnumerateFileSystemEntries(directory, "*", SearchOption.AllDirectories)
                    .Select(File.GetLastWriteTimeUtc)
                    .DefaultIfEmpty(Directory.GetLastWriteTimeUtc(directory))
                    .Max();
                if (newest < cutoff)
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Still in use or protected; try again next start.
            }
        }
    }
}
