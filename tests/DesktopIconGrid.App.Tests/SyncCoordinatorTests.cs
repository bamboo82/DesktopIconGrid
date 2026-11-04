using DesktopIconGrid.Core.Models;
using System.Net.Http;
using DesktopIconGrid.Core.Sync;
using DesktopIconGrid.Services;

namespace DesktopIconGrid.App.Tests;

/// <summary>The "tidy up, then quit" path: what FlushPendingAsync waits for and what it leaves armed.</summary>
public class SyncCoordinatorTests
{
    private sealed class FakeDrive : IDriveClient
    {
        private int _nextId = 1;

        public Dictionary<string, (string Content, Dictionary<string, string> Properties)> Files { get; } = [];

        public List<string> Log { get; } = [];

        /// <summary>When set, uploads wait here (an upload "in flight").</summary>
        public TaskCompletionSource? Gate { get; set; }

        public Task<DriveUser> GetUserAsync(CancellationToken cancellationToken) => Task.FromResult(new DriveUser("me@example.com", "Me"));

        public Task<IReadOnlyList<DriveFile>> ListFilesAsync(CancellationToken cancellationToken)
        {
            IReadOnlyList<DriveFile> files = Files
                .Select(f => new DriveFile(f.Key, f.Value.Properties["name"], null, f.Value.Content.Length, f.Value.Properties))
                .ToList();
            return Task.FromResult(files);
        }

        public async Task<DriveFile> UploadJsonAsync(string fileName, string json, IReadOnlyDictionary<string, string> appProperties, string? existingFileId, CancellationToken cancellationToken)
        {
            Log.Add(existingFileId is null ? $"create {fileName}" : $"update {existingFileId}");
            if (Gate is { } gate)
            {
                await gate.Task;
            }

            var id = existingFileId ?? $"id{_nextId++}";
            var properties = new Dictionary<string, string>(appProperties) { ["name"] = fileName };
            Files[id] = (json, properties);
            return new DriveFile(id, fileName, null, json.Length, properties);
        }

        public Task<string> DownloadTextAsync(string fileId, CancellationToken cancellationToken) => Task.FromResult(Files[fileId].Content);

        public Task DeleteAsync(string fileId, CancellationToken cancellationToken)
        {
            Files.Remove(fileId);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeHost : ISyncHost
    {
        public LauncherLayout Layout { get; set; } = new()
        {
            Items = [new ShortcutItem { Name = "Editor", Location = "C:\\ed.exe" }],
        };

        public event EventHandler? LayoutChanged;

        public LauncherLayout ExportLayout() => Layout;

        public void ReplaceLayout(LauncherLayout layout) => Layout = layout;

        public void Change() => LayoutChanged?.Invoke(this, EventArgs.Empty);
    }

    private static (SyncCoordinator Coordinator, FakeDrive Drive, FakeHost Host) Create()
    {
        var tokens = new InMemoryTokenStore { Token = new OAuthToken("at", "rt", DateTimeOffset.UtcNow.AddHours(1), null) };
        var auth = new GoogleAuthorization(new HttpClient(), tokens, () => new OAuthClientCredentials("id", "secret"));
        var drive = new FakeDrive();
        var settings = new SyncSettings
        {
            DeviceName = "PC-1",
            ClientId = "id",
            ClientSecret = "secret",
            LinkedProfile = "Work",
            AutoUpload = true,
        };
        var service = new SyncService(auth, drive, settings, () => { }, "0.2.0");
        var host = new FakeHost();
        return (new SyncCoordinator(service, host, (_, _) => true), drive, host);
    }

    [Fact]
    public async Task Flush_with_nothing_pending_returns_at_once()
    {
        var (coordinator, drive, _) = Create();

        Assert.True(await coordinator.FlushPendingAsync(TimeSpan.FromSeconds(5)));
        Assert.Empty(drive.Log);
    }

    [Fact]
    public async Task Flush_runs_the_debounced_upload_without_waiting_for_the_timer()
    {
        var (coordinator, drive, host) = Create();
        host.Change();
        Assert.True(coordinator.IsUploadScheduled);

        Assert.True(await coordinator.FlushPendingAsync(TimeSpan.FromSeconds(5)));

        Assert.Equal(["create Work.json"], drive.Log);
        Assert.False(coordinator.IsUploadScheduled);
        Assert.False(coordinator.IsUploading);
    }

    [Fact]
    public async Task Flush_waits_for_the_upload_already_running_then_sends_the_later_change_and_does_not_rearm()
    {
        var (coordinator, drive, host) = Create();
        drive.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var inFlight = coordinator.UploadNowAsync();
        Assert.True(coordinator.IsUploading);
        host.Change(); // edited while the upload runs

        var flush = coordinator.FlushPendingAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(100);
        Assert.False(flush.IsCompleted); // still waiting for the running upload
        drive.Gate.SetResult();

        Assert.True(await flush);
        await inFlight;
        Assert.Equal(["create Work.json", "update id1"], drive.Log);
        Assert.False(coordinator.IsUploadScheduled);
        Assert.False(coordinator.IsUploading);
    }

    [Fact]
    public async Task Flush_gives_up_after_the_timeout_when_the_upload_hangs()
    {
        var (coordinator, drive, _) = Create();
        drive.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var inFlight = coordinator.UploadNowAsync();

        Assert.False(await coordinator.FlushPendingAsync(TimeSpan.FromMilliseconds(200)));

        drive.Gate.SetResult();
        await inFlight;
    }
}
