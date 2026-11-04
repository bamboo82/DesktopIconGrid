using System.Net;
using DesktopIconGrid.Core.Sync;

namespace DesktopIconGrid.Core.Tests.Sync;

public class GoogleDriveClientTests
{
    private sealed class StaticTokens(string token) : IAccessTokenProvider
    {
        public int Invalidations { get; private set; }

        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken) => Task.FromResult(token);

        public void InvalidateAccessToken() => Invalidations++;
    }

    private const string EmptyList = """{"files":[]}""";
    private const string FolderList = """{"files":[{"id":"folder1","name":"DesktopIconGrid"}]}""";

    [Fact]
    public async Task Lists_profile_files_creating_the_folder_when_missing()
    {
        var handler = new FakeHttpHandler()
            .Enqueue(HttpStatusCode.OK, EmptyList)
            .Enqueue(HttpStatusCode.OK, """{"id":"newfolder","name":"DesktopIconGrid"}""")
            .Enqueue(HttpStatusCode.OK, """
                {"files":[
                  {"id":"f1","name":"Work.json","modifiedTime":"2026-11-04T10:00:00.000Z","size":"1234","appProperties":{"device":"PC-1","savedAt":"2026-11-04T09:59:00+00:00","profile":"Work"}},
                  {"id":"f2","name":"Home.json","modifiedTime":"2026-11-03T10:00:00.000Z"}
                ]}
                """);
        var tokens = new StaticTokens("tok");
        var client = new GoogleDriveClient(new HttpClient(handler), tokens);

        var files = await client.ListFilesAsync(CancellationToken.None);

        Assert.Equal(3, handler.Requests.Count);
        Assert.All(handler.Requests, r => Assert.Equal("tok", r.BearerToken));
        Assert.Contains("mimeType%20%3D%20%27application%2Fvnd.google-apps.folder%27", handler.Requests[0].Url);
        Assert.Equal(HttpMethod.Post, handler.Requests[1].Method);
        Assert.Contains("\"name\":\"DesktopIconGrid\"", handler.Requests[1].Body);
        Assert.Contains("%27newfolder%27%20in%20parents", handler.Requests[2].Url);

        Assert.Equal(2, files.Count);
        Assert.Equal("f1", files[0].Id);
        Assert.Equal(1234, files[0].Size);
        Assert.Equal("PC-1", files[0].AppProperties["device"]);
        Assert.Equal(new DateTimeOffset(2026, 11, 4, 10, 0, 0, TimeSpan.Zero), files[0].ModifiedTime);
        Assert.Empty(files[1].AppProperties);
        Assert.Null(files[1].Size);

        // The folder id is cached: a second listing only issues the list call.
        handler.Enqueue(HttpStatusCode.OK, EmptyList);
        await client.ListFilesAsync(CancellationToken.None);
        Assert.Equal(4, handler.Requests.Count);
    }

    [Fact]
    public async Task Upload_creates_multipart_with_parent_and_updates_without()
    {
        var handler = new FakeHttpHandler()
            .Enqueue(HttpStatusCode.OK, FolderList)
            .Enqueue(HttpStatusCode.OK, """{"id":"created","name":"Work.json","modifiedTime":"2026-11-04T10:00:00Z","appProperties":{"device":"PC-1"}}""")
            .Enqueue(HttpStatusCode.OK, """{"id":"created","name":"Work.json"}""");
        var client = new GoogleDriveClient(new HttpClient(handler), new StaticTokens("tok"));
        var properties = new Dictionary<string, string> { ["device"] = "PC-1" };

        var created = await client.UploadJsonAsync("Work.json", "{\"a\":1}", properties, null, CancellationToken.None);
        var updated = await client.UploadJsonAsync("Work.json", "{\"a\":2}", properties, "created", CancellationToken.None);

        var createRequest = handler.Requests[1];
        Assert.Equal(HttpMethod.Post, createRequest.Method);
        Assert.StartsWith(GoogleDriveClient.UploadBase + "files?uploadType=multipart", createRequest.Url);
        Assert.Equal("multipart/related", createRequest.MediaType);
        Assert.Contains("\"parents\":[\"folder1\"]", createRequest.Body);
        Assert.Contains("\"appProperties\":{\"device\":\"PC-1\"}", createRequest.Body);
        Assert.Contains("{\"a\":1}", createRequest.Body);
        Assert.Equal("created", created.Id);
        Assert.Equal("PC-1", created.AppProperties["device"]);

        var updateRequest = handler.Requests[2];
        Assert.Equal(HttpMethod.Patch, updateRequest.Method);
        Assert.StartsWith(GoogleDriveClient.UploadBase + "files/created?uploadType=multipart", updateRequest.Url);
        Assert.DoesNotContain("parents", updateRequest.Body);
        Assert.Contains("{\"a\":2}", updateRequest.Body);
        Assert.Equal("created", updated.Id);
    }

    [Fact]
    public async Task Download_and_delete_target_the_file()
    {
        var handler = new FakeHttpHandler()
            .Enqueue(HttpStatusCode.OK, "{\"x\":1}")
            .Enqueue(HttpStatusCode.NoContent, string.Empty);
        var client = new GoogleDriveClient(new HttpClient(handler), new StaticTokens("tok"));

        var text = await client.DownloadTextAsync("f1", CancellationToken.None);
        await client.DeleteAsync("f1", CancellationToken.None);

        Assert.Equal("{\"x\":1}", text);
        Assert.Equal(GoogleDriveClient.ApiBase + "files/f1?alt=media", handler.Requests[0].Url);
        Assert.Equal(HttpMethod.Delete, handler.Requests[1].Method);
        Assert.Equal(GoogleDriveClient.ApiBase + "files/f1", handler.Requests[1].Url);
    }

    [Fact]
    public async Task Unauthorized_response_invalidates_token_and_retries_once()
    {
        var handler = new FakeHttpHandler()
            .Enqueue(HttpStatusCode.Unauthorized, """{"error":{"message":"Invalid Credentials"}}""")
            .Enqueue(HttpStatusCode.OK, """{"user":{"emailAddress":"me@example.com","displayName":"Me"}}""");
        var tokens = new StaticTokens("tok");
        var client = new GoogleDriveClient(new HttpClient(handler), tokens);

        var user = await client.GetUserAsync(CancellationToken.None);

        Assert.Equal("me@example.com", user.EmailAddress);
        Assert.Equal(1, tokens.Invalidations);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Persistent_unauthorized_becomes_drive_exception()
    {
        var handler = new FakeHttpHandler()
            .Enqueue(HttpStatusCode.Unauthorized, "")
            .Enqueue(HttpStatusCode.Unauthorized, "");
        var client = new GoogleDriveClient(new HttpClient(handler), new StaticTokens("tok"));

        var ex = await Assert.ThrowsAsync<DriveException>(() => client.GetUserAsync(CancellationToken.None));

        Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);
    }

    [Fact]
    public async Task Api_error_message_is_surfaced()
    {
        var handler = new FakeHttpHandler()
            .Enqueue(HttpStatusCode.Forbidden, """{"error":{"code":403,"message":"Google Drive API has not been used in project 123 before or it is disabled."}}""");
        var client = new GoogleDriveClient(new HttpClient(handler), new StaticTokens("tok"));

        var ex = await Assert.ThrowsAsync<DriveException>(() => client.GetUserAsync(CancellationToken.None));

        Assert.Contains("has not been used in project 123", ex.Message);
        Assert.Contains("403", ex.Message);
    }

    [Fact]
    public async Task Network_failure_becomes_drive_exception()
    {
        var handler = new FakeHttpHandler().Enqueue(_ => throw new HttpRequestException("No such host is known."));
        var client = new GoogleDriveClient(new HttpClient(handler), new StaticTokens("tok"));

        var ex = await Assert.ThrowsAsync<DriveException>(() => client.GetUserAsync(CancellationToken.None));

        Assert.Contains("No such host", ex.Message);
    }
}
