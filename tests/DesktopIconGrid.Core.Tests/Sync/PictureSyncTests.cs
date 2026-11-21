using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Core.Sync;

namespace DesktopIconGrid.Core.Tests.Sync;

/// <summary>0.15.0 WP5b: the background picture file travels with sync, and a received one is kept only after every check.</summary>
public sealed class PictureSyncTests : IDisposable
{
    private readonly string _sync = Directory.CreateTempSubdirectory("dig-picture-sync-").FullName;
    private readonly string _here = Directory.CreateTempSubdirectory("dig-pictures-here-").FullName;
    private readonly string _there = Directory.CreateTempSubdirectory("dig-pictures-there-").FullName;

    public void Dispose()
    {
        foreach (var folder in new[] { _sync, _here, _there })
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>Bytes standing in for a stored picture, and the name the program would give them.</summary>
    private static (byte[] Bytes, string Name) Picture(byte seed, bool png = false)
    {
        var bytes = Enumerable.Range(0, 2048).Select(i => (byte)(i * 31 + seed)).ToArray();
        return (bytes, BackgroundImage.NameFor(bytes, png));
    }

    private static bool Decodes(byte[] bytes, bool png) => true;

    /// <summary>A sync place that lists what it is told and counts what is asked of it.</summary>
    private sealed class FakePlace : ISyncPictures
    {
        public List<SyncPicture> Listed { get; } = [];

        public Dictionary<string, byte[]?> Contents { get; } = [];

        public List<string> Uploaded { get; } = [];

        public List<string> Deleted { get; } = [];

        public Task<IReadOnlyList<SyncPicture>> ListPicturesAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<SyncPicture>>(Listed.ToList());

        public Task UploadPictureAsync(string remoteName, byte[] content, CancellationToken cancellationToken)
        {
            Uploaded.Add(remoteName);
            Listed.Add(new SyncPicture(remoteName, remoteName, content.Length));
            Contents[remoteName] = content;
            return Task.CompletedTask;
        }

        public Task<byte[]?> DownloadPictureAsync(SyncPicture picture, long maxBytes, CancellationToken cancellationToken) =>
            Task.FromResult(Contents.TryGetValue(picture.Id, out var bytes) && bytes is not null && bytes.Length <= maxBytes ? bytes : null);

        public Task DeletePictureAsync(SyncPicture picture, CancellationToken cancellationToken)
        {
            Deleted.Add(picture.Name);
            Listed.RemoveAll(entry => entry.Id == picture.Id);
            return Task.CompletedTask;
        }
    }

    private void NothingKeptThere() => Assert.Empty(Directory.EnumerateFiles(_there));

    [Fact]
    public async Task A_picture_goes_up_once_beside_the_copies_under_its_remote_name()
    {
        var place = new FolderSyncClient(_sync);
        var (bytes, name) = Picture(1);
        File.WriteAllBytes(Path.Combine(_here, name), bytes);

        Assert.True(await PictureSync.UploadIfMissingAsync(place, _here, name, CancellationToken.None));
        Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(_sync, "background-" + name)));
        Assert.False(await PictureSync.UploadIfMissingAsync(place, _here, name, CancellationToken.None)); // never twice

