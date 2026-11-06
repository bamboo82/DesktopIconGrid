using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Core.Sync;

namespace DesktopIconGrid.Core.Tests.Sync;

/// <summary>Settings copies in a folder that OneDrive, Dropbox or a share keeps in step: no sign-in, same rules.</summary>
public sealed class FolderSyncTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("dig-folder-sync-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    /// <summary>Stands in for Google Drive and fails the test if the folder backend ever reaches for it.</summary>
    private sealed class NoDrive : IDriveClient
    {
        public Task<DriveUser> GetUserAsync(CancellationToken cancellationToken) => throw new InvalidOperationException("Google Drive used");

        public Task<IReadOnlyList<DriveFile>> ListFilesAsync(CancellationToken cancellationToken) => throw new InvalidOperationException("Google Drive used");

        public Task<DriveFile> UploadJsonAsync(string fileName, string json, IReadOnlyDictionary<string, string> appProperties, string? existingFileId, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Google Drive used");

        public Task<string> DownloadTextAsync(string fileId, CancellationToken cancellationToken) => throw new InvalidOperationException("Google Drive used");

        public Task DeleteAsync(string fileId, CancellationToken cancellationToken) => throw new InvalidOperationException("Google Drive used");
    }

    /// <summary>A PC with no Google sign-in whose copies are kept in the test folder.</summary>
    private SyncService Device(string name, string? folder = null)
    {
        var auth = new GoogleAuthorization(new HttpClient(new FakeHttpHandler()), new InMemoryTokenStore(), () => new OAuthClientCredentials("id", "secret"));
        var settings = new SyncSettings { DeviceName = name, SyncFolder = folder ?? _folder };
        return new SyncService(auth, new NoDrive(), settings, () => { }, "0.8.0");
    }

    private static LauncherLayout Layout(params string[] names) => new()
    {
        Settings = new LauncherSettings { IconSize = 48 },
        Items = names.Select(n => (LauncherItem)new ShortcutItem { Name = n, Location = $@"C:\{n}.exe" }).ToList(),
    };

    private string PathOf(string fileName) => Path.Combine(_folder, fileName);

    [Fact]
    public async Task A_copy_is_saved_listed_loaded_and_deleted_without_a_sign_in()
    {
        var pc = Device("PC-1");
        Assert.True(pc.IsConnected);
        Assert.False(pc.IsGoogleConnected);

        var saved = await pc.SaveProfileAsync("Work", Layout("Editor", "Mail"), CancellationToken.None);

        Assert.True(File.Exists(PathOf("Work.json")));
        Assert.Equal("Work.json", saved.FileId);
        Assert.Equal("Work", pc.Settings.LinkedProfile);
        Assert.True(pc.IsLinked);
        var listed = Assert.Single(await pc.ListProfilesAsync(CancellationToken.None));
        Assert.Equal(("Work", "PC-1", pc.Settings.DeviceId), (listed.Name, listed.SavedBy, listed.SavedById));
        Assert.Equal(new DateTimeOffset(File.GetLastWriteTimeUtc(PathOf("Work.json")), TimeSpan.Zero), listed.SavedAt); // the file's time is the clock
        Assert.Equal(pc.Settings.LastSyncedAt, listed.SavedAt);

        var loaded = await Device("PC-2").LoadProfileAsync(listed, CancellationToken.None);
        Assert.Equal(["Editor", "Mail"], loaded.Layout.Items.Select(i => i.Name));

        await pc.DeleteProfileAsync(listed, CancellationToken.None);
        Assert.False(File.Exists(PathOf("Work.json")));
        Assert.Null(pc.Settings.LinkedProfile);
    }

    [Fact]
    public async Task Conflict_copies_are_listed_under_their_file_names_and_other_files_are_left_out()
    {
        var pc = Device("PC-1");
        await pc.SaveProfileAsync("Work", Layout("Editor"), CancellationToken.None);
        File.Copy(PathOf("Work.json"), PathOf("Work (1).json"));      // Dropbox
        File.Copy(PathOf("Work.json"), PathOf("Work-LAPTOP.json"));   // OneDrive
        File.WriteAllText(PathOf("notes.json"), "{ \"todo\": [] }");  // not a copy
        File.WriteAllText(PathOf("Half.json"), "{ \"layout\": ");      // still being synced
        File.WriteAllText(PathOf("readme.txt"), "x");

        var copies = await pc.ListProfilesAsync(CancellationToken.None);

        Assert.Equal(["Half", "Work", "Work (1)", "Work-LAPTOP"], copies.Select(c => c.Name).Order(StringComparer.Ordinal));
        var half = copies.Single(c => c.Name == "Half");
        Assert.Null(half.SavedBy); // unreadable for now: never taken for this PC's own copy
        Assert.Null(half.SavedById);
        Assert.Equal("PC-1", copies.Single(c => c.Name == "Work (1)").SavedBy);
    }

    [Fact]
    public async Task Names_Windows_forbids_in_file_names_are_kept_inside_the_copy()
    {
        var pc = Device("PC-1");

        await pc.SaveProfileAsync("Home: desk", Layout("A"), CancellationToken.None);
        await pc.SaveProfileAsync("Home? desk", Layout("B"), CancellationToken.None); // the same file name once made safe

        Assert.True(File.Exists(PathOf("Home_ desk.json")));
        Assert.True(File.Exists(PathOf("Home_ desk (2).json")));
        var copies = await pc.ListProfilesAsync(CancellationToken.None);
        Assert.Equal(2, copies.Count);
        Assert.Contains(copies, c => c.Name == "Home: desk" && c.FileId == "Home_ desk.json");
        Assert.Equal("Home? desk", (await pc.LoadProfileAsync(copies.Single(c => c.FileId == "Home_ desk (2).json"), CancellationToken.None)).Name);
    }

    [Fact]
    public async Task A_newer_save_from_another_PC_stops_the_automatic_upload_as_it_does_on_Google_Drive()
    {
        var desk = Device("DESK");
        var laptop = Device("LAPTOP");
        var copy = await desk.SaveProfileAsync("Work", Layout("Editor"), CancellationToken.None);
        await laptop.LoadProfileAsync(copy, CancellationToken.None);

        // The laptop saves later; the sync program brings its file over with the laptop's time.
        Assert.Equal(AutoUploadOutcome.Uploaded, (await laptop.AutoUploadAsync(Layout("Editor", "Mail"), CancellationToken.None)).Outcome);
        File.SetLastWriteTimeUtc(PathOf("Work.json"), DateTime.UtcNow.AddMinutes(1));

        var result = await desk.AutoUploadAsync(Layout("Editor", "Desk only"), CancellationToken.None);

        Assert.Equal(AutoUploadOutcome.Conflict, result.Outcome);
        Assert.Equal("LAPTOP", result.Remote!.SavedBy);
        Assert.Contains("Mail", File.ReadAllText(PathOf("Work.json")), StringComparison.Ordinal); // the laptop's copy stays
        Assert.Equal(AutoUploadOutcome.Uploaded, (await laptop.AutoUploadAsync(Layout("Editor"), CancellationToken.None)).Outcome); // its own copy
    }

    [Fact]
    public async Task Switching_between_Google_Drive_and_a_folder_unlinks_and_signing_out_of_Google_keeps_a_folder_link()
    {
        var pc = Device("PC-1", folder: null);
        pc.UseGoogleDrive();
        pc.Settings.LinkedProfile = "On Drive";
        pc.Settings.LinkedFileId = "drive-id";
        Assert.False(pc.UsesFolder);
        Assert.False(pc.IsConnected); // Google Drive without a sign-in

        var folder = Path.Combine(_folder, "Sync");
        pc.UseFolder(folder);

        Assert.True(Directory.Exists(folder));
        Assert.Equal(Path.GetFullPath(folder), pc.Settings.SyncFolder);
        Assert.Null(pc.Settings.LinkedProfile);
        Assert.Null(pc.Settings.LinkedFileId);

        await pc.SaveProfileAsync("Work", Layout("A"), CancellationToken.None);
        await pc.DisconnectAsync(CancellationToken.None);
        Assert.Equal("Work", pc.Settings.LinkedProfile);

        pc.UseGoogleDrive();
        Assert.Null(pc.Settings.SyncFolder);
        Assert.Null(pc.Settings.LinkedProfile);
    }

    [Fact]
    public async Task A_folder_that_is_not_there_says_so()
    {
        var gone = Path.Combine(_folder, "unplugged");
        var pc = Device("PC-1", gone);

        var error = await Assert.ThrowsAsync<DirectoryNotFoundException>(() => pc.ListProfilesAsync(CancellationToken.None));

        Assert.Contains(gone, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Work.json", "Work", "Work")]
    [InlineData("Work (1).json", "Work", "Work (1)")]
    [InlineData("Home_ desk.json", "Home: desk", "Home: desk")]
    [InlineData("Work.json", null, "Work")]
    [InlineData("work.json", "WORK", "WORK")]
    public void A_copy_shows_its_own_name_when_the_file_is_named_after_it(string fileName, string? inside, string shown)
    {
        Assert.Equal(shown, FolderSyncClient.DisplayName(fileName, inside));
    }
}
