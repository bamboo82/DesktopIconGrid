using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Core.Sync;

namespace DesktopIconGrid.Core.Tests.Sync;

public class SyncServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// In-memory stand-in for Drive: one file per name, with the metadata the real client would return. An update keeps
    /// the app properties it does not name, as Drive's PATCH does.
    /// </summary>
    private sealed class FakeDrive : IDriveClient
    {
        private int _nextId = 1;

        public Dictionary<string, (string Content, Dictionary<string, string> Properties)> Files { get; } = [];

        /// <summary>Drive's own clock, given to a file as its modified time when it is written; none when <see langword="null"/>.</summary>
        public DateTimeOffset? ServerTime { get; set; }

        public Dictionary<string, DateTimeOffset> ModifiedTimes { get; } = [];

        public List<string> Log { get; } = [];

        public Task<DriveUser> GetUserAsync(CancellationToken cancellationToken) => Task.FromResult(new DriveUser("me@example.com", "Me"));

        public Task<IReadOnlyList<DriveFile>> ListFilesAsync(CancellationToken cancellationToken)
        {
            Log.Add("list");
            IReadOnlyList<DriveFile> files = Files
                .Select(f => new DriveFile(f.Key, f.Value.Properties["name"], ModifiedTimes.TryGetValue(f.Key, out var modified) ? modified : null, f.Value.Content.Length, f.Value.Properties))
                .ToList();
            return Task.FromResult(files);
        }

        public Task<DriveFile> UploadJsonAsync(string fileName, string json, IReadOnlyDictionary<string, string> appProperties, string? existingFileId, CancellationToken cancellationToken)
        {
            Log.Add(existingFileId is null ? $"create {fileName}" : $"update {existingFileId}");
            var id = existingFileId ?? $"id{_nextId++}";
            var properties = existingFileId is not null && Files.TryGetValue(existingFileId, out var old)
                ? new Dictionary<string, string>(old.Properties)
                : [];
            foreach (var (key, value) in appProperties)
            {
                properties[key] = value;
            }

            properties["name"] = fileName;
            Files[id] = (json, properties);
            if (ServerTime is { } time)
            {
                ModifiedTimes[id] = time;
            }
            else
            {
                ModifiedTimes.Remove(id);
            }

            return Task.FromResult(new DriveFile(id, fileName, ServerTime, json.Length, properties));
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
    public async Task Save_replaces_the_linked_file_even_when_another_copy_has_the_same_name()
    {
        var (service, drive, settings, _) = CreateService();
        var properties = new Dictionary<string, string> { ["app"] = "DesktopIconGrid", ["profile"] = "Work" };
        await drive.UploadJsonAsync("Work.json", "{}", properties, null, CancellationToken.None); // id1
        await drive.UploadJsonAsync("Work.json", "{}", properties, null, CancellationToken.None); // id2
        settings.LinkedProfile = "Work";
        settings.LinkedFileId = "id2";

        await service.SaveProfileAsync("Work", SampleLayout(), CancellationToken.None);
        await service.SaveProfileAsync("Work", SampleLayout(), "id1", CancellationToken.None);

        Assert.Equal(2, drive.Files.Count);
        Assert.Equal(["update id2", "update id1"], drive.Log.Where(l => l.StartsWith("update", StringComparison.Ordinal)));
        Assert.Equal("id1", settings.LinkedFileId);
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
        // Same machine name on another installation still counts as another device when ids are present.
        Assert.True(SyncService.IsNewerThanLocal(new RemoteProfile("W", "id", Now.AddMinutes(5), "PC-1", null) { SavedById = "elsewhere" }, settings));
        Assert.False(SyncService.IsNewerThanLocal(new RemoteProfile("W", "id", Now.AddMinutes(5), "Other name", null) { SavedById = settings.DeviceId }, settings));
        Assert.False(SyncService.IsNewerThanLocal(new RemoteProfile("W", "id", Now.AddSeconds(1), "PC-2", null), settings));
        Assert.True(SyncService.IsNewerThanLocal(new RemoteProfile("W", "id", Now.AddSeconds(5), "PC-2", null), settings));
        Assert.False(SyncService.IsNewerThanLocal(new RemoteProfile("W", "id", null, "PC-2", null), settings));
        Assert.True(SyncService.IsNewerThanLocal(new RemoteProfile("W", "id", Now, "PC-2", null), new SyncSettings { DeviceName = "PC-1", LastSyncedAt = null }));
    }

    [Fact]
    public void With_the_same_revision_a_later_time_counts_on_Google_Drive_but_not_in_a_folder()
    {
        var onDrive = new SyncSettings { DeviceName = "DESK", LastSyncedAt = Now, LastRevision = "r1" };
        var inFolder = new SyncSettings { DeviceName = "DESK", LastSyncedAt = Now, LastRevision = "r1", SyncFolder = "/sync" };
        var later = new RemoteProfile("Work", "id", Now.AddMinutes(10), "LAPTOP", null) { SavedById = "laptop", Revision = "r1" };
        var sameTime = later with { SavedAt = Now.AddSeconds(1) };

        Assert.True(SyncService.IsNewerThanLocal(later, onDrive)); // Drive's own clock: a save from 0.7.0 kept the old revision
        Assert.False(SyncService.IsNewerThanLocal(sameTime, onDrive));
        Assert.False(SyncService.IsNewerThanLocal(later, inFolder)); // a sync program's file time says nothing there
        Assert.True(SyncService.IsNewerThanLocal(sameTime with { Revision = "r2" }, onDrive));
        Assert.True(SyncService.IsNewerThanLocal(sameTime with { Revision = "r2" }, inFolder));
        Assert.False(SyncService.IsNewerThanLocal(later with { SavedById = onDrive.DeviceId }, onDrive)); // this PC's own save
    }

    [Fact]
    public async Task On_Google_Drive_a_save_from_0_7_0_is_offered_although_Drive_kept_the_revision_before_it()
    {
        // The review's T1: an update names only the app properties it knows, Drive keeps the rest, and 0.7.0 knows no "revision".
        var (service, drive, settings, _) = CreateService();
        drive.ServerTime = Now;
        await service.SaveProfileAsync("Work", SampleLayout(), CancellationToken.None); // DESK, on 0.8.0
        var file = drive.Files.Single().Key;
        var desksRevision = settings.LastRevision;
        Assert.NotNull(desksRevision);

        async Task SaveOnLaptopWithVersion070(DateTimeOffset driveTime)
        {
            drive.ServerTime = driveTime;
            var copy = SyncProfile.FromLayout("Work", SampleLayout(), "LAPTOP", Now.AddHours(-1), "0.7.0", "laptop-install"); // its clock is an hour behind
            copy.Revision = null;
            await drive.UploadJsonAsync("Work.json", copy.ToJson(), new Dictionary<string, string>
            {
                ["app"] = "DesktopIconGrid",
                ["profile"] = "Work",
                ["device"] = "LAPTOP",
                ["savedAt"] = copy.SavedAt.ToString("O"),
                ["deviceId"] = "laptop-install",
            }, file, CancellationToken.None);
        }

        await SaveOnLaptopWithVersion070(Now.AddMinutes(10));
        Assert.Equal(desksRevision, drive.Files[file].Properties["revision"]);

        var offered = await service.CheckForNewerCopyAsync(CancellationToken.None);
        Assert.Equal("LAPTOP", offered?.SavedBy);
        var upload = await service.AutoUploadAsync(SampleLayout(), CancellationToken.None);
        Assert.Equal(AutoUploadOutcome.Conflict, upload.Outcome);
        Assert.Equal("LAPTOP", drive.Files[file].Properties["device"]); // not overwritten

        // DESK loads it, then LAPTOP saves again: the same old revision, and the same device as the copy DESK loaded.
        await service.LoadProfileAsync(offered!, CancellationToken.None);
        Assert.Null(await service.CheckForNewerCopyAsync(CancellationToken.None));
        await SaveOnLaptopWithVersion070(Now.AddMinutes(20));

        Assert.Equal("LAPTOP", (await service.CheckForNewerCopyAsync(CancellationToken.None))?.SavedBy);
        Assert.Equal(AutoUploadOutcome.Conflict, (await service.AutoUploadAsync(SampleLayout(), CancellationToken.None)).Outcome);
    }

    [Fact]
    public async Task CheckForNewerCopy_reports_only_other_devices_newer_saves()
    {
        var (service, drive, settings, _) = CreateService();
        await service.SaveProfileAsync("Work", SampleLayout(), CancellationToken.None);
        Assert.Null(await service.CheckForNewerCopyAsync(CancellationToken.None));

        // Simulate another device saving the same copy later (every save has a revision of its own).
        var (file, (content, properties)) = drive.Files.Single();
        properties["device"] = "PC-2";
        properties["deviceId"] = "other-install";
        properties["savedAt"] = Now.AddMinutes(10).ToString("O");
        properties["revision"] = "another-save";
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
        properties["deviceId"] = "other-install";
        properties["savedAt"] = Now.AddMinutes(10).ToString("O");
        properties["revision"] = "another-save";
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
            ["deviceId"] = "abc",
            ["savedAt"] = Now.AddMinutes(-1).ToString("O"),
        });

        var profile = RemoteProfile.FromFile(file)!;

        Assert.Equal("Work", profile.Name);
        Assert.Equal("PC-2", profile.SavedBy);
        Assert.Equal("abc", profile.SavedById);
        Assert.Equal(Now, profile.SavedAt); // Drive's modifiedTime wins over the device's own clock
        Assert.Equal(Now.AddMinutes(-1), RemoteProfile.FromFile(new DriveFile("id", "Work.json", null, 10, file.AppProperties))!.SavedAt);
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
            Assert.Equal(fresh.DeviceId, loaded.DeviceId);
            Assert.Equal(32, loaded.DeviceId.Length);
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
