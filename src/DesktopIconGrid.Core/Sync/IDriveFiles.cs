namespace DesktopIconGrid.Core.Sync;

/// <summary>Browsing, uploading and downloading arbitrary Drive files (needs the full Drive scope).</summary>
public interface IDriveFiles
{
    /// <summary>The entry with that id, or <see langword="null"/> when it does not exist or is not accessible.</summary>
    Task<DriveEntry?> GetEntryAsync(string id, CancellationToken cancellationToken);

    /// <summary>Direct children of a folder (not trashed), folders first, in natural name order.</summary>
    Task<IReadOnlyList<DriveEntry>> ListChildrenAsync(string folderId, CancellationToken cancellationToken);

    Task<DriveEntry> CreateFolderAsync(string parentId, string name, CancellationToken cancellationToken);

    /// <summary>
    /// Uploads a file into <paramref name="parentId"/>, or replaces the content of <paramref name="existingFileId"/>.
    /// Large files use resumable uploads; <paramref name="progress"/> receives bytes sent so far.
    /// </summary>
    Task<DriveEntry> UploadAsync(
        string parentId,
        string name,
        string mimeType,
        Stream content,
        long length,
        string? existingFileId,
        IProgress<long>? progress,
        CancellationToken cancellationToken);

    /// <summary>Streams a file's bytes into <paramref name="destination"/>; <paramref name="progress"/> receives bytes received so far.</summary>
    Task DownloadAsync(string fileId, Stream destination, IProgress<long>? progress, CancellationToken cancellationToken);

    /// <summary>
    /// The id of the app's own folder on Drive (created when missing), under which the layout copies and the inbox
    /// live. Only the real client has one.
    /// </summary>
    Task<string> AppFolderIdAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

    /// <summary>Deletes a file or folder the app created.</summary>
    Task DeleteEntryAsync(string id, CancellationToken cancellationToken) => throw new NotSupportedException();

    /// <summary>Exports a Google document in the given format.</summary>
    Task ExportAsync(string fileId, string exportMimeType, Stream destination, CancellationToken cancellationToken);
}
