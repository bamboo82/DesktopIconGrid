using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DesktopIconGrid.Core.Diagnostics;

namespace DesktopIconGrid.Core.Sync;

public sealed record DriveUser(string? EmailAddress, string? DisplayName);

public sealed record DriveFile(
    string Id,
    string Name,
    DateTimeOffset? ModifiedTime,
    long? Size,
    IReadOnlyDictionary<string, string> AppProperties);

public sealed class DriveException(string message, HttpStatusCode? statusCode = null) : Exception(message)
{
    public HttpStatusCode? StatusCode { get; } = statusCode;
}

/// <summary>The subset of Google Drive the sync feature needs, scoped to the app's own folder.</summary>
public interface IDriveClient
{
    Task<DriveUser> GetUserAsync(CancellationToken cancellationToken);

    /// <summary>JSON files in the app folder.</summary>
    Task<IReadOnlyList<DriveFile>> ListFilesAsync(CancellationToken cancellationToken);

    /// <summary>Creates a file in the app folder, or replaces the content and properties of <paramref name="existingFileId"/>.</summary>
    Task<DriveFile> UploadJsonAsync(string fileName, string json, IReadOnlyDictionary<string, string> appProperties, string? existingFileId, CancellationToken cancellationToken);

    Task<string> DownloadTextAsync(string fileId, CancellationToken cancellationToken);

    Task DeleteAsync(string fileId, CancellationToken cancellationToken);
}

/// <summary>Google Drive v3 REST calls over <see cref="HttpClient"/>, with one automatic token refresh on 401.</summary>
public sealed class GoogleDriveClient : IDriveClient, IDriveFiles
{
    public const string FolderName = "DesktopIconGrid";
    public const string ApiBase = "https://www.googleapis.com/drive/v3/";
    public const string UploadBase = "https://www.googleapis.com/upload/drive/v3/";

    /// <summary>Files above this size use resumable uploads.</summary>
    public const long MultipartLimit = 5 * 1024 * 1024;

    /// <summary>Resumable upload chunk size; Google requires multiples of 256 KiB.</summary>
    public const int ChunkSize = 8 * 1024 * 1024;

    private const string FolderMimeType = DriveEntry.FolderMimeType;
    private const string JsonMimeType = "application/json";
    private const string FileFields = "id,name,modifiedTime,size,appProperties";
    private const string EntryFields = "id,name,mimeType,size,modifiedTime,md5Checksum,createdTime";
    private const int MaxChunkAttempts = 4;

    /// <summary>
    /// Limit for metadata calls and for the header phase of streamed downloads; also how long a download
    /// may go without delivering any bytes.
    /// </summary>
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(100);

    /// <summary>Limit for one upload request: a multipart upload (up to <see cref="MultipartLimit"/>) or one resumable chunk.</summary>
    public static readonly TimeSpan ChunkTimeout = TimeSpan.FromMinutes(10);

    /// <summary>Waits before a retry (the delay comes from <see cref="BackoffDelay"/>); replaced in tests.</summary>
    internal Func<TimeSpan, CancellationToken, Task> RetryDelay { get; set; } = Task.Delay;

    /// <summary>Longest wait a Retry-After header may impose.</summary>
    internal static readonly TimeSpan MaxRetryAfter = TimeSpan.FromMinutes(5);

    /// <summary>How long a download may stall before it is abandoned; replaced in tests.</summary>
    internal TimeSpan StallTimeout { get; set; } = RequestTimeout;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly IAccessTokenProvider _tokens;
    private string? _folderId;
    private IReadOnlyList<string> _folderIds = [];

    public GoogleDriveClient(HttpClient http, IAccessTokenProvider tokens)
    {
        _http = http;
        _tokens = tokens;
    }

    // ----- sync (app folder) ---------------------------------------------------------------------

