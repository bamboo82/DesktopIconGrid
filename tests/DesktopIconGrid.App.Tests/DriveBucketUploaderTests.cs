using System.IO;
using System.Net.Http;
using DesktopIconGrid.Core.Sync;
using DesktopIconGrid.SpecialIcons;

namespace DesktopIconGrid.App.Tests;

/// <summary>The bucket uploader must list a Drive folder once per run, not once per dropped file (the rate-limit trigger).</summary>
public sealed class DriveBucketUploaderTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("dig-bucket-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private sealed class FakeFiles : IDriveFiles
    {
        private int _nextId = 1;
        private int _listings;

        public int Listings => Volatile.Read(ref _listings);

        public List<string> Log { get; } = [];

        public Dictionary<string, List<DriveEntry>> Children { get; } = new(StringComparer.Ordinal);

        public Task<DriveEntry?> GetEntryAsync(string id, CancellationToken cancellationToken) => Task.FromResult<DriveEntry?>(null);

        public Task<IReadOnlyList<DriveEntry>> ListChildrenAsync(string folderId, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _listings);
            Log.Add($"list {folderId}");
            IReadOnlyList<DriveEntry> result = Children.TryGetValue(folderId, out var list) ? list.ToList() : [];
            return Task.FromResult(result);
        }

        public Task<DriveEntry> CreateFolderAsync(string parentId, string name, CancellationToken cancellationToken)
        {
            var folder = new DriveEntry($"folder{_nextId++}", name, DriveEntry.FolderMimeType, null, null);
            Add(parentId, folder);
            Log.Add($"mkdir {parentId}/{name}");
            return Task.FromResult(folder);
        }

        public Task<DriveEntry> UploadAsync(string parentId, string name, string mimeType, Stream content, long length, string? existingFileId, IProgress<long>? progress, CancellationToken cancellationToken)
        {
            Log.Add(existingFileId is null ? $"upload {parentId}/{name}" : $"replace {existingFileId}");
            var entry = new DriveEntry(existingFileId ?? $"file{_nextId++}", name, mimeType, length, null);
            if (existingFileId is null)
            {
                Add(parentId, entry);
            }

            return Task.FromResult(entry);
        }

        public Task DownloadAsync(string fileId, Stream destination, IProgress<long>? progress, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task ExportAsync(string fileId, string exportMimeType, Stream destination, CancellationToken cancellationToken) => Task.CompletedTask;

        public void Add(string parentId, DriveEntry entry)
        {
            if (!Children.TryGetValue(parentId, out var list))
            {
                Children[parentId] = list = [];
            }

            list.Add(entry);
        }
    }

    private sealed class NoDrive : IDriveClient
    {
        public Task<DriveUser> GetUserAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<DriveFile>> ListFilesAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<DriveFile> UploadJsonAsync(string fileName, string json, IReadOnlyDictionary<string, string> appProperties, string? existingFileId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<string> DownloadTextAsync(string fileId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task DeleteAsync(string fileId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private static (DriveBucketUploader Uploader, FakeFiles Files, List<string> Errors) Create()
    {
        var files = new FakeFiles();
        var auth = new GoogleAuthorization(new HttpClient(), new InMemoryTokenStore(), () => new OAuthClientCredentials("id", "secret"));
        var service = new SyncService(auth, new NoDrive(), new SyncSettings(), () => { }, "0.2.0", files: files);
        var errors = new List<string>();
        return (new DriveBucketUploader(service, (title, message) => errors.Add($"{title}: {message}")), files, errors);
    }

    private static async Task DropAsync(DriveBucketUploader uploader, string policy, params string[] paths)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        uploader.Completed += (_, _) => done.TrySetResult();
        uploader.Enqueue("bucket", policy, paths);
        await done.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    private string File_(string name, string? directory = null)
    {
        var path = Path.Combine(directory ?? _directory, name);
        File.WriteAllText(path, name);
        return path;
    }

    [Fact]
    public async Task Loose_files_under_Replace_list_the_folder_once()
    {
        var (uploader, files, errors) = Create();

        await DropAsync(uploader, DriveBucketHandler.Replace, File_("a.txt"), File_("b.txt"), File_("c.txt"));

        Assert.Empty(errors);
        Assert.Equal(1, files.Listings);
        Assert.Equal(3, files.Log.Count(l => l.StartsWith("upload bucket/", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task A_dropped_folder_tree_lists_only_the_bucket_folder()
    {
        var folder = Path.Combine(_directory, "photos");
        var nested = Path.Combine(folder, "raw");
        Directory.CreateDirectory(nested);
        File_("1.jpg", folder);
        File_("2.jpg", folder);
        File_("3.raw", nested);
        var (uploader, files, errors) = Create();

        await DropAsync(uploader, DriveBucketHandler.Replace, folder);

        Assert.Empty(errors);
        Assert.Equal(1, files.Listings); // folders created just now are known to be empty
        Assert.Contains("mkdir bucket/photos", files.Log);
        Assert.Contains("mkdir folder1/raw", files.Log);
        Assert.Equal(3, files.Log.Count(l => l.StartsWith("upload ", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Keep_both_never_lists_the_folder()
    {
        var (uploader, files, _) = Create();

        await DropAsync(uploader, DriveBucketHandler.KeepBoth, File_("a.txt"), File_("b.txt"));

        Assert.Equal(0, files.Listings);
    }

    [Fact]
    public async Task Replace_and_Skip_act_on_the_existing_file()
    {
        var (uploader, files, _) = Create();
        files.Add("bucket", new DriveEntry("old", "a.txt", "text/plain", 1, null));

        await DropAsync(uploader, DriveBucketHandler.Replace, File_("a.txt"));
        await DropAsync(uploader, DriveBucketHandler.Skip, File_("a.txt"), File_("new.txt"));

        Assert.Contains("replace old", files.Log);
        Assert.Equal(1, files.Log.Count(l => l.StartsWith("upload ", StringComparison.Ordinal)));
        Assert.Contains("upload bucket/new.txt", files.Log);
    }
}