        Assert.False(await PictureSync.UploadIfMissingAsync(place, _here, Picture(2).Name, CancellationToken.None)); // this PC lacks it
        Assert.False(await PictureSync.UploadIfMissingAsync(place, _here, "..\\layout.json", CancellationToken.None)); // no stored name
        Assert.Equal(["background-" + name], (await place.ListPicturesAsync(CancellationToken.None)).Select(p => p.Name));
        Assert.Empty(await place.ListFilesAsync(CancellationToken.None)); // no profile copy is listed for it
    }

    [Fact]
    public async Task A_picture_this_PC_lacks_comes_down_through_a_temporary_name_and_one_not_there_is_said_so()
    {
        var place = new FolderSyncClient(_sync);
        var (bytes, name) = Picture(3, png: true);
        File.WriteAllBytes(Path.Combine(_sync, "background-" + name), bytes);

        Assert.Equal(PictureFetch.Fetched, await PictureSync.FetchAsync(place, _there, name, Decodes, CancellationToken.None));
        Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(_there, name)));
        Assert.Equal([name], Directory.EnumerateFiles(_there).Select(Path.GetFileName));
        Assert.Equal(PictureFetch.Fetched, await PictureSync.FetchAsync(place, _there, name, Decodes, CancellationToken.None)); // already here

        Assert.Equal(PictureFetch.NotThere, await PictureSync.FetchAsync(place, _there, Picture(4).Name, Decodes, CancellationToken.None));
    }

    [Fact]
    public async Task Each_refusal_keeps_nothing()
    {
        var (bytes, name) = Picture(5);

        // Too large by the listing: not even asked for.
        var listedLarge = new FakePlace();
        listedLarge.Listed.Add(new SyncPicture("big", "background-" + name, PictureSync.MaxBytes + 1));
        Assert.Equal(PictureFetch.TooLarge, await PictureSync.FetchAsync(listedLarge, _there, name, Decodes, CancellationToken.None));
        NothingKeptThere();

        // Too large while reading: the listing said nothing, the download stopped at the limit.
        var readLarge = new FakePlace();
        readLarge.Listed.Add(new SyncPicture("big", "background-" + name, null));
        readLarge.Contents["big"] = null;
        Assert.Equal(PictureFetch.TooLarge, await PictureSync.FetchAsync(readLarge, _there, name, Decodes, CancellationToken.None));
        NothingKeptThere();

        // Bytes that do not give the name the layout asks for: not the file a PC of this bar stored.
        var other = new FakePlace();
        other.Listed.Add(new SyncPicture("x", "background-" + name, bytes.Length));
        other.Contents["x"] = Picture(6).Bytes;
        Assert.Equal(PictureFetch.WrongHash, await PictureSync.FetchAsync(other, _there, name, Decodes, CancellationToken.None));
        NothingKeptThere();

        // The right bytes, but Windows does not decode them as the picture their name says.
        var right = new FakePlace();
        right.Listed.Add(new SyncPicture("y", "background-" + name, bytes.Length));
        right.Contents["y"] = bytes;
        Assert.Equal(PictureFetch.NotAPicture, await PictureSync.FetchAsync(right, _there, name, (_, _) => false, CancellationToken.None));
        NothingKeptThere();

        // A real file over the limit in a sync folder, by its length (made by its length alone).
        using (var stream = File.Create(Path.Combine(_sync, "background-" + name)))
        {
            stream.SetLength(PictureSync.MaxBytes + 1);
        }

        Assert.Equal(PictureFetch.TooLarge, await PictureSync.FetchAsync(new FolderSyncClient(_sync), _there, name, Decodes, CancellationToken.None));
        NothingKeptThere();
    }

    [Fact]
    public async Task The_png_flag_the_check_gets_is_the_names_and_the_name_must_match_its_type()
    {
        var (bytes, jpegName) = Picture(7);
        var pngName = BackgroundImage.NameFor(bytes, png: true);
        var place = new FakePlace();
        place.Listed.Add(new SyncPicture("p", "background-" + pngName, bytes.Length));
        place.Contents["p"] = bytes;
        var asked = new List<bool>();

        Assert.Equal(PictureFetch.Fetched, await PictureSync.FetchAsync(place, _there, pngName, (_, png) => { asked.Add(png); return true; }, CancellationToken.None));
        Assert.Equal([true], asked); // decoded as the PNG its name says

        // The same bytes asked for as .jpg: the name says JPEG and the place has only the .png one.
        Assert.Equal(PictureFetch.NotThere, await PictureSync.FetchAsync(place, _there, jpegName, Decodes, CancellationToken.None));
    }

    [Fact]
    public async Task The_old_picture_is_deleted_only_when_no_copy_names_it_and_stays_when_unsure()
    {
        var place = new FakePlace();
        var (_, old) = Picture(8);
        place.Listed.Add(new SyncPicture("old", "background-" + old, 10));

        Assert.False(await PictureSync.DeleteIfUnnamedAsync(place, old, [old], CancellationToken.None)); // a copy still names it
        Assert.False(await PictureSync.DeleteIfUnnamedAsync(place, old, null, CancellationToken.None)); // unsure: it stays
        Assert.Empty(place.Deleted);

        Assert.True(await PictureSync.DeleteIfUnnamedAsync(place, old, [Picture(9).Name], CancellationToken.None));
        Assert.Equal(["background-" + old], place.Deleted);
    }

    [Fact]
    public void The_picture_a_copy_names_is_found_wherever_the_copy_has_it()
    {
        var (_, name) = Picture(10);
        Assert.Equal(name, PictureSync.NamedIn("""{"format":1,"layout":{"settings":{"theme":"custom","backgroundImage":{"file":""" + "\"" + name + "\"" + ""","zoom":150}}}}"""));
        Assert.Null(PictureSync.NamedIn("""{"layout":{"settings":{"theme":"dark"}}}"""));
        Assert.Null(PictureSync.NamedIn("""{"layout":{"settings":{"backgroundImage":{"file":"..\\x.jpg"}}}}""")); // no stored name
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => PictureSync.NamedIn("not json"));
        Assert.Equal(name, PictureSync.StoredNameOf("background-" + name));
        Assert.Null(PictureSync.StoredNameOf("background-" + name + ".partial"));
        Assert.Null(PictureSync.StoredNameOf(name));
    }

    /// <summary>A PC whose copies are kept in the test's sync folder.</summary>
    private static SyncService Device(string folder, string name)
    {
        var auth = new GoogleAuthorization(new HttpClient(new FakeHttpHandler()), new InMemoryTokenStore(), () => new OAuthClientCredentials("id", "secret"));
        return new SyncService(auth, new NoDrive(), new SyncSettings { DeviceName = name, SyncFolder = folder }, () => { }, "0.15.0");
    }

    private static LauncherLayout WithPicture(string? name) => new()
    {
        Settings = new LauncherSettings { Theme = ThemeCatalog.CustomId, BackgroundImage = name is null ? null : new BackgroundImage { File = name } },
    };

    [Fact]
    public async Task After_an_upload_the_new_picture_goes_up_and_the_old_one_goes_only_when_no_copy_names_it()
    {
        // Each copy has its own PC, as in use: PC-1 keeps "Work", PC-2 keeps "Home".
        var pc1 = Device(_sync, "PC-1");
        var pc2 = Device(_sync, "PC-2");
        var (oldBytes, old) = Picture(11);
        var (newBytes, fresh) = Picture(12);
        File.WriteAllBytes(Path.Combine(_here, old), oldBytes);
        File.WriteAllBytes(Path.Combine(_here, fresh), newBytes);

        await pc1.SaveProfileAsync("Work", WithPicture(old), CancellationToken.None);
        Assert.True(await pc1.SyncPictureAfterUploadAsync(old, _here, CancellationToken.None));
        Assert.Equal(old, pc1.Settings.LastSyncedPicture);
        await pc2.SaveProfileAsync("Home", WithPicture(old), CancellationToken.None);

        // "Home" still names the old picture: replacing it on "Work" leaves it beside the copies.
        await pc1.SaveProfileAsync("Work", WithPicture(fresh), CancellationToken.None);
        Assert.True(await pc1.SyncPictureAfterUploadAsync(fresh, _here, CancellationToken.None));
        Assert.True(File.Exists(Path.Combine(_sync, "background-" + old)));
        Assert.True(File.Exists(Path.Combine(_sync, "background-" + fresh)));
        Assert.Equal(fresh, pc1.Settings.LastSyncedPicture);

        // Once no copy names it, PC-1's next upload away from it tidies it (here PC-1 last synced it, as when the tidy failed).
        await pc2.SaveProfileAsync("Home", WithPicture(fresh), CancellationToken.None);
        pc1.Settings.LastSyncedPicture = old;
        Assert.False(await pc1.SyncPictureAfterUploadAsync(fresh, _here, CancellationToken.None)); // up already, not twice
        Assert.False(File.Exists(Path.Combine(_sync, "background-" + old)));
        Assert.True(File.Exists(Path.Combine(_sync, "background-" + fresh)));

        // A copy that cannot be read (one being written: it starts as a copy and stops) may name it: unsure, so it stays.
        File.WriteAllText(Path.Combine(_sync, "Broken.json"), "{\"format\":1,\"name\":\"Broken\",\"layout\":{");
        File.WriteAllBytes(Path.Combine(_sync, "background-" + old), oldBytes);
        pc1.Settings.LastSyncedPicture = old;
        await pc1.SyncPictureAfterUploadAsync(fresh, _here, CancellationToken.None);
        Assert.True(File.Exists(Path.Combine(_sync, "background-" + old)));
    }

    [Fact]
    public async Task A_picture_comes_down_through_the_service_and_its_failure_leaves_the_copies_alone()
    {
        var pc = Device(_sync, "PC-1");
        var other = Device(_sync, "PC-2");
        var (bytes, name) = Picture(13);
        File.WriteAllBytes(Path.Combine(_here, name), bytes);
        await pc.SaveProfileAsync("Work", WithPicture(name), CancellationToken.None);
        await pc.SyncPictureAfterUploadAsync(name, _here, CancellationToken.None);

        Assert.Equal(PictureFetch.Fetched, await other.FetchPictureAsync(name, _there, Decodes, CancellationToken.None));
        Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(_there, name)));

        // The file vanishes from the sync place: the next fetch says so, and the copies load as before.
        File.Delete(Path.Combine(_sync, "background-" + name));
        File.Delete(Path.Combine(_there, name));
        Assert.Equal(PictureFetch.NotThere, await other.FetchPictureAsync(name, _there, Decodes, CancellationToken.None));
        var work = Assert.Single(await other.ListProfilesAsync(CancellationToken.None));
        Assert.Equal(name, (await other.LoadProfileAsync(work, CancellationToken.None)).Layout.Settings.BackgroundImage!.File);
    }

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
}
