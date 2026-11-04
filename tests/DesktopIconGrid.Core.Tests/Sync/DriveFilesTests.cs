using System.Net;
using System.Text;
using DesktopIconGrid.Core.Sync;

namespace DesktopIconGrid.Core.Tests.Sync;

public class DriveFilesTests
{
    private sealed class StaticTokens : IAccessTokenProvider
    {
        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken) => Task.FromResult("tok");

        public void InvalidateAccessToken()
        {
        }
    }

    private static GoogleDriveClient Client(FakeHttpHandler handler) => new(new HttpClient(handler), new StaticTokens());

    [Theory]
    [InlineData("https://drive.google.com/drive/folders/1AbC_def-GHIjkl?usp=sharing", "1AbC_def-GHIjkl")]
    [InlineData("https://drive.google.com/drive/u/0/folders/1AbC_def-GHIjkl", "1AbC_def-GHIjkl")]
    [InlineData("https://drive.google.com/open?id=1AbC_def-GHIjkl", "1AbC_def-GHIjkl")]
    [InlineData("https://docs.google.com/document/d/1AbC_def-GHIjkl/edit", "1AbC_def-GHIjkl")]
    [InlineData("  1AbC_def-GHIjkl  ", "1AbC_def-GHIjkl")]
    public void Folder_links_and_ids_are_parsed(string input, string expected)
    {
        Assert.True(DriveFolderLink.TryParseId(input, out var id));
        Assert.Equal(expected, id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("https://example.com/folders/1AbC_def-GHIjkl")]
    [InlineData("C:\\Users\\me\\Documents")]
    public void Other_input_is_rejected(string input)
    {
        Assert.False(DriveFolderLink.TryParseId(input, out _));
    }

    [Fact]
    public void Entries_know_their_kind_and_export()
    {
        var folder = new DriveEntry("f", "Docs", DriveEntry.FolderMimeType, null, null);
        var doc = new DriveEntry("d", "Plan", "application/vnd.google-apps.document", null, null);
        var form = new DriveEntry("q", "Survey", "application/vnd.google-apps.form", null, null);
        var pdf = new DriveEntry("p", "scan.PDF", "application/pdf", 10, null);

        Assert.True(folder.IsFolder);
        Assert.False(folder.CanDownload);
        Assert.Equal("Folder", folder.KindText);
        Assert.True(doc.IsGoogleFormat);
        Assert.True(doc.CanDownload);
        Assert.Equal("Plan.docx", doc.DownloadFileName);
        Assert.Equal("Google Doc", doc.KindText);
        Assert.False(form.CanDownload);
        Assert.Equal("scan.PDF", pdf.DownloadFileName);
        Assert.Equal("PDF file", pdf.KindText);
        Assert.Equal("https://drive.google.com/drive/folders/f", folder.WebUrl);
        Assert.Equal("https://drive.google.com/open?id=p", pdf.WebUrl);
    }

    [Fact]
    public void Mime_types_come_from_extensions()
    {
        Assert.Equal("image/png", MimeTypes.FromFileName("C:\\x\\photo.PNG"));
        Assert.Equal(MimeTypes.Binary, MimeTypes.FromFileName("archive.unknown"));
    }

    [Fact]
    public async Task ListChildren_follows_pages()
    {
        var handler = new FakeHttpHandler()
            .Enqueue(HttpStatusCode.OK, """{"files":[{"id":"a","name":"Sub","mimeType":"application/vnd.google-apps.folder"}],"nextPageToken":"p2"}""")
            .Enqueue(HttpStatusCode.OK, """{"files":[{"id":"b","name":"file.txt","mimeType":"text/plain","size":"12","modifiedTime":"2026-11-04T10:00:00Z"}]}""");

        var entries = await Client(handler).ListChildrenAsync("folder1", CancellationToken.None);

        Assert.Equal(2, entries.Count);
        Assert.True(entries[0].IsFolder);
        Assert.Equal(12, entries[1].Size);
        Assert.Contains("%27folder1%27%20in%20parents", handler.Requests[0].Url);
        Assert.DoesNotContain("pageToken", handler.Requests[0].Url);
        Assert.Contains("pageToken=p2", handler.Requests[1].Url);
    }

    [Fact]
    public async Task GetEntry_returns_null_for_missing_files()
    {
        var handler = new FakeHttpHandler()
            .Enqueue(HttpStatusCode.NotFound, """{"error":{"message":"File not found: nope."}}""")
            .Enqueue(HttpStatusCode.OK, """{"id":"x","name":"Receipts","mimeType":"application/vnd.google-apps.folder"}""");
        var client = Client(handler);

        Assert.Null(await client.GetEntryAsync("nope", CancellationToken.None));
        var entry = await client.GetEntryAsync("x", CancellationToken.None);
        Assert.Equal("Receipts", entry!.Name);
    }

    [Fact]
    public async Task Small_uploads_use_multipart_with_parent()
    {
        var handler = new FakeHttpHandler()
            .Enqueue(HttpStatusCode.OK, """{"id":"new","name":"notes.txt","mimeType":"text/plain","size":"5"}""");
        var progress = new List<long>();
        using var content = new MemoryStream(Encoding.UTF8.GetBytes("hello"));

        var entry = await Client(handler).UploadAsync("parent1", "notes.txt", "text/plain", content, 5, null, new Progress<long>(progress.Add), CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.StartsWith(GoogleDriveClient.UploadBase + "files?uploadType=multipart", request.Url);
        Assert.Contains("\"parents\":[\"parent1\"]", request.Body);
        Assert.Contains("hello", request.Body);
        Assert.Equal("new", entry.Id);
    }

    [Fact]
    public async Task Large_uploads_open_a_session_and_send_chunks_with_content_range()
    {
        var size = GoogleDriveClient.MultipartLimit + 1;
        var data = new byte[size];
        Random.Shared.NextBytes(data);
        var sessionUri = new Uri("https://www.googleapis.com/upload/drive/v3/files?uploadType=resumable&upload_id=abc");

        var handler = new FakeHttpHandler()
            .Enqueue(_ =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(string.Empty) };
                response.Headers.Location = sessionUri;
                return response;
            })
            .Enqueue(HttpStatusCode.OK, """{"id":"big","name":"big.bin","mimeType":"application/octet-stream"}""");
        var progress = new List<long>();

        var entry = await Client(handler).UploadAsync("parent1", "big.bin", "application/octet-stream", new MemoryStream(data), size, null, new Progress<long>(progress.Add), CancellationToken.None);

        Assert.Equal("big", entry.Id);
        Assert.Equal(2, handler.Requests.Count);
        var open = handler.Requests[0];
        Assert.Equal(HttpMethod.Post, open.Method);
        Assert.Contains("uploadType=resumable", open.Url);
        Assert.Contains("\"parents\":[\"parent1\"]", open.Body);

        var chunk = handler.Requests[1];
        Assert.Equal(HttpMethod.Put, chunk.Method);
        Assert.Equal(sessionUri.AbsoluteUri, chunk.Url);
        Assert.Equal($"bytes 0-{size - 1}/{size}", chunk.ContentRange);
        Assert.Equal(size, chunk.BodyLength);
    }

    [Fact]
    public async Task Resumable_upload_continues_after_308_and_replaces_existing_files_with_patch()
    {
        var size = GoogleDriveClient.ChunkSize + 100;
        var data = new byte[size];
        var sessionUri = new Uri("https://www.googleapis.com/upload/drive/v3/files/existing?uploadType=resumable&upload_id=xyz");

        var handler = new FakeHttpHandler()
            .Enqueue(_ =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(string.Empty) };
                response.Headers.Location = sessionUri;
                return response;
            })
            .Enqueue(_ => new HttpResponseMessage((HttpStatusCode)308) { Content = new StringContent(string.Empty) })
            .Enqueue(HttpStatusCode.OK, """{"id":"existing","name":"big.bin","mimeType":"application/octet-stream"}""");

        await Client(handler).UploadAsync("parent1", "big.bin", "application/octet-stream", new MemoryStream(data), size, "existing", null, CancellationToken.None);

        Assert.Equal(HttpMethod.Patch, handler.Requests[0].Method);
        Assert.DoesNotContain("parents", handler.Requests[0].Body);
        Assert.Equal($"bytes 0-{GoogleDriveClient.ChunkSize - 1}/{size}", handler.Requests[1].ContentRange);
        Assert.Equal($"bytes {GoogleDriveClient.ChunkSize}-{size - 1}/{size}", handler.Requests[2].ContentRange);
    }

    [Fact]
    public async Task Download_streams_bytes_with_progress_and_export_uses_the_export_endpoint()
    {
        var payload = Encoding.UTF8.GetBytes("file content here");
        var handler = new FakeHttpHandler()
            .Enqueue(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) })
            .Enqueue(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) });
        var client = Client(handler);
        var progress = new List<long>();

        using var download = new MemoryStream();
        await client.DownloadAsync("f1", download, new Progress<long>(progress.Add), CancellationToken.None);
        using var export = new MemoryStream();
        await client.ExportAsync("d1", "application/pdf", export, CancellationToken.None);

        Assert.Equal(payload, download.ToArray());
        Assert.Equal(payload, export.ToArray());
        Assert.StartsWith(GoogleDriveClient.ApiBase + "files/f1?alt=media", handler.Requests[0].Url);
        Assert.Equal(GoogleDriveClient.ApiBase + "files/d1/export?mimeType=application%2Fpdf", handler.Requests[1].Url);
    }

    [Fact]
    public async Task CreateFolder_posts_metadata_with_parent()
    {
        var handler = new FakeHttpHandler().Enqueue(HttpStatusCode.OK, """{"id":"nf","name":"New","mimeType":"application/vnd.google-apps.folder"}""");

        var entry = await Client(handler).CreateFolderAsync("parent1", "New", CancellationToken.None);

        Assert.True(entry.IsFolder);
        Assert.Contains("\"parents\":[\"parent1\"]", Assert.Single(handler.Requests).Body);
    }
}
