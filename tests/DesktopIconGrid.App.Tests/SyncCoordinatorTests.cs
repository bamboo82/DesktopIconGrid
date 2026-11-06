using DesktopIconGrid.Core.Models;
using System.IO;
using System.Net.Http;
using DesktopIconGrid.Core.Sync;
using DesktopIconGrid.Resources;
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

        /// <summary>When set, downloads wait here (a load "in flight").</summary>
        public TaskCompletionSource? DownloadGate { get; set; }

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

        public async Task<string> DownloadTextAsync(string fileId, CancellationToken cancellationToken)
        {
            if (DownloadGate is { } gate)
            {
                await gate.Task;
            }

            return Files[fileId].Content;
        }

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

    private static LauncherLayout LayoutOf(string itemName) => new() { Items = [new ShortcutItem { Name = itemName, Location = "C:\\" + itemName + ".exe" }] };

    /// <summary>A PC with a copy "Work" to load, linked to "Home", whose bar has a change.</summary>
    private static async Task<(SyncCoordinator Coordinator, FakeDrive Drive, FakeHost Host, RemoteProfile Work)> LinkedToHomeWithWork()
    {
        var (coordinator, drive, host) = Create();
        await coordinator.Service.SaveProfileAsync("Work", LayoutOf("work-app"), CancellationToken.None);
        await coordinator.Service.SaveProfileAsync("Home", LayoutOf("home-app"), CancellationToken.None);
        var work = (await coordinator.Service.ListProfilesAsync(CancellationToken.None)).Single(p => p.Name == "Work");
        drive.Log.Clear();
        host.Layout = LayoutOf("home-app-moved");
        return (coordinator, drive, host, work);
    }

    [Fact]
    public async Task An_upload_due_while_a_copy_loads_takes_nothing_and_the_load_drops_it()
    {
        // Review 15 U1: the timer keeps running under the sync dialog; the bar it takes while a copy downloads (or after
        // the download, before the bar is replaced) must not go into the copy the load links to.
        var (coordinator, drive, host, work) = await LinkedToHomeWithWork();
        host.Change(); // the change is waiting for the timer
        Assert.True(coordinator.IsUploadScheduled);
        drive.DownloadGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var loading = coordinator.LoadAndApplyAsync(work, CancellationToken.None);
        await coordinator.UploadNowAsync(); // a tick during the download (the retry timer's, or a second change's)
        Assert.Empty(drive.Log);

        drive.DownloadGate.SetResult();
        await loading;

        Assert.Equal("work-app", Assert.Single(host.Layout.Items).Name);
        Assert.False(coordinator.IsUploadScheduled); // the change to the old bar is not uploaded after the load either
        Assert.False(coordinator.IsUploading);
        Assert.Empty(drive.Log);
        Assert.Contains("work-app", drive.Files[work.FileId].Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_upload_due_while_a_load_fails_is_due_again_afterwards()
    {
        var (coordinator, drive, _, work) = await LinkedToHomeWithWork();
        drive.DownloadGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var loading = coordinator.LoadAndApplyAsync(work, CancellationToken.None);
        await coordinator.UploadNowAsync(); // the timer's tick during the download, for the change
        Assert.False(coordinator.IsUploadScheduled);

        drive.DownloadGate.SetException(new HttpRequestException("offline"));
        await Assert.ThrowsAsync<HttpRequestException>(() => loading);

        Assert.Equal("Home", coordinator.Service.Settings.LinkedProfile);
        Assert.True(coordinator.IsUploadScheduled); // the bar is still Home's, with its change
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
    public async Task The_startup_question_points_out_the_copies_a_sync_program_kept_of_the_linked_copy()
    {
        // The review's F2: nothing told the user that OneDrive had kept "Work-LAPTOP" beside "Work".
        var folder = Directory.CreateTempSubdirectory("dig-coordinator-").FullName;
        try
        {
            SyncService Pc(string name) => new(
                new GoogleAuthorization(new HttpClient(), new InMemoryTokenStore(), () => new OAuthClientCredentials("id", "secret")),
                new FakeDrive(),
                new SyncSettings { DeviceName = name, SyncFolder = folder },
                () => { },
                "0.8.1");
            var desk = Pc("DESK");
            var laptop = Pc("LAPTOP");
            await laptop.LoadProfileAsync(await desk.SaveProfileAsync("Work", new LauncherLayout(), CancellationToken.None), CancellationToken.None);
            File.Copy(Path.Combine(folder, "Work.json"), Path.Combine(folder, "Work-LAPTOP.json"));
            await desk.SaveProfileAsync("Work", new LauncherLayout { Items = [new ShortcutItem { Name = "Desk", Location = "x" }] }, CancellationToken.None);
            string? asked = null;
            var coordinator = new SyncCoordinator(laptop, new FakeHost(), (_, message) =>
            {
                asked = message;
                return false;
            });

            await coordinator.RunStartupCheckAsync();

            Assert.NotNull(asked);
            Assert.Contains(string.Format(Strings.Sync_ConflictCopies, "\"Work-LAPTOP\"", "Work"), asked, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
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
