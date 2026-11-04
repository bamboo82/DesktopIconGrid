using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

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
public sealed class GoogleDriveClient : IDriveClient
{
    public const string FolderName = "DesktopIconGrid";
    public const string ApiBase = "https://www.googleapis.com/drive/v3/";
    public const string UploadBase = "https://www.googleapis.com/upload/drive/v3/";

    private const string FolderMimeType = "application/vnd.google-apps.folder";
    private const string JsonMimeType = "application/json";
    private const string FileFields = "id,name,modifiedTime,size,appProperties";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly IAccessTokenProvider _tokens;
    private string? _folderId;

    public GoogleDriveClient(HttpClient http, IAccessTokenProvider tokens)
    {
        _http = http;
        _tokens = tokens;
    }

    public async Task<DriveUser> GetUserAsync(CancellationToken cancellationToken)
    {
        var about = await GetAsync<AboutDto>("about?fields=user(emailAddress,displayName)", cancellationToken).ConfigureAwait(false);
        return new DriveUser(about?.User?.EmailAddress, about?.User?.DisplayName);
    }

    public async Task<IReadOnlyList<DriveFile>> ListFilesAsync(CancellationToken cancellationToken)
    {
        var folderId = await EnsureFolderAsync(cancellationToken).ConfigureAwait(false);
        var query = $"'{folderId}' in parents and trashed = false and mimeType = '{JsonMimeType}'";
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

    private async Task<string> EnsureFolderAsync(CancellationToken cancellationToken)
    {
        if (_folderId is not null)
        {
            return _folderId;
        }

        var query = $"name = '{FolderName}' and mimeType = '{FolderMimeType}' and trashed = false and 'root' in parents";
        var found = await GetAsync<FileListDto>($"files?q={Uri.EscapeDataString(query)}&fields=files(id,name)&pageSize=10", cancellationToken).ConfigureAwait(false);
        var existing = found?.Files?.FirstOrDefault();
        if (existing?.Id is not null)
        {
            return _folderId = existing.Id;
        }

        var metadata = new { name = FolderName, mimeType = FolderMimeType };
        using var response = await SendAsync(
            () => new HttpRequestMessage(HttpMethod.Post, $"{ApiBase}files?fields=id,name")
            {
                Content = new StringContent(JsonSerializer.Serialize(metadata, JsonOptions), Encoding.UTF8, JsonMimeType),
            },
            cancellationToken).ConfigureAwait(false);
        var created = await ReadJsonAsync<FileDto>(response, cancellationToken).ConfigureAwait(false);
        if (created?.Id is null)
        {
            throw new DriveException("Google Drive did not return the id of the new folder.");
        }

        return _folderId = created.Id;
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
    private async Task<HttpResponseMessage> SendAsync(Func<HttpRequestMessage> createRequest, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            var request = createRequest();
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await _tokens.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false));

            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                throw new DriveException($"Google Drive could not be reached: {ex.Message}");
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
            throw new DriveException(DescribeError(body, status), status);
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
            HttpStatusCode.Forbidden => "Google Drive refused the request (HTTP 403). Check that the Drive API is enabled for the project.",
            HttpStatusCode.NotFound => "The file no longer exists on Google Drive.",
            _ => $"Google Drive returned HTTP {(int)status}.",
        };
    }

    private static DriveFile ToDriveFile(FileDto dto) => new(
        dto.Id ?? string.Empty,
        dto.Name ?? string.Empty,
        dto.ModifiedTime,
        long.TryParse(dto.Size, out var size) ? size : null,
        dto.AppProperties ?? new Dictionary<string, string>());

    private sealed class FileListDto
    {
        public List<FileDto>? Files { get; set; }
    }

    private sealed class FileDto
    {
        public string? Id { get; set; }

        public string? Name { get; set; }

        public DateTimeOffset? ModifiedTime { get; set; }

        /// <summary>Drive reports sizes as strings.</summary>
        public string? Size { get; set; }

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
