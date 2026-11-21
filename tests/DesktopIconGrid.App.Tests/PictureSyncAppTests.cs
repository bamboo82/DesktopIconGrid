using System.IO;
using System.Net.Http;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Core.Sync;
using DesktopIconGrid.Resources;
using DesktopIconGrid.Services;
using DesktopIconGrid.ViewModels;

namespace DesktopIconGrid.App.Tests;

/// <summary>0.15.0 WP5b: the background picture file through sync, as the app does it.</summary>
[Collection(WpfWindowsCollection.Name)]
public sealed class PictureSyncAppTests(WpfThread wpf) : IDisposable
{
    private readonly string _sync = Directory.CreateTempSubdirectory("dig-pic-sync-").FullName;
    private readonly string _local = Directory.CreateTempSubdirectory("dig-pic-local-").FullName;

    public void Dispose()
    {
        PictureTransfers.Reset();
        Directory.Delete(_sync, recursive: true);
        Directory.Delete(_local, recursive: true);
    }

    /// <summary>A grey picture of <paramref name="width"/> × <paramref name="height"/>, encoded by Windows.</summary>
    private byte[] Encode(int width, int height, bool png)
    {
        byte[]? bytes = null;
        wpf.Invoke(() =>
        {
            var stride = width * 3;
            var pixels = new byte[stride * height];
            Array.Fill(pixels, (byte)0x80);
            var source = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr24, null, pixels, stride);
            BitmapEncoder encoder = png ? new PngBitmapEncoder() : new JpegBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            using var memory = new MemoryStream();
            encoder.Save(memory);
            bytes = memory.ToArray();
        });
        return bytes!;
    }

    private static string AName(char digit) => new string(digit, 32) + ".jpg";

    /// <summary>The bar, as far as the coordinator sees it.</summary>
    private sealed class Host : ISyncHost
    {
        public LauncherLayout Layout { get; set; } = new();

        public List<string> Arrived { get; } = [];

        public event EventHandler? LayoutChanged;

        public LauncherLayout ExportLayout() => Layout;

        public void ReplaceLayout(LauncherLayout layout) => Layout = layout;

        public void PictureArrived(string storedName) => Arrived.Add(storedName);

        public void Change() => LayoutChanged?.Invoke(this, EventArgs.Empty);
    }

    private static LauncherLayout Naming(string picture) => new()
    {
        Settings = new LauncherSettings { Theme = ThemeCatalog.CustomId, BackgroundImage = new BackgroundImage { File = picture } },
    };

    [Fact]
    public void Windows_decodes_a_received_picture_only_as_the_type_its_name_says_and_within_the_stored_size()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var png = Encode(64, 32, png: true);
        var jpeg = Encode(64, 32, png: false);
        var tooWide = Encode(BackgroundPictures.MaxSide + 1, 8, png: false);
        var widest = Encode(BackgroundPictures.MaxSide, 8, png: false);
        wpf.Invoke(() =>
        {
            Assert.True(BackgroundPictures.DecodesAs(png, png: true));
            Assert.True(BackgroundPictures.DecodesAs(jpeg, png: false));
            Assert.False(BackgroundPictures.DecodesAs(png, png: false)); // a PNG named .jpg
            Assert.False(BackgroundPictures.DecodesAs(jpeg, png: true)); // and the other way round
            Assert.False(BackgroundPictures.DecodesAs(tooWide, png: false)); // more pixels than a stored picture may have
            Assert.True(BackgroundPictures.DecodesAs(widest, png: false));
            Assert.False(BackgroundPictures.DecodesAs([0x47, 0x49, 0x46, 0x38, 0x39, 0x61], png: false)); // no picture of either type
        });
    }

    [Fact]
    public void A_picture_is_not_pruned_while_a_download_of_its_name_is_under_way()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var name = AName('a');
        var path = Path.Combine(_local, name);
        File.WriteAllBytes(path, [1, 2, 3]);
        PictureTransfers.MarkFetching(name, fetching: true);
        try
        {
            BackgroundPictures.Prune(_local, inUse: null);
            Assert.True(File.Exists(path));
        }
        finally
        {
            PictureTransfers.MarkFetching(name, fetching: false);
        }

        BackgroundPictures.Prune(_local, inUse: null);
        Assert.False(File.Exists(path)); // pruned as WP5 has it once no download is under way
    }

    [Fact]
    public void Settings_says_whether_a_missing_picture_is_coming_or_not_here()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var jpeg = Encode(64, 32, png: false);
        var name = BackgroundImage.NameFor(jpeg, png: false);
        wpf.Invoke(() =>
        {
            var settings = new SettingsViewModel(new LauncherSettings { Theme = ThemeCatalog.CustomId, BackgroundImage = new BackgroundImage { File = name } })
            {
                PicturesFolder = _local,
            };
            Assert.Equal(Strings.Settings_PictureMissingHere, settings.PictureNote);

            PictureTransfers.MarkFetching(name, fetching: true);
            settings.RefreshPicture();
            Assert.Equal(Strings.Settings_PictureDownloading, settings.PictureNote);

            PictureTransfers.MarkFetching(name, fetching: false);
            File.WriteAllBytes(Path.Combine(_local, name), jpeg); // it came
            settings.RefreshPicture();
            Assert.Equal(string.Empty, settings.PictureNote);
            Assert.NotNull(settings.PictureThumbnailBrush);

            settings.RemovePicture();
            Assert.Equal(Strings.Settings_PictureNone, settings.PictureNote);
        });
    }

    [Fact]
    public async Task A_sync_check_fetches_a_missing_picture_and_tells_the_bar_and_one_not_there_is_left_for_the_next_check()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var jpeg = Encode(64, 32, png: false);
        var name = BackgroundImage.NameFor(jpeg, png: false);
        File.WriteAllBytes(Path.Combine(_sync, "background-" + name), jpeg);
        var auth = new GoogleAuthorization(new HttpClient(), new InMemoryTokenStore(), () => new OAuthClientCredentials("id", "secret"));
        var service = new SyncService(auth, new PictureDrive(), new SyncSettings { DeviceName = "PC-2", SyncFolder = _sync }, () => { }, "0.15.0");
        var host = new Host { Layout = Naming(name) };
        var coordinator = new SyncCoordinator(service, host, (_, _) => true) { PicturesFolder = _local, Decodes = (_, _) => true };

        await coordinator.EnsurePictureAsync();

        Assert.Equal(jpeg, File.ReadAllBytes(Path.Combine(_local, name)));
        Assert.Equal([name], host.Arrived);
        Assert.False(PictureTransfers.IsFetching(name));

        var missing = AName('b');
        host.Layout = Naming(missing);
        await coordinator.EnsurePictureAsync(); // not in the sync place: the colour stays, nothing is retried by itself
        Assert.False(File.Exists(Path.Combine(_local, missing)));
        Assert.True(PictureTransfers.HasFailed(missing));
        Assert.Equal([name], host.Arrived);
    }

    [Fact]
    public async Task A_picture_that_cannot_go_up_leaves_the_layouts_upload_as_it_was_and_goes_at_the_next_try()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var jpeg = Encode(64, 32, png: false);
        var name = BackgroundImage.NameFor(jpeg, png: false);
        File.WriteAllBytes(Path.Combine(_local, name), jpeg);
        var tokens = new InMemoryTokenStore { Token = new OAuthToken("at", "rt", DateTimeOffset.UtcNow.AddHours(1), null) };
        var auth = new GoogleAuthorization(new HttpClient(), tokens, () => new OAuthClientCredentials("id", "secret"));
        var drive = new PictureDrive { FailPictureUploads = true };
        var settings = new SyncSettings { DeviceName = "PC-1", ClientId = "id", ClientSecret = "secret", LinkedProfile = "Work", AutoUpload = true };
        var service = new SyncService(auth, drive, settings, () => { }, "0.15.0");
        var host = new Host { Layout = Naming(name) };
        var coordinator = new SyncCoordinator(service, host, (_, _) => true) { PicturesFolder = _local, Decodes = (_, _) => true };

        await coordinator.UploadNowAsync();

        Assert.Single(drive.Copies); // the layout is up
        Assert.Equal(1, drive.PictureUploadAttempts);
        Assert.Empty(drive.Pictures);
        Assert.Null(settings.LastSyncedPicture); // not moved: the next upload tries again
        Assert.DoesNotContain(Strings.Sync_UploadFailed.Split('{')[0], coordinator.Status, StringComparison.Ordinal);

        drive.FailPictureUploads = false;
        await coordinator.SyncPictureAsync();
        Assert.Equal(["background-" + name], drive.Pictures.Keys);
        Assert.Equal(name, settings.LastSyncedPicture);
    }

    /// <summary>A Drive-like place for copies and pictures, whose picture uploads can be made to fail.</summary>
    private sealed class PictureDrive : IDriveClient, ISyncPictures
    {
        private int _next = 1;

        public Dictionary<string, (string Name, string Content, Dictionary<string, string> Properties)> Copies { get; } = [];

        public Dictionary<string, byte[]> Pictures { get; } = [];

        public bool FailPictureUploads { get; set; }

        public int PictureUploadAttempts { get; private set; }

        public Task<DriveUser> GetUserAsync(CancellationToken cancellationToken) => Task.FromResult(new DriveUser("me@example.com", "Me"));

        public Task<IReadOnlyList<DriveFile>> ListFilesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DriveFile>>(Copies.Select(c => new DriveFile(c.Key, c.Value.Name, null, c.Value.Content.Length, c.Value.Properties)).ToList());

        public Task<DriveFile> UploadJsonAsync(string fileName, string json, IReadOnlyDictionary<string, string> appProperties, string? existingFileId, CancellationToken cancellationToken)
        {
            var id = existingFileId ?? $"id{_next++}";
            var properties = new Dictionary<string, string>(appProperties);
            Copies[id] = (fileName, json, properties);
            return Task.FromResult(new DriveFile(id, fileName, null, json.Length, properties));
        }

        public Task<string> DownloadTextAsync(string fileId, CancellationToken cancellationToken) => Task.FromResult(Copies[fileId].Content);

        public Task DeleteAsync(string fileId, CancellationToken cancellationToken)
        {
            Copies.Remove(fileId);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<SyncPicture>> ListPicturesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SyncPicture>>(Pictures.Select(p => new SyncPicture(p.Key, p.Key, p.Value.Length)).ToList());

        public Task UploadPictureAsync(string remoteName, byte[] content, CancellationToken cancellationToken)
        {
            PictureUploadAttempts++;
            if (FailPictureUploads)
            {
                throw new DriveException("Google Drive refused the picture.");
            }

            Pictures[remoteName] = content;
            return Task.CompletedTask;
        }

        public Task<byte[]?> DownloadPictureAsync(SyncPicture picture, long maxBytes, CancellationToken cancellationToken) =>
            Task.FromResult<byte[]?>(Pictures.TryGetValue(picture.Id, out var bytes) && bytes.Length <= maxBytes ? bytes : null);

        public Task DeletePictureAsync(SyncPicture picture, CancellationToken cancellationToken)
        {
            Pictures.Remove(picture.Id);
            return Task.CompletedTask;
        }
    }
}
