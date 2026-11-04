using System.IO;
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

    /// <summary>Downloads into the cache (reusing a previous copy of the same size) and returns the local path.</summary>
    public async Task<string?> DownloadToCacheAsync(DriveEntry entry, string cacheDirectory, IProgress<DriveTransferProgress>? progress, CancellationToken cancellationToken)
    {
        var directory = Path.Combine(cacheDirectory, SafeName(entry.Id));
        if (!entry.IsFolder && !entry.IsGoogleFormat)
        {
            var cached = Path.Combine(directory, SafeName(entry.DownloadFileName));
            if (File.Exists(cached) && entry.Size is { } size && new FileInfo(cached).Length == size)
            {
                File.SetLastWriteTimeUtc(cached, DateTime.UtcNow);
                return cached;
            }
        }

        return await DownloadEntryAsync(entry, directory, overwrite: true, progress, cancellationToken);
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

    public static string SafeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        var result = new string(chars).TrimEnd('.', ' ');
        return result.Length == 0 ? "file" : result;
    }

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
