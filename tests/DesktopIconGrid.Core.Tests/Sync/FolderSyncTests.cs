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
    public async Task A_copy_whose_file_name_another_file_holds_keeps_its_name_and_every_PC_one_file()
    {
        // Review 15 U3: another program's config.json made DESK's "config" config (2).json, listed as a sync program's
        // copy of a missing "config". DESK's next upload renamed it "config (2)" (the listed name), and LAPTOP, which
        // loaded it and linked by name, made config (3).json at its first upload: two files, both PCs "up to date".
        File.WriteAllText(PathOf("config.json"), "{\"theme\":\"another program's\"}");
        var desk = Device("DESK");
        var laptop = Device("LAPTOP");
        await desk.SaveProfileAsync("config", Layout("a"), CancellationToken.None);
        Assert.Equal("config (2).json", desk.Settings.LinkedFileId);

        Assert.Equal(AutoUploadOutcome.Uploaded, (await desk.AutoUploadAsync(Layout("b"), CancellationToken.None)).Outcome);
        Assert.Equal("config", SyncProfile.FromJson(File.ReadAllText(PathOf("config (2).json"))).Name); // not renamed

        var listed = (await laptop.ListProfilesAsync(CancellationToken.None)).Single(c => c.FileId == "config (2).json");
        await laptop.LoadProfileAsync(listed, CancellationToken.None);
        Assert.Equal("config (2).json", laptop.Settings.LinkedFileId); // no original, and its file name is taken: stays with this file
        Assert.Equal(AutoUploadOutcome.Uploaded, (await laptop.AutoUploadAsync(Layout("c"), CancellationToken.None)).Outcome);

        Assert.False(File.Exists(PathOf("config (3).json")));
        var deskAgain = await desk.AutoUploadAsync(Layout("d"), CancellationToken.None);
        Assert.Equal(AutoUploadOutcome.Conflict, deskAgain.Outcome); // the same file: DESK sees LAPTOP's save
        Assert.Equal("config (2).json", deskAgain.Remote?.FileId);
        Assert.Equal("{\"theme\":\"another program's\"}", File.ReadAllText(PathOf("config.json")));
    }

    [Fact]
    public async Task A_save_from_the_dialog_leaves_a_copy_that_cannot_be_read_just_now()
    {
        // Review 11, still open in 15: only the automatic upload waited for a copy a sync program may be writing.
        var desk = Device("DESK");
        await desk.SaveProfileAsync("Work", Layout("a"), CancellationToken.None);
        File.WriteAllText(PathOf("Work.json"), "{\"name\":\"Work\",\"layout\":"); // half written

        var refused = await Assert.ThrowsAsync<CopyBusyException>(() => desk.SaveProfileAsync("Work", Layout("b"), CancellationToken.None));

        Assert.Contains("Work", refused.Message, StringComparison.Ordinal);
        Assert.Equal("{\"name\":\"Work\",\"layout\":", File.ReadAllText(PathOf("Work.json")));
    }

    [Fact]
    public async Task A_numbered_original_is_listed_under_its_own_name_and_a_sync_programs_copy_of_it_links_there()
    {
        // Review 16 U3′: config (2).json, numbered because another program held config.json, was listed as a sync
        // program's copy of a "config" that did not exist: wrong questions, no question on Save, and a conflict copy of it
        // pinned the PC that loaded it.
        File.WriteAllText(PathOf("config.json"), "{\"theme\":\"another program's\"}");
        var desk = Device("DESK");
        await desk.SaveProfileAsync("config", Layout("a"), CancellationToken.None);
        File.Copy(PathOf("config (2).json"), PathOf("config (2)-LAPTOP.json")); // OneDrive's copy of it

        var copies = await Device("LAPTOP").ListProfilesAsync(CancellationToken.None);
        var original = copies.Single(c => c.FileId == "config (2).json");
        Assert.Equal("config", original.Name);
        Assert.False(original.IsConflictCopy);
        var conflict = copies.Single(c => c.FileId == "config (2)-LAPTOP.json");
        Assert.True(conflict.IsConflictCopy);

        var laptop = Device("LAPTOP");
        await laptop.LoadProfileAsync(conflict, CancellationToken.None);
        Assert.Equal("config (2).json", laptop.Settings.LinkedFileId); // to the original, not the copy
    }

    [Fact]
    public async Task A_conflict_copy_loaded_while_its_original_cannot_be_read_links_to_the_original()
    {
        // Review 16 U5: "Home: desk" in Home_ desk.json could not be read, so the original was not found; its file name was
        // taken, and the PC stayed on OneDrive's copy without a word.
        var desk = Device("DESK");
        await desk.SaveProfileAsync("Home: desk", Layout("a"), CancellationToken.None);
        File.Copy(PathOf("Home_ desk.json"), PathOf("Home_ desk-LAPTOP.json"));
        File.WriteAllText(PathOf("Home_ desk.json"), "{\"name\":\"Home: desk\",\"layout\":"); // being written

        var laptop = Device("LAPTOP");
        var conflict = (await laptop.ListProfilesAsync(CancellationToken.None)).Single(c => c.FileId == "Home_ desk-LAPTOP.json");
        await laptop.LoadProfileAsync(conflict, CancellationToken.None);

        Assert.Equal("Home_ desk.json", laptop.Settings.LinkedFileId);
        Assert.Equal("Home: desk", laptop.Settings.LinkedProfile);
    }

    [Fact]
    public async Task While_a_copy_cannot_be_read_a_numbered_file_of_its_name_stays_a_sync_programs_copy()
    {
        // Review 17 U8: with Work.json unreadable (a new PC still downloading it), Explorer's or a sync program's
        // Work (2).json was listed as "Work": saves went into it, and PCs linked to it stayed there.
        var desk = Device("DESK");
        await desk.SaveProfileAsync("Work", Layout("a"), CancellationToken.None);
        File.Copy(PathOf("Work.json"), PathOf("Work (2).json"));
        File.WriteAllText(PathOf("Work.json"), "{\n  \"format\": 1,\n  \"name\": \"Work\",\n  \"lay"); // being written

        var laptop = Device("LAPTOP");
        var copies = await laptop.ListProfilesAsync(CancellationToken.None);
        Assert.True(copies.Single(c => c.FileId == "Work (2).json").IsConflictCopy);
        Assert.Equal("Work", copies.Single(c => c.FileId == "Work.json").Name);

        await Assert.ThrowsAsync<CopyBusyException>(() => laptop.SaveProfileAsync("Work", Layout("b"), CancellationToken.None));
        Assert.Equal("Work", SyncProfile.FromJson(File.ReadAllText(PathOf("Work (2).json"))).Name);
        Assert.Equal(["a"], SyncProfile.FromJson(File.ReadAllText(PathOf("Work (2).json"))).Layout.Items.Select(i => i.Name));
    }

    [Fact]
    public async Task A_save_waits_while_the_file_of_its_own_name_cannot_be_read()
    {
        // Review 17 U8, for a name Windows does not allow in a file name: Home_ desk.json, being written, is listed under
        // its file name, and a save of "Home: desk" made Home_ desk (2).json beside it, a second "Home: desk".
        var desk = Device("DESK");
        await desk.SaveProfileAsync("Home: desk", Layout("a"), CancellationToken.None);
        File.WriteAllText(PathOf("Home_ desk.json"), "{\"format\": 1, \"name\": \"Home: d"); // being written

        await Assert.ThrowsAsync<CopyBusyException>(() => Device("LAPTOP").SaveProfileAsync("Home: desk", Layout("b"), CancellationToken.None));

        Assert.False(File.Exists(PathOf("Home_ desk (2).json")));
    }

    [Fact]
    public async Task Of_two_numbered_originals_only_the_lowest_is_one()
    {
        // Review 17 U9: DESK's "config" became config (2).json, and LAPTOP, which could not read it yet, made
        // config (3).json: both were listed as "config", and the two PCs split, both "up to date".
        File.WriteAllText(PathOf("config.json"), "{\"theme\":\"another program's\"}");
        var desk = Device("DESK");
        await desk.SaveProfileAsync("config", Layout("a"), CancellationToken.None);
        File.Copy(PathOf("config (2).json"), PathOf("config (3).json"));

        var copies = await desk.ListProfilesAsync(CancellationToken.None);
        Assert.Equal("config", copies.Single(c => c.FileId == "config (2).json").Name);
        var third = copies.Single(c => c.FileId == "config (3).json");
        Assert.Equal("config (3)", third.Name);
        Assert.True(third.IsConflictCopy);
        Assert.Equal(["config (3).json"], (await desk.CheckLinkedCopyAsync(CancellationToken.None)).ConflictCopies.Select(c => c.FileId)); // named at start-up
    }

    [Fact]
    public async Task A_sync_programs_numbered_copy_of_a_name_with_forbidden_characters_is_no_second_original()
    {
        // Review 17, U3′ left over: Home_ desk (2).json, a copy of a readable Home_ desk.json, was listed as "Home: desk" too.
        var desk = Device("DESK");
        await desk.SaveProfileAsync("Home: desk", Layout("a"), CancellationToken.None);
        File.Copy(PathOf("Home_ desk.json"), PathOf("Home_ desk (2).json"));

        var copies = await desk.ListProfilesAsync(CancellationToken.None);

        Assert.Equal("Home: desk", copies.Single(c => c.FileId == "Home_ desk.json").Name);
        Assert.True(copies.Single(c => c.FileId == "Home_ desk (2).json").IsConflictCopy);
    }

    [Fact]
    public async Task Another_programs_json_that_does_not_parse_is_no_copy()
    {
        // Review 17 U10: VS Code's settings (with comments) or an empty file were listed as copies that could not be
        // read, and a save of that name was told to delete the other program's file.
        File.WriteAllText(PathOf("settings.json"), "{\n  // the editor\n  \"editor.fontSize\": 14,\n}");
        File.WriteAllText(PathOf("empty.json"), string.Empty);
        var pc = Device("PC-1");

        Assert.Empty(await pc.ListProfilesAsync(CancellationToken.None));
        var saved = await pc.SaveProfileAsync("settings", Layout("a"), CancellationToken.None);

        Assert.Equal("settings (2).json", saved.FileId);
        Assert.Equal("settings", saved.Name); // said as the copy's name, not its file's
        Assert.Equal("{\n  // the editor\n  \"editor.fontSize\": 14,\n}", File.ReadAllText(PathOf("settings.json")));
    }

    [Fact]
    public async Task A_numbered_copy_is_named_by_its_own_name_when_saved_and_uploaded()
    {
        // Review 17: "Saved "config (2)"" and "Uploaded to "config (2)"" for a copy called config.
        File.WriteAllText(PathOf("config.json"), "{\"theme\":\"another program's\"}");
        var desk = Device("DESK");

        var saved = await desk.SaveProfileAsync("config", Layout("a"), CancellationToken.None);
        var uploaded = await desk.AutoUploadAsync(Layout("b"), CancellationToken.None);

        Assert.Equal(("config", "config (2).json"), (saved.Name, saved.FileId));
        Assert.Equal(AutoUploadOutcome.Uploaded, uploaded.Outcome);
        Assert.Equal("config", uploaded.Remote?.Name);
    }

    [Fact]
    public async Task A_conflict_copy_does_not_take_an_unreadable_file_for_its_original_while_numbered_ones_are_listed()
    {
        // Review 17, U3′ left over: U5's fallback linked "Home: desk" to Home_ desk.json, which may hold "Home? desk".
        var desk = Device("DESK");
        await desk.SaveProfileAsync("Home? desk", Layout("a"), CancellationToken.None);
        await desk.SaveProfileAsync("Home: desk", Layout("b"), CancellationToken.None); // Home_ desk (2).json
        File.Copy(PathOf("Home_ desk (2).json"), PathOf("Home_ desk-LAPTOP.json"));
        File.WriteAllText(PathOf("Home_ desk.json"), "{\"format\": 1, \"name\": \"Home? desk\", \"lay"); // being written

        var laptop = Device("LAPTOP");
        var conflict = (await laptop.ListProfilesAsync(CancellationToken.None)).Single(c => c.FileId == "Home_ desk-LAPTOP.json");
        await laptop.LoadProfileAsync(conflict, CancellationToken.None);

        Assert.Equal("Home: desk", laptop.Settings.LinkedProfile);
        Assert.NotEqual("Home_ desk.json", laptop.Settings.LinkedFileId);
    }

    [Fact]
    public async Task A_file_with_words_in_brackets_does_not_count_as_numbered()
    {
        // Review 18 D5: "Work (old).json" turned off the fallback to Work.json, which only numbered files should.
        var desk = Device("DESK");
        await desk.SaveProfileAsync("Work", Layout("a"), CancellationToken.None);
        File.Copy(PathOf("Work.json"), PathOf("Work-LAPTOP.json"));
        File.Copy(PathOf("Work.json"), PathOf("Work (old).json"));
        File.WriteAllText(PathOf("Work.json"), "{\"format\": 1, \"name\": \"Wo"); // being written

        var laptop = Device("LAPTOP");
        var conflict = (await laptop.ListProfilesAsync(CancellationToken.None)).Single(c => c.FileId == "Work-LAPTOP.json");
        await laptop.LoadProfileAsync(conflict, CancellationToken.None);

        Assert.Equal("Work.json", laptop.Settings.LinkedFileId);
    }

    [Fact]
    public async Task A_copy_loaded_from_a_listing_made_while_it_could_not_be_read_is_linked_under_the_name_it_holds()
    {
        // Review 17, U3′ left over: the PC kept "Home_ desk", the listed name, until its first upload.
        var desk = Device("DESK");
        await desk.SaveProfileAsync("Home: desk", Layout("a"), CancellationToken.None);
        var whole = File.ReadAllText(PathOf("Home_ desk.json"));
        File.WriteAllText(PathOf("Home_ desk.json"), "{\"format\": 1, \"name\": \"Home: d"); // being written
        var laptop = Device("LAPTOP");
        var listed = (await laptop.ListProfilesAsync(CancellationToken.None)).Single(c => c.FileId == "Home_ desk.json");
        Assert.Equal("Home_ desk", listed.Name);
        File.WriteAllText(PathOf("Home_ desk.json"), whole); // written by now

        await laptop.LoadProfileAsync(listed, CancellationToken.None);

        Assert.Equal("Home: desk", laptop.Settings.LinkedProfile);
        Assert.Equal("Home_ desk.json", laptop.Settings.LinkedFileId);
    }

    [Fact]
    public async Task An_upload_to_a_copy_another_program_holds_open_is_postponed()
    {
        // Review 17, U7 left over: a sharing violation when the new file took the copy's place said "upload failed",
        // with no retry.
        var desk = Device("DESK");
        await desk.SaveProfileAsync("Work", Layout("a"), CancellationToken.None);
        if (!OperatingSystem.IsWindows())
        {
            return; // only Windows keeps a file that is open from being replaced
        }

        using (new FileStream(PathOf("Work.json"), FileMode.Open, FileAccess.Read, FileShare.Read)) // readable, not replaceable
        {
            var result = await desk.AutoUploadAsync(Layout("b"), CancellationToken.None);

            Assert.Equal(AutoUploadOutcome.Postponed, result.Outcome);
        }

        Assert.Equal(["a"], SyncProfile.FromJson(File.ReadAllText(PathOf("Work.json"))).Layout.Items.Select(i => i.Name));
    }

    [Fact]
    public async Task A_name_from_a_listing_made_while_a_copy_could_not_be_read_does_not_rename_it()
    {
        // Review 16 U6: the dialog listed Home_ desk.json as "Home_ desk"; a few seconds later Overwrite wrote that name in.
        var desk = Device("DESK");
        await desk.SaveProfileAsync("Home: desk", Layout("a"), CancellationToken.None);

        await desk.SaveProfileAsync("Home_ desk", Layout("b"), "Home_ desk.json", CancellationToken.None);

        Assert.Equal("Home: desk", SyncProfile.FromJson(File.ReadAllText(PathOf("Home_ desk.json"))).Name);
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
        var second = copies.Single(c => c.FileId == "Home_ desk (2).json");
        Assert.Equal("Home? desk", second.Name); // its own name, with the number its file got
        Assert.False(second.IsConflictCopy);
        Assert.Equal("Home? desk", (await pc.LoadProfileAsync(second, CancellationToken.None)).Name);

        // The review: an upload took the file's name for the copy's and renamed it "Home_ desk (2)".
        Assert.Equal(AutoUploadOutcome.Uploaded, (await pc.AutoUploadAsync(Layout("B", "C"), CancellationToken.None)).Outcome);
        Assert.Equal("Home? desk", SyncProfile.FromJson(File.ReadAllText(PathOf("Home_ desk (2).json"))).Name);
        Assert.Equal(2, (await pc.ListProfilesAsync(CancellationToken.None)).Count);
    }

    [Fact]
    public async Task Loading_a_conflict_copy_keeps_this_PC_on_the_copy_every_PC_uploads_to()
    {
        // The review's F2: LAPTOP loaded "Work-LAPTOP" and was linked to that file from then on, while DESK stayed on
        // Work.json. Each uploaded to its own file, and both said "up to date".
        var desk = Device("DESK");
        var laptop = Device("LAPTOP");
        await laptop.LoadProfileAsync(await desk.SaveProfileAsync("Work", Layout("Editor"), CancellationToken.None), CancellationToken.None);
        Assert.Equal(AutoUploadOutcome.Uploaded, (await laptop.AutoUploadAsync(Layout("Editor", "Laptop"), CancellationToken.None)).Outcome);
        File.Copy(PathOf("Work.json"), PathOf("Work-LAPTOP.json")); // OneDrive keeps LAPTOP's offline save aside ...
        await desk.SaveProfileAsync("Work", Layout("Editor", "Desk"), CancellationToken.None); // ... and DESK's as Work.json

        var listed = await laptop.ListProfilesAsync(CancellationToken.None);
        var conflictCopy = listed.Single(c => c.FileId == "Work-LAPTOP.json");
        Assert.True(conflictCopy.IsConflictCopy);
        Assert.Equal(("Work-LAPTOP", "Work"), (conflictCopy.Name, conflictCopy.NameInside));
        Assert.False(listed.Single(c => c.FileId == "Work.json").IsConflictCopy);

        var check = await laptop.CheckLinkedCopyAsync(CancellationToken.None);
        Assert.Equal("DESK", check.Newer?.SavedBy);
        Assert.Equal("Work-LAPTOP", Assert.Single(check.ConflictCopies).Name); // the start-up question points it out

        var loaded = await laptop.LoadProfileAsync(conflictCopy, CancellationToken.None);

        Assert.Equal(["Editor", "Laptop"], loaded.Layout.Items.Select(i => i.Name));
        Assert.Equal(("Work", "Work.json"), (laptop.Settings.LinkedProfile, laptop.Settings.LinkedFileId));
        var upload = await laptop.AutoUploadAsync(loaded.Layout, CancellationToken.None);
        Assert.Equal(AutoUploadOutcome.Conflict, upload.Outcome); // DESK's save is not replaced without a question
        Assert.Equal("Work-LAPTOP", Assert.Single(upload.ConflictCopies).Name);
        Assert.Contains("Desk", File.ReadAllText(PathOf("Work.json")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_conflict_copy_whose_original_is_gone_links_by_name_and_the_next_upload_writes_the_original_again()
    {
        var pc = Device("PC-1");
        await pc.SaveProfileAsync("Work", Layout("Editor"), CancellationToken.None);
        File.Move(PathOf("Work.json"), PathOf("Work (1).json")); // only the sync program's copy is left
        var copy = Assert.Single(await pc.ListProfilesAsync(CancellationToken.None));
        Assert.True(copy.IsConflictCopy);

        await pc.LoadProfileAsync(copy, CancellationToken.None);

        Assert.Equal("Work", pc.Settings.LinkedProfile);
        Assert.Null(pc.Settings.LinkedFileId);
        Assert.Equal(AutoUploadOutcome.Uploaded, (await pc.AutoUploadAsync(Layout("Editor", "Mail"), CancellationToken.None)).Outcome);
        Assert.True(File.Exists(PathOf("Work.json")));
        Assert.True(File.Exists(PathOf("Work (1).json"))); // left as it was
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
    public async Task Another_PCs_save_stops_the_upload_even_when_its_file_time_is_older_than_this_PCs_last_sync()
    {
        // The review: LAPTOP saved offline at 10:05; OneDrive then kept DESK's 10:00 save as Work.json (with DESK's own
        // time) and LAPTOP's as a conflict copy. The times said "not newer", and LAPTOP's next upload overwrote DESK's.
        var desk = Device("DESK");
        var laptop = Device("LAPTOP");
        await laptop.LoadProfileAsync(await desk.SaveProfileAsync("Work", Layout("Editor"), CancellationToken.None), CancellationToken.None);
        Assert.Equal(AutoUploadOutcome.Uploaded, (await laptop.AutoUploadAsync(Layout("Editor", "Laptop"), CancellationToken.None)).Outcome);
        File.Copy(PathOf("Work.json"), PathOf("Work-LAPTOP.json"));
        await desk.SaveProfileAsync("Work", Layout("Editor", "Desk"), CancellationToken.None);
        File.SetLastWriteTimeUtc(PathOf("Work.json"), DateTime.UtcNow.AddMinutes(-5));

        var result = await laptop.AutoUploadAsync(Layout("Editor", "Laptop", "More"), CancellationToken.None);

        Assert.Equal(AutoUploadOutcome.Conflict, result.Outcome);
        Assert.Equal("DESK", result.Remote!.SavedBy);
        Assert.Contains("Desk", File.ReadAllText(PathOf("Work.json")), StringComparison.Ordinal); // DESK's save stays
        Assert.NotNull(await laptop.CheckForNewerCopyAsync(CancellationToken.None)); // and is offered at start-up
    }

    [Fact]
    public async Task A_linked_copy_that_cannot_be_read_postpones_the_upload_and_is_left_alone()
    {
        var pc = Device("PC-1");
        await pc.SaveProfileAsync("Work", Layout("Editor"), CancellationToken.None);
        const string halfWritten = "{ \"name\": \"Work\", \"layout\": {";
        File.WriteAllText(PathOf("Work.json"), halfWritten); // a sync program still writing it

        var result = await pc.AutoUploadAsync(Layout("Editor", "Mail"), CancellationToken.None);

        Assert.Equal(AutoUploadOutcome.Postponed, result.Outcome);
        Assert.Equal(halfWritten, File.ReadAllText(PathOf("Work.json")));
        Assert.Null(await pc.CheckForNewerCopyAsync(CancellationToken.None)); // not "newer" either: asked again later
    }

    [Fact]
    public async Task Each_save_has_a_revision_that_the_listing_and_this_PC_remember()
    {
        var pc = Device("PC-1");
        await pc.SaveProfileAsync("Work", Layout("Editor"), CancellationToken.None);
        var first = pc.Settings.LastRevision;
        await pc.SaveProfileAsync("Work", Layout("Editor", "Mail"), CancellationToken.None);

        var listed = Assert.Single(await pc.ListProfilesAsync(CancellationToken.None));

        Assert.NotNull(first);
        Assert.NotEqual(first, pc.Settings.LastRevision);
        Assert.Equal(pc.Settings.LastRevision, listed.Revision);
        Assert.False(listed.Unreadable);
        Assert.Equal(pc.Settings.LastRevision, SyncProfile.FromJson(File.ReadAllText(PathOf("Work.json"))).Revision);

        var store = new SyncSettingsStore(Path.Combine(_folder, "sync.json"));
        store.Save(pc.Settings);
        Assert.Equal(pc.Settings.LastRevision, store.Load().LastRevision);
    }

    [Fact]
    public async Task Switching_between_Google_Drive_and_a_folder_unlinks_and_signing_out_of_Google_keeps_a_folder_link()
    {
        var pc = Device("PC-1", folder: null);
        await pc.UseGoogleDriveAsync();
        pc.Settings.LinkedProfile = "On Drive";
        pc.Settings.LinkedFileId = "drive-id";
        Assert.False(pc.UsesFolder);
        Assert.False(pc.IsConnected); // Google Drive without a sign-in

        var folder = Path.Combine(_folder, "Sync");
        await pc.UseFolderAsync(folder);

        Assert.True(Directory.Exists(folder));
        Assert.Equal(Path.GetFullPath(folder), pc.Settings.SyncFolder);
        Assert.Null(pc.Settings.LinkedProfile);
        Assert.Null(pc.Settings.LinkedFileId);

        await pc.SaveProfileAsync("Work", Layout("A"), CancellationToken.None);
        await pc.DisconnectAsync(CancellationToken.None);
        Assert.Equal("Work", pc.Settings.LinkedProfile);

        await pc.UseGoogleDriveAsync();
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
    [InlineData("Home_ desk (2).json", "Home? desk", "Home? desk")] // the number a name made safe gets when it clashes
    [InlineData("Home_ desk (1).json", "Home? desk", "Home_ desk (1)")] // that numbering starts at 2
    [InlineData("Home_ desk (x).json", "Home? desk", "Home_ desk (x)")]
    [InlineData("Work (2).json", "Work", "Work (2)")] // a name that needs no change never gets one: a sync program's copy
    [InlineData("Work.json", null, "Work")]
    [InlineData("work.json", "WORK", "WORK")]
    public void A_copy_shows_its_own_name_when_the_file_is_named_after_it(string fileName, string? inside, string shown)
    {
        Assert.Equal(shown, FolderSyncClient.DisplayName(fileName, inside));
    }

    [Fact]
    public async Task A_copy_says_which_file_it_was_written_to()
    {
        // Review 16 U3′, the root fix: the file name is in the copy, right after "format".
        File.WriteAllText(PathOf("config.json"), "{\"theme\":\"another program's\"}");
        await Device("DESK").SaveProfileAsync("config", Layout("a"), CancellationToken.None);

        var text = File.ReadAllText(PathOf("config (2).json"));
        using var document = System.Text.Json.JsonDocument.Parse(text);
        Assert.Equal(["format", "file"], document.RootElement.EnumerateObject().Take(2).Select(p => p.Name).ToArray()); // "format" stays first
        Assert.Equal("config (2).json", SyncProfile.FromJson(text).File);
    }

    [Fact]
    public async Task The_file_a_copy_was_written_to_is_its_original_whatever_the_numbers_say()
    {
        // By numbers alone the lowest-numbered file was taken for the original: wrong when a sync program's copy got
        // the lower number.
        File.WriteAllText(PathOf("config.json"), "{\"theme\":\"another program's\"}");
        File.WriteAllText(PathOf("config (2).json"), "{\"theme\":\"its backup\"}");
        await Device("DESK").SaveProfileAsync("config", Layout("a"), CancellationToken.None);
        Assert.True(File.Exists(PathOf("config (3).json")));
        File.Delete(PathOf("config (2).json"));
        File.Copy(PathOf("config (3).json"), PathOf("config (2).json")); // a sync program's copy, numbered lower

        var copies = await Device("LAPTOP").ListProfilesAsync(CancellationToken.None);

        var original = copies.Single(c => c.FileId == "config (3).json");
        Assert.Equal("config", original.Name);
        Assert.False(original.IsConflictCopy);
        var copy = copies.Single(c => c.FileId == "config (2).json");
        Assert.Equal("config (2)", copy.Name);
        Assert.True(copy.IsConflictCopy);
    }

    [Fact]
    public async Task A_copy_of_a_file_that_is_gone_is_still_a_copy()
    {
        var desk = Device("DESK");
        await desk.SaveProfileAsync("Work", Layout("a"), CancellationToken.None);
        File.Move(PathOf("Work.json"), PathOf("Work (2).json")); // Keep both, then the original deleted

        var listed = Assert.Single(await Device("LAPTOP").ListProfilesAsync(CancellationToken.None));

        Assert.Equal("Work (2)", listed.Name); // it says Work.json inside
    }

    [Fact]
    public async Task A_copy_saved_by_an_older_version_is_judged_by_the_file_names()
    {
        // No "file" in it: the rule of 0.8.1 to 0.10.0 still applies.
        File.WriteAllText(PathOf("config.json"), "{\"theme\":\"another program's\"}");
        File.WriteAllText(PathOf("config (2).json"), "{\"format\":1,\"name\":\"config\",\"savedBy\":\"OLD\",\"layout\":{\"items\":[]}}");

        var listed = Assert.Single(await Device("LAPTOP").ListProfilesAsync(CancellationToken.None));

        Assert.Equal("config", listed.Name);
        Assert.False(listed.IsConflictCopy);
    }

    [Fact]
    public void The_file_name_replaces_an_older_one_and_keeps_what_a_newer_version_wrote()
    {
        var json = FolderSyncClient.WithFileName("{\"format\":1,\"name\":\"Work\",\"file\":\"Old.json\",\"future\":{\"x\":[1,2]},\"layout\":{}}", "Work.json");

        using var document = System.Text.Json.JsonDocument.Parse(json);
        Assert.Equal(["format", "file", "name", "future", "layout"], document.RootElement.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal("Work.json", document.RootElement.GetProperty("file").GetString());
        Assert.Equal(2, document.RootElement.GetProperty("future").GetProperty("x")[1].GetInt32());
        Assert.Equal("not json", FolderSyncClient.WithFileName("not json", "Work.json"));
    }
}