    public async Task<DriveUser> GetUserAsync(CancellationToken cancellationToken)
    {
        var about = await GetAsync<AboutDto>("about?fields=user(emailAddress,displayName)", cancellationToken).ConfigureAwait(false);
        return new DriveUser(about?.User?.EmailAddress, about?.User?.DisplayName);
    }

    public async Task<IReadOnlyList<DriveFile>> ListFilesAsync(CancellationToken cancellationToken)
    {
        var folderId = await EnsureFolderAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<string> folders = _folderIds.Count > 0 ? _folderIds : [folderId];
        var parents = folders.Count == 1
            ? $"'{folders[0]}' in parents"
            : "(" + string.Join(" or ", folders.Select(id => $"'{id}' in parents")) + ")";
        var query = $"{parents} and trashed = false and mimeType = '{JsonMimeType}'";
        var url = $"files?q={Uri.EscapeDataString(query)}&fields={Uri.EscapeDataString($"files({FileFields})")}&pageSize=200&orderBy=name_natural";
        var response = await GetAsync<FileListDto>(url, cancellationToken).ConfigureAwait(false);
        return (response?.Files ?? []).Select(ToDriveFile).ToList();
    }

    public async Task<DriveFile> UploadJsonAsync(string fileName, string json, IReadOnlyDictionary<string, string> appProperties, string? existingFileId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        var folderId = await EnsureFolderAsync(cancellationToken).ConfigureAwait(false);

        var metadata = new Dictionary<string, object>
        {
            ["name"] = fileName,
            ["mimeType"] = JsonMimeType,
            ["appProperties"] = appProperties,
        };
        if (existingFileId is null)
        {
            metadata["parents"] = new[] { folderId };
        }

        var url = existingFileId is null
            ? $"{UploadBase}files?uploadType=multipart&fields={FileFields}"
            : $"{UploadBase}files/{Uri.EscapeDataString(existingFileId)}?uploadType=multipart&fields={FileFields}";
        var method = existingFileId is null ? HttpMethod.Post : HttpMethod.Patch;

        using var response = await SendAsync(() =>
        {
            var body = new MultipartContent("related");
            body.Add(new StringContent(JsonSerializer.Serialize(metadata, JsonOptions), Encoding.UTF8, JsonMimeType));
            body.Add(new StringContent(json, Encoding.UTF8, JsonMimeType));
            return new HttpRequestMessage(method, url) { Content = body };
        }, cancellationToken).ConfigureAwait(false);

        var dto = await ReadJsonAsync<FileDto>(response, cancellationToken).ConfigureAwait(false)
                  ?? throw new DriveException("Google Drive did not describe the uploaded file.");
        return ToDriveFile(dto);
    }

