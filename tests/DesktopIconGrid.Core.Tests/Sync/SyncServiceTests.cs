using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Core.Sync;

namespace DesktopIconGrid.Core.Tests.Sync;

public class SyncServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);

    /// <summary>In-memory stand-in for Drive: one file per name, with the metadata the real client would return.</summary>
    private sealed class FakeDrive : IDriveClient
    {
        private int _nextId = 1;

        public Dictionary<string, (string Content, Dictionary<string, string> Properties)> Files { get; } = [];

        public List<string> Log { get; } = [];

        public Task<DriveUser> GetUserAsync(CancellationToken cancellationToken) => Task.FromResult(new DriveUser("me@example.com", "Me"));

        public Task<IReadOnlyList<DriveFile>> ListFilesAsync(CancellationToken cancellationToken)
        {
            Log.Add("list");
            IReadOnlyList<DriveFile> files = Files
                .Select(f => new DriveFile(f.Key, f.Value.Properties["name"], null, f.Value.Content.Length, f.Value.Properties))
                .ToList();
            return Task.FromResult(files);
        }

        public Task<DriveFile> UploadJsonAsync(string fileName, string json, IReadOnlyDictionary<string, string> appProperties, string? existingFileId, CancellationToken cancellationToken)
        {
            Log.Add(existingFileId is null ? $"create {fileName}" : $"update {existingFileId}");
            var id = existingFileId ?? $"id{_nextId++}";
            var properties = new Dictionary<string, string>(appProperties) { ["name"] = fileName };
            Files[id] = (json, properties);
            return Task.FromResult(new DriveFile(id, fileName, null, json.Length, properties));
        }

        public Task<string> DownloadTextAsync(string fileId, CancellationToken cancellationToken)
        {
            Log.Add($"download {fileId}");
            return Task.FromResult(Files[fileId].Content);
        }

        public Task DeleteAsync(string fileId, CancellationToken cancellationToken)
        {
            Log.Add($"delete {fileId}");
            Files.Remove(fileId);
            return Task.CompletedTask;
        }
    }

    private static (SyncService Service, FakeDrive Drive, SyncSettings Settings, List<int> Saves) CreateService(bool connected = true, DateTimeOffset? now = null, FakeHttpHandler? http = null)
    {
        var store = new InMemoryTokenStore
        {
            Token = connected ? new OAuthToken("at", "rt", (now ?? Now).AddHours(1), null) : null,
        };
        var auth = new GoogleAuthorization(new HttpClient(http ?? new FakeHttpHandler()), store, () => new OAuthClientCredentials("id", "secret"), clock: () => now ?? Now);
        var drive = new FakeDrive();
        var settings = new SyncSettings { DeviceName = "PC-1", ClientId = "id", ClientSecret = "secret" };
        var saves = new List<int>();
        var service = new SyncService(auth, drive, settings, () => saves.Add(saves.Count), "0.1.0", () => now ?? Now);
        return (service, drive, settings, saves);
    }

    private static LauncherLayout SampleLayout() => new()
    {
        Settings = new LauncherSettings { IconSize = 48, ShowLabels = true },
        Window = new WindowPlacement { Left = 1, Top = 2, Width = 300, Height = 100 },
        Items = [new ShortcutItem { Name = "Editor", Location = "C:\\ed.exe" }, new GroupItem { Name = "G", Items = [new ShortcutItem { Name = "Inner", Location = "x" }] }],
    };

    [Fact]
    public async Task Save_creates_a_named_copy_and_links_the_device()
    {
        var (service, drive, settings, saves) = CreateService();

        var remote = await service.SaveProfileAsync("  Work ", SampleLayout(), CancellationToken.None);

        Assert.Equal("Work", remote.Name);
        Assert.Equal("PC-1", remote.SavedBy);
        Assert.Equal(Now, remote.SavedAt);
        Assert.Equal("Work", settings.LinkedProfile);
        Assert.Equal(Now, settings.LastSyncedAt);
        Assert.True(service.IsLinked);
        Assert.NotEmpty(saves);

        var (content, properties) = Assert.Single(drive.Files).Value;
        Assert.Equal("Work.json", properties["name"]);
        Assert.Equal("DesktopIconGrid", properties["app"]);
        var profile = SyncProfile.FromJson(content);
        Assert.Equal("Work", profile.Name);
        Assert.Equal("0.1.0", profile.AppVersion);
        Assert.Null(profile.Layout.Window);
        Assert.Equal(48, profile.Layout.Settings.IconSize);
        Assert.Equal(2, profile.Layout.Items.Count);
        Assert.IsType<GroupItem>(profile.Layout.Items[1]);
    }

    [Fact]
    public async Task Save_with_existing_name_overwrites_the_same_file()
    {
        var (service, drive, _, _) = CreateService();
        await service.SaveProfileAsync("Work", SampleLayout(), CancellationToken.None);

        await service.SaveProfileAsync("work", SampleLayout(), CancellationToken.None);

        Assert.Single(drive.Files);
        Assert.Contains("update id1", drive.Log);
    }

    [Fact]
    public async Task List_returns_profiles_sorted_by_name()
    {
        var (service, _, _, _) = CreateService();
        await service.SaveProfileAsync("zeta", SampleLayout(), CancellationToken.None);
        await service.SaveProfileAsync("Alpha", SampleLayout(), CancellationToken.None);

        var profiles = await service.ListProfilesAsync(CancellationToken.None);

        Assert.Equal(["Alpha", "zeta"], profiles.Select(p => p.Name));
    }

    [Fact]
    public async Task Load_returns_layout_and_links_the_device()
    {
        var (service, _, settings, _) = CreateService();
        var saved = await service.SaveProfileAsync("Work", SampleLayout(), CancellationToken.None);
        settings.LinkedProfile = null;
        settings.LastSyncedAt = null;

        var profile = await service.LoadProfileAsync(saved, CancellationToken.None);

        Assert.Equal("Work", profile.Name);
        Assert.Equal("Editor", profile.Layout.Items[0].Name);
        Assert.Null(profile.Layout.Window);
        Assert.Equal("Work", settings.LinkedProfile);
        Assert.Equal(saved.SavedAt, settings.LastSyncedAt);
    }

    [Fact]
    public async Task Load_rejects_files_that_are_not_profiles()
    {
        var (service, drive, _, _) = CreateService();
        drive.Files["bad"] = ("not json", new Dictionary<string, string> { ["name"] = "Bad.json" });

        await Assert.ThrowsAsync<FormatException>(() => service.LoadProfileAsync(new RemoteProfile("Bad", "bad", null, null, null), CancellationToken.None));
    }

    [Fact]
    public async Task Delete_unlinks_when_the_linked_copy_is_removed()
    {
        var (service, drive, settings, _) = CreateService();
        var saved = await service.SaveProfileAsync("Work", SampleLayout(), CancellationToken.None);

        await service.DeleteProfileAsync(saved, CancellationToken.None);

        Assert.Empty(drive.Files);
        Assert.Null(settings.LinkedProfile);
        Assert.False(service.IsLinked);
    }

    [Fact]
    public void Newer_copy_detection_ignores_own_device_and_clock_jitter()
    {
        var settings = new SyncSettings { DeviceName = "PC-1", LastSyncedAt = Now };

        Assert.False(SyncService.IsNewerThanLocal(new RemoteProfile("W", "id", Now.AddMinutes(5), "PC-1", null), settings));
        Assert.False(SyncService.IsNewerThanLocal(new RemoteProfile("W", "id", Now.AddSeconds(1), "PC-2", null), settings));
        Assert.True(SyncService.IsNewerThanLocal(new RemoteProfile("W", "id", Now.AddSeconds(5), "PC-2", null), settings));
        Assert.False(SyncService.IsNewerThanLocal(new RemoteProfile("W", "id", null, "PC-2", null), settings));
        Assert.True(SyncService.IsNewerThanLocal(new RemoteProfile("W", "id", Now, "PC-2", null), new SyncSettings { DeviceName = "PC-1", LastSyncedAt = null }));
    }

    [Fact]
    public async Task CheckForNewerCopy_reports_only_other_devices_newer_saves()
    {
        var (service, drive, settings, _) = CreateService();
        await service.SaveProfileAsync("Work", SampleLayout(), CancellationToken.None);
        Assert.Null(await service.CheckForNewerCopyAsync(CancellationToken.None));

        // Simulate another device saving the same copy later.
        var (file, (content, properties)) = drive.Files.Single();
        properties["device"] = "PC-2";
        properties["savedAt"] = Now.AddMinutes(10).ToString("O");
        drive.Files[file] = (content, properties);

        var newer = await service.CheckForNewerCopyAsync(CancellationToken.None);

        Assert.NotNull(newer);
        Assert.Equal("PC-2", newer.SavedBy);
        Assert.Equal("Work", settings.LinkedProfile);
    }

    [Fact]
    public async Task AutoUpload_skips_when_not_linked_or_disabled_and_detects_conflicts()
    {
        var (service, drive, settings, _) = CreateService();
        Assert.Equal(AutoUploadOutcome.Skipped, (await service.AutoUploadAsync(SampleLayout(), CancellationToken.None)).Outcome);

        await service.SaveProfileAsync("Work", SampleLayout(), CancellationToken.None);
        settings.AutoUpload = false;
        Assert.Equal(AutoUploadOutcome.Skipped, (await service.AutoUploadAsync(SampleLayout(), CancellationToken.None)).Outcome);

        settings.AutoUpload = true;
        var uploaded = await service.AutoUploadAsync(SampleLayout(), CancellationToken.None);
        Assert.Equal(AutoUploadOutcome.Uploaded, uploaded.Outcome);
        Assert.Single(drive.Files);

        var (file, (content, properties)) = drive.Files.Single();
        properties["device"] = "PC-2";
        properties["savedAt"] = Now.AddMinutes(10).ToString("O");
        drive.Files[file] = (content, properties);

        var conflict = await service.AutoUploadAsync(SampleLayout(), CancellationToken.None);
        Assert.Equal(AutoUploadOutcome.Conflict, conflict.Outcome);
        Assert.Equal("PC-2", conflict.Remote!.SavedBy);
        Assert.Equal("PC-2", drive.Files[file].Properties["device"]); // not overwritten
    }

    [Fact]
    public async Task Disconnect_forgets_account_and_link()
    {
        var http = new FakeHttpHandler().Enqueue(System.Net.HttpStatusCode.OK, "{}");
        var (service, _, settings, _) = CreateService(http: http);
        await service.SaveProfileAsync("Work", SampleLayout(), CancellationToken.None);
        settings.AccountEmail = "me@example.com";

        await service.DisconnectAsync(CancellationToken.None);

        Assert.False(service.IsConnected);
        Assert.Null(settings.AccountEmail);
        Assert.Null(settings.LinkedProfile);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("name.json")]
    public void Invalid_profile_names_are_rejected(string name)
    {
        Assert.Throws<ArgumentException>(() => SyncService.NormalizeProfileName(name));
    }

    [Fact]
    public void Remote_profile_is_read_from_file_metadata()
    {
        var file = new DriveFile("id", "Work.json", Now, 10, new Dictionary<string, string>
        {
            ["profile"] = "Work",
            ["device"] = "PC-2",
            ["savedAt"] = Now.AddMinutes(-1).ToString("O"),
        });

        var profile = RemoteProfile.FromFile(file)!;

        Assert.Equal("Work", profile.Name);
        Assert.Equal("PC-2", profile.SavedBy);
        Assert.Equal(Now.AddMinutes(-1), profile.SavedAt);
        Assert.Equal("Work.json", profile.FileName);
        Assert.Null(RemoteProfile.FromFile(new DriveFile("id", "notes.txt", null, null, new Dictionary<string, string>())));
        Assert.Equal(Now, RemoteProfile.FromFile(new DriveFile("id", "Plain.json", Now, null, new Dictionary<string, string>()))!.SavedAt);
    }

    [Fact]
    public void Sync_settings_store_round_trips_and_normalizes()
    {
        var directory = Path.Combine(Path.GetTempPath(), "DesktopIconGrid.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new SyncSettingsStore(Path.Combine(directory, "sync.json"));
            var fresh = store.Load();
            Assert.False(string.IsNullOrWhiteSpace(fresh.DeviceName));
            Assert.True(fresh.AutoUpload);

            fresh.DeviceName = "  Laptop ";
            fresh.ClientId = " id ";
            fresh.LinkedProfile = "Work";
            fresh.LastSyncedAt = Now;
            store.Save(fresh);

            var loaded = store.Load();
            Assert.Equal("Laptop", loaded.DeviceName);
            Assert.Equal("id", loaded.ClientId);
            Assert.Equal("Work", loaded.LinkedProfile);
            Assert.Equal(Now, loaded.LastSyncedAt);

            File.WriteAllText(store.FilePath, "{ broken");
            Assert.NotNull(store.Load());
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