    public async Task<string> DownloadTextAsync(string fileId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileId);
        using var response = await SendAsync(
            () => new HttpRequestMessage(HttpMethod.Get, $"{ApiBase}files/{Uri.EscapeDataString(fileId)}?alt=media"),
            cancellationToken).ConfigureAwait(false);
        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string fileId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileId);
        using var response = await SendAsync(
            () => new HttpRequestMessage(HttpMethod.Delete, $"{ApiBase}files/{Uri.EscapeDataString(fileId)}"),
            cancellationToken).ConfigureAwait(false);
    }

    // ----- browsing and transfers (buckets, viewer) ---------------------------------------------------

    public async Task<DriveEntry?> GetEntryAsync(string id, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        try
        {
            var dto = await GetAsync<FileDto>($"files/{Uri.EscapeDataString(id)}?fields={EntryFields}&supportsAllDrives=true", cancellationToken).ConfigureAwait(false);
            return dto is null ? null : ToEntry(dto);
        }
        catch (DriveException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<DriveEntry>> ListChildrenAsync(string folderId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderId);
        var entries = new List<DriveEntry>();
        string? pageToken = null;
        var query = Uri.EscapeDataString($"'{folderId}' in parents and trashed = false");
        var fields = Uri.EscapeDataString($"nextPageToken,files({EntryFields})");
        do
        {
            var url = $"files?q={query}&fields={fields}&orderBy=folder,name_natural&pageSize=200&supportsAllDrives=true&includeItemsFromAllDrives=true";
            if (pageToken is not null)
            {
                url += "&pageToken=" + Uri.EscapeDataString(pageToken);
            }

            var page = await GetAsync<FileListDto>(url, cancellationToken).ConfigureAwait(false);
            entries.AddRange((page?.Files ?? []).Select(ToEntry));
            pageToken = page?.NextPageToken;
        }
        while (!string.IsNullOrEmpty(pageToken));

        return entries;
    }

    public async Task<DriveEntry> CreateFolderAsync(string parentId, string name, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var metadata = new { name, mimeType = FolderMimeType, parents = new[] { parentId } };
        using var response = await SendAsync(
            () => new HttpRequestMessage(HttpMethod.Post, $"{ApiBase}files?fields={EntryFields}&supportsAllDrives=true")
            {
                Content = new StringContent(JsonSerializer.Serialize(metadata, JsonOptions), Encoding.UTF8, JsonMimeType),
            },
            cancellationToken).ConfigureAwait(false);
        var dto = await ReadJsonAsync<FileDto>(response, cancellationToken).ConfigureAwait(false)
                  ?? throw new DriveException("Google Drive did not describe the new folder.");
        return ToEntry(dto);
    }

    public async Task<DriveEntry> UploadAsync(string parentId, string name, string mimeType, Stream content, long length, string? existingFileId, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(content);
        mimeType = string.IsNullOrWhiteSpace(mimeType) ? MimeTypes.Binary : mimeType;

        return length <= MultipartLimit
            ? await UploadMultipartAsync(parentId, name, mimeType, content, length, existingFileId, progress, cancellationToken).ConfigureAwait(false)
            : await UploadResumableAsync(parentId, name, mimeType, content, length, existingFileId, progress, cancellationToken).ConfigureAwait(false);
    }

    public async Task DownloadAsync(string fileId, Stream destination, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileId);
        using var response = await SendAsync(
            () => new HttpRequestMessage(HttpMethod.Get, $"{ApiBase}files/{Uri.EscapeDataString(fileId)}?alt=media&supportsAllDrives=true"),
            cancellationToken,
            HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
        await CopyWithProgressAsync(response, destination, progress, cancellationToken).ConfigureAwait(false);
    }

    public async Task ExportAsync(string fileId, string exportMimeType, Stream destination, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileId);
        ArgumentException.ThrowIfNullOrWhiteSpace(exportMimeType);
        using var response = await SendAsync(
            () => new HttpRequestMessage(HttpMethod.Get, $"{ApiBase}files/{Uri.EscapeDataString(fileId)}/export?mimeType={Uri.EscapeDataString(exportMimeType)}"),
            cancellationToken,
            HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
        await CopyWithProgressAsync(response, destination, null, cancellationToken).ConfigureAwait(false);
    }

    private async Task<DriveEntry> UploadMultipartAsync(string parentId, string name, string mimeType, Stream content, long length, string? existingFileId, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        var metadata = new Dictionary<string, object> { ["name"] = name };
        if (existingFileId is null)
        {
            metadata["parents"] = new[] { parentId };
        }

        var url = existingFileId is null
            ? $"{UploadBase}files?uploadType=multipart&fields={EntryFields}&supportsAllDrives=true"
            : $"{UploadBase}files/{Uri.EscapeDataString(existingFileId)}?uploadType=multipart&fields={EntryFields}&supportsAllDrives=true";
        var method = existingFileId is null ? HttpMethod.Post : HttpMethod.Patch;

        var bytes = new byte[length];
        var read = 0;
        while (read < length)
        {
            var count = await content.ReadAsync(bytes.AsMemory(read, (int)(length - read)), cancellationToken).ConfigureAwait(false);
            if (count == 0)
            {
                break;
            }

            read += count;
        }

        using var response = await SendAsync(() =>
        {
            var body = new MultipartContent("related");
            body.Add(new StringContent(JsonSerializer.Serialize(metadata, JsonOptions), Encoding.UTF8, JsonMimeType));
            var media = new ByteArrayContent(bytes, 0, read);
            media.Headers.ContentType = new MediaTypeHeaderValue(mimeType);
            body.Add(media);
            return new HttpRequestMessage(method, url) { Content = body };
        }, cancellationToken, timeout: ChunkTimeout).ConfigureAwait(false);

        progress?.Report(read);
        var dto = await ReadJsonAsync<FileDto>(response, cancellationToken).ConfigureAwait(false)
                  ?? throw new DriveException("Google Drive did not describe the uploaded file.");
        return ToEntry(dto);
    }

    private async Task<DriveEntry> UploadResumableAsync(string parentId, string name, string mimeType, Stream content, long length, string? existingFileId, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        // 1. Open the session.
        var metadata = new Dictionary<string, object> { ["name"] = name };
        if (existingFileId is null)
        {
            metadata["parents"] = new[] { parentId };
        }

        var sessionUrl = existingFileId is null
            ? $"{UploadBase}files?uploadType=resumable&fields={EntryFields}&supportsAllDrives=true"
            : $"{UploadBase}files/{Uri.EscapeDataString(existingFileId)}?uploadType=resumable&fields={EntryFields}&supportsAllDrives=true";
        var method = existingFileId is null ? HttpMethod.Post : HttpMethod.Patch;

        Uri uploadUri;
        using (var open = await SendAsync(() =>
               {
                   var request = new HttpRequestMessage(method, sessionUrl)
                   {
                       Content = new StringContent(JsonSerializer.Serialize(metadata, JsonOptions), Encoding.UTF8, JsonMimeType),
                   };
                   request.Headers.Add("X-Upload-Content-Type", mimeType);
                   request.Headers.Add("X-Upload-Content-Length", length.ToString(System.Globalization.CultureInfo.InvariantCulture));
                   return request;
               }, cancellationToken).ConfigureAwait(false))
        {
            uploadUri = open.Headers.Location ?? throw new DriveException("Google Drive did not open an upload session.");
        }

        AppLog.Default.Debug("Drive", $"Resumable upload of \"{name}\" ({length:N0} bytes) started");

        // 2. Send the chunks. `offset` is the first byte Drive has not confirmed yet; the buffer holds
        // [bufferStart, bufferStart + bufferLength) of the file so a partly accepted chunk can be re-sent.
        var origin = content.CanSeek ? content.Position : 0;
        var buffer = new byte[ChunkSize];
        long offset = 0;
        long bufferStart = 0;
        var bufferLength = 0;
        var attempt = 0;
        while (true)
        {
            if (offset >= length)
            {
                // Everything was confirmed by 308 responses without a final answer: ask once more.
                var finished = await ResumeAsync(uploadUri, length, attempt: 0, retryAfter: null, cancellationToken).ConfigureAwait(false);
                if (finished.Completed is not null)
                {
                    progress?.Report(length);
                    return finished.Completed;
                }

                throw new DriveException("The upload finished without a confirmation from Google Drive.");
            }

            if (offset < bufferStart || offset >= bufferStart + bufferLength)
            {
                if (offset != bufferStart + bufferLength)
                {
                    if (!content.CanSeek)
                    {
                        throw new DriveException("Google Drive lost part of the upload and the file cannot be re-read.");
                    }

                    content.Position = origin + offset;
                }

                bufferStart = offset;
                bufferLength = await FillAsync(content, buffer, (int)Math.Min(buffer.Length, length - offset), cancellationToken).ConfigureAwait(false);
                if (bufferLength == 0)
                {
                    throw new DriveException("The file ended before the expected length was read.");
                }
            }

            var sliceStart = (int)(offset - bufferStart);
            var sliceLength = bufferLength - sliceStart;
            var end = offset + sliceLength - 1;

            HttpResponseMessage response;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Put, uploadUri)
                {
                    Content = new ByteArrayContent(buffer, sliceStart, sliceLength),
                };
                request.Content.Headers.ContentType = new MediaTypeHeaderValue(mimeType);
                request.Content.Headers.ContentRange = new ContentRangeHeaderValue(offset, end, length);
                using var chunkTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                chunkTimeout.CancelAfter(ChunkTimeout);
                response = await _http.SendAsync(request, chunkTimeout.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpRequestException || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
            {
                var reason = ex is OperationCanceledException ? "timed out" : ex.Message;
                if (++attempt >= MaxChunkAttempts)
                {
                    throw new DriveException(ex is OperationCanceledException ? "The upload timed out." : $"The upload was interrupted: {ex.Message}");
                }

                AppLog.Default.Warning("Drive", $"Chunk at {offset:N0} of \"{name}\" {reason}; retry {attempt} of {MaxChunkAttempts - 1}");
                var resumed = await ResumeAsync(uploadUri, length, attempt, retryAfter: null, cancellationToken).ConfigureAwait(false);
                if (resumed.Completed is not null)
                {
                    progress?.Report(length);
                    return resumed.Completed;
                }

                offset = resumed.NextByte ?? offset;
                continue;
            }

            using (response)
            {
                var status = response.StatusCode;
                if ((int)status == 308)
                {
                    var confirmed = Math.Min(ConfirmedBytes(response), length);
                    if (confirmed > offset)
                    {
                        attempt = 0;
                    }
                    else if (++attempt >= MaxChunkAttempts)
                    {
                        throw new DriveException("Google Drive is not accepting the uploaded data.");
                    }

                    offset = confirmed;
                    progress?.Report(offset);
                    continue;
                }

                if (response.IsSuccessStatusCode)
                {
                    progress?.Report(length);
                    var dto = await ReadJsonAsync<FileDto>(response, cancellationToken).ConfigureAwait(false)
                              ?? throw new DriveException("Google Drive did not describe the uploaded file.");
                    return ToEntry(dto);
                }

                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var description = DescribeError(body, status);
                if (!IsRetryable(status, body) || ++attempt >= MaxChunkAttempts)
                {
                    AppLog.Default.Warning("Drive", $"Upload of \"{name}\" failed: {description}");
                    throw new DriveException(description, status);
                }

                AppLog.Default.Warning("Drive", $"Chunk at {offset:N0} of \"{name}\" rejected ({description}); retry {attempt} of {MaxChunkAttempts - 1}");
                var recovered = await ResumeAsync(uploadUri, length, attempt, RetryAfterOf(response), cancellationToken).ConfigureAwait(false);
                if (recovered.Completed is not null)
                {
                    progress?.Report(length);
                    return recovered.Completed;
                }

                offset = recovered.NextByte ?? offset;
            }
        }
    }

    /// <summary>After a failed chunk: backs off, then asks Drive how much it holds (<c>Content-Range: bytes */length</c>).</summary>
    private async Task<(long? NextByte, DriveEntry? Completed)> ResumeAsync(Uri uploadUri, long length, int attempt, TimeSpan? retryAfter, CancellationToken cancellationToken)
    {
        if (attempt > 0)
        {
            await RetryDelay(BackoffDelay(attempt, retryAfter), cancellationToken).ConfigureAwait(false);
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Put, uploadUri) { Content = new ByteArrayContent([]) };
            request.Content.Headers.ContentRange = new ContentRangeHeaderValue(length);
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            limit.CancelAfter(RequestTimeout);
            using var response = await _http.SendAsync(request, limit.Token).ConfigureAwait(false);
            if ((int)response.StatusCode == 308)
            {
                return (Math.Min(ConfirmedBytes(response), length), null);
            }

            if (response.IsSuccessStatusCode)
            {
                var dto = await ReadJsonAsync<FileDto>(response, cancellationToken).ConfigureAwait(false);
                return dto is null ? (null, null) : (length, ToEntry(dto));
            }

            return (null, null); // e.g. 404: the session expired; the next chunk attempt reports it
        }
        catch (HttpRequestException)
        {
            return (null, null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (null, null);
        }
    }

    /// <summary>The server's Retry-After when it sent one, otherwise 2^attempt seconds with ±25 % jitter so retrying clients spread out.</summary>
    internal static TimeSpan BackoffDelay(int attempt, TimeSpan? retryAfter)
    {
        if (retryAfter is { } wait && wait > TimeSpan.Zero)
        {
            return wait > MaxRetryAfter ? MaxRetryAfter : wait;
        }

        var seconds = Math.Pow(2, attempt) * (0.75 + Random.Shared.NextDouble() * 0.5);
        return TimeSpan.FromSeconds(seconds);
    }

    private static TimeSpan? RetryAfterOf(HttpResponseMessage response)
    {
        var header = response.Headers.RetryAfter;
        if (header is null)
        {
            return null;
        }

        if (header.Delta is { } delta)
        {
            return delta;
        }

        return header.Date is { } date ? date - DateTimeOffset.UtcNow : null;
    }

    /// <summary>Bytes Drive holds according to a 308 response's <c>Range: bytes=0-N</c> header; 0 when the header is absent.</summary>
    internal static long ConfirmedBytes(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Range", out var values))
        {
            return 0;
        }

        foreach (var value in values)
        {
            var dash = value.LastIndexOf('-');
            if (dash > 0 && long.TryParse(value.AsSpan(dash + 1).Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var last))
            {
                return last + 1;
            }
        }

        return 0;
    }

    /// <summary>Rate limits and server errors are worth a retry; everything else is final.</summary>
    private static bool IsRetryable(HttpStatusCode status, string body) =>
        (int)status == 429
        || (int)status >= 500
        || (status == HttpStatusCode.Forbidden && body.Contains("ateLimitExceeded", StringComparison.Ordinal));

    private static async Task<int> FillAsync(Stream source, byte[] buffer, int count, CancellationToken cancellationToken)
    {
        var filled = 0;
        while (filled < count)
        {
            var read = await source.ReadAsync(buffer.AsMemory(filled, count - filled), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            filled += read;
        }

        return filled;
    }

    /// <summary>Copies the body to <paramref name="destination"/>; a read that delivers nothing for <see cref="StallTimeout"/> abandons the transfer.</summary>
    private async Task CopyWithProgressAsync(HttpResponseMessage response, Stream destination, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        using var idle = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try
        {
            idle.CancelAfter(StallTimeout);
            await using var source = await response.Content.ReadAsStreamAsync(idle.Token).ConfigureAwait(false);
            var buffer = new byte[81920];
            long total = 0;
            while (true)
            {
                idle.CancelAfter(StallTimeout);
                var read = await source.ReadAsync(buffer, idle.Token).ConfigureAwait(false);
                if (read <= 0)
                {
                    break;
                }

                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                total += read;
                progress?.Report(total);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            AppLog.Default.Warning("Drive", "Download stalled: no data for " + StallTimeout.TotalSeconds.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " s");
            throw new DriveException("The download stalled: Google Drive stopped sending data.");
        }
        catch (HttpRequestException ex)
        {
            throw new DriveException($"The download was interrupted: {ex.Message}");
        }
    }

    public Task<string> AppFolderIdAsync(CancellationToken cancellationToken) => EnsureFolderAsync(cancellationToken);

    public Task DeleteEntryAsync(string id, CancellationToken cancellationToken) => DeleteAsync(id, cancellationToken);

    // ----- plumbing -------------------------------------------------------------------------------------

    private async Task<string> EnsureFolderAsync(CancellationToken cancellationToken)
    {
        if (_folderId is not null)
        {
            return _folderId;
        }

        var folders = await AppFoldersAsync(cancellationToken).ConfigureAwait(false);
        if (folders.Count > 0)
        {
            RememberFolders(folders);
            return _folderId = folders[0];
        }

        var metadata = new { name = FolderName, mimeType = FolderMimeType };
        FileDto? created;
        using (var response = await SendAsync(
                   () => new HttpRequestMessage(HttpMethod.Post, $"{ApiBase}files?fields=id,name,createdTime")
                   {
                       Content = new StringContent(JsonSerializer.Serialize(metadata, JsonOptions), Encoding.UTF8, JsonMimeType),
                   },
                   cancellationToken).ConfigureAwait(false))
        {
            created = await ReadJsonAsync<FileDto>(response, cancellationToken).ConfigureAwait(false);
        }

        if (created?.Id is null)
        {
            throw new DriveException("Google Drive did not return the id of the new folder.");
        }

        // Two devices connecting for the first time can both have found nothing: look again and keep the
        // folder created first, removing the one just created when another device won the race.
        var again = await AppFoldersAsync(cancellationToken).ConfigureAwait(false);
        if (again.Count > 0 && !string.Equals(again[0], created.Id, StringComparison.Ordinal))
        {
            AppLog.Default.Info("Drive", $"Another device created the {FolderName} folder at the same time; using the older one");
            try
            {
                await DeleteAsync(created.Id, cancellationToken).ConfigureAwait(false);
            }
            catch (DriveException)
            {
                // Best effort; an empty extra folder is harmless.
            }

            RememberFolders(again.Where(id => !string.Equals(id, created.Id, StringComparison.Ordinal)).ToList());
            return _folderId = again[0];
        }

        RememberFolders(again.Count > 0 ? again : new List<string> { created.Id });
        return _folderId = created.Id;
    }

    /// <summary>Ids of every app folder in the Drive root, the one created first at index 0.</summary>
    private async Task<List<string>> AppFoldersAsync(CancellationToken cancellationToken)
    {
        var query = $"name = '{FolderName}' and mimeType = '{FolderMimeType}' and trashed = false and 'root' in parents";
        var found = await GetAsync<FileListDto>($"files?q={Uri.EscapeDataString(query)}&fields=files(id,name,createdTime)&orderBy=createdTime&pageSize=10", cancellationToken).ConfigureAwait(false);
        return (found?.Files ?? [])
            .Where(f => !string.IsNullOrEmpty(f.Id))
            .OrderBy(f => f.CreatedTime ?? DateTimeOffset.MaxValue)
            .ThenBy(f => f.Id, StringComparer.Ordinal)
            .Select(f => f.Id!)
            .ToList();
    }

    /// <summary>
    /// Keeps every app folder for listing: version 0.1.0 could create two when a user's first connections raced,
    /// and the copies saved into the second one must not vanish from the list. New copies go to the folder
    /// created first.
    /// </summary>
    private void RememberFolders(IReadOnlyList<string> folders)
    {
        _folderIds = folders;
        if (folders.Count > 1)
        {
            AppLog.Default.Warning("Drive", $"{folders.Count} \"{FolderName}\" folders exist on this Drive (an earlier version could create two); copies are listed from all of them and new copies go to the one created first");
        }
    }

    private async Task<T?> GetAsync<T>(string relativeUrl, CancellationToken cancellationToken)
        where T : class
    {
        using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, ApiBase + relativeUrl), cancellationToken).ConfigureAwait(false);
        return await ReadJsonAsync<T>(response, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<T?> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
        where T : class
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(body, JsonOptions);
        }
        catch (JsonException)
        {
            throw new DriveException("Google Drive returned a response that could not be read.");
        }
    }

    /// <summary>Sends a request with a bearer token; on 401 refreshes the token once and retries.</summary>
    private async Task<HttpResponseMessage> SendAsync(Func<HttpRequestMessage> createRequest, CancellationToken cancellationToken, HttpCompletionOption completion = HttpCompletionOption.ResponseContentRead, TimeSpan? timeout = null)
    {
        for (var attempt = 0; ; attempt++)
        {
            var request = createRequest();
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await _tokens.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false));

            HttpResponseMessage response;
            try
            {
                // Bounds the whole call for buffered responses and the header phase for streamed ones (with
                // ResponseHeadersRead the token does not govern body reads; CopyWithProgressAsync bounds those).
                using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                limit.CancelAfter(timeout ?? RequestTimeout);
                response = await _http.SendAsync(request, completion, limit.Token).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                AppLog.Default.Error("Drive", $"{request.Method} {request.RequestUri?.AbsolutePath}: could not be reached", ex);
                throw new DriveException($"Google Drive could not be reached: {ex.Message}");
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                AppLog.Default.Warning("Drive", $"{request.Method} {request.RequestUri?.AbsolutePath}: timed out");
                throw new DriveException("Google Drive did not answer in time.");
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
            {
                response.Dispose();
                _tokens.InvalidateAccessToken();
                continue;
            }

            if (response.IsSuccessStatusCode)
            {
                return response;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var status = response.StatusCode;
            response.Dispose();
            var description = DescribeError(body, status);
            AppLog.Default.Warning("Drive", $"{request.Method} {request.RequestUri?.AbsolutePath}: {description}");
            throw new DriveException(description, status);
        }
    }

    private static string DescribeError(string body, HttpStatusCode status)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("error", out var error))
            {
                if (error.ValueKind == JsonValueKind.Object
                    && error.TryGetProperty("message", out var message)
                    && message.ValueKind == JsonValueKind.String)
                {
                    return $"{message.GetString()} (HTTP {(int)status})";
                }

                if (error.ValueKind == JsonValueKind.String)
                {
                    return $"{error.GetString()} (HTTP {(int)status})";
                }
            }
        }
        catch (JsonException)
        {
            // fall through
        }

        return status switch
        {
            HttpStatusCode.Unauthorized => "Google Drive rejected the sign-in. Connect again.",
            HttpStatusCode.Forbidden => "Google Drive refused the request (HTTP 403). Check that the Drive API is enabled for the project and that the app was granted access to your Drive.",
            HttpStatusCode.NotFound => "The file or folder no longer exists on Google Drive, or the app cannot see it.",
            _ => $"Google Drive returned HTTP {(int)status}.",
        };
    }

    private static DriveFile ToDriveFile(FileDto dto) => new(
        dto.Id ?? string.Empty,
        dto.Name ?? string.Empty,
        dto.ModifiedTime,
        long.TryParse(dto.Size, out var size) ? size : null,
        dto.AppProperties ?? new Dictionary<string, string>());

    private static DriveEntry ToEntry(FileDto dto) => new(
        dto.Id ?? string.Empty,
        dto.Name ?? string.Empty,
        dto.MimeType ?? MimeTypes.Binary,
        long.TryParse(dto.Size, out var size) ? size : null,
        dto.ModifiedTime,
        dto.Md5Checksum,
        dto.CreatedTime);

    private sealed class FileListDto
    {
        public List<FileDto>? Files { get; set; }

        public string? NextPageToken { get; set; }
    }

    private sealed class FileDto
    {
        public string? Id { get; set; }

        public string? Name { get; set; }

        public string? MimeType { get; set; }

        public DateTimeOffset? ModifiedTime { get; set; }

        public DateTimeOffset? CreatedTime { get; set; }

        /// <summary>Drive reports sizes as strings.</summary>
        public string? Size { get; set; }

        public string? Md5Checksum { get; set; }

        public Dictionary<string, string>? AppProperties { get; set; }
    }

    private sealed class AboutDto
    {
        public UserDto? User { get; set; }
    }

    private sealed class UserDto
    {
        public string? EmailAddress { get; set; }

        public string? DisplayName { get; set; }
    }
}
