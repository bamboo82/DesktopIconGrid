using System.IO;
using System.Net;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Rectangle = System.Windows.Shapes.Rectangle;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Services;
using DesktopIconGrid.ViewModels;
using DesktopIconGrid.Views;

namespace DesktopIconGrid.App.Tests;

/// <summary>0.15.0 WP5: the custom theme's background picture. In the WPF collection: WPF imaging wants its thread.</summary>
[Collection(WpfWindowsCollection.Name)]
public sealed class BackgroundPictureTests(WpfThread wpf) : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("dig-backgrounds-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    /// <summary>A picture of <paramref name="width"/> × <paramref name="height"/>: its left half one colour, its right half another.</summary>
    private static BitmapSource Halves(int width, int height, Color left, Color right)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var colour = x < width / 2 ? left : right;
                var i = ((y * width) + x) * 4;
                (pixels[i], pixels[i + 1], pixels[i + 2], pixels[i + 3]) = (colour.B, colour.G, colour.R, colour.A);
            }
        }

        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        bitmap.Freeze();
        return bitmap;
    }

    private static Color PixelAt(BitmapSource bitmap, int x, int y)
    {
        var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        var pixel = new byte[4];
        converted.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
        return Color.FromArgb(pixel[3], pixel[2], pixel[1], pixel[0]);
    }

    private static byte[] Png(BitmapSource bitmap)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var memory = new MemoryStream();
        encoder.Save(memory);
        return memory.ToArray();
    }

    [Fact]
    public void A_picture_is_written_anew_as_jpeg_or_with_transparency_as_png_once_by_its_content()
    {
        wpf.Invoke(() =>
        {
            var opaque = Halves(100, 50, Colors.Red, Colors.Red);
            var (name, average) = BackgroundPictures.Store(opaque, _directory);
            Assert.True(BackgroundImage.IsStoredName(name));
            Assert.EndsWith(".jpg", name, StringComparison.Ordinal);
            Assert.Equal("#FF0000", average);
            Assert.True(File.Exists(Path.Combine(_directory, name)));

            Assert.Equal(name, BackgroundPictures.Store(opaque, _directory).Name); // the same picture: the same file
            Assert.Single(Directory.GetFiles(_directory));

            var seeThrough = Halves(100, 50, Colors.Transparent, Colors.Blue);
            var (png, blueAverage) = BackgroundPictures.Store(seeThrough, _directory);
            Assert.EndsWith(".png", png, StringComparison.Ordinal);
            Assert.Equal("#0000FF", blueAverage); // the transparent half does not count
        });
    }

    [Fact]
    public void What_is_not_a_picture_and_an_icon_are_refused_and_a_huge_picture_is_reduced()
    {
        wpf.Invoke(() =>
        {
            Assert.Throws<InvalidOperationException>(() => BackgroundPictures.Decode([1, 2, 3, 4]));

            // An icon: a header, one entry and a PNG inside.
            var png = Png(Halves(16, 16, Colors.Green, Colors.Green));
            var icon = new byte[6 + 16 + png.Length];
            BitConverter.GetBytes((ushort)1).CopyTo(icon, 2); // type 1: an icon
            BitConverter.GetBytes((ushort)1).CopyTo(icon, 4); // one image
            icon[6] = 16;
            icon[7] = 16;
            BitConverter.GetBytes((ushort)1).CopyTo(icon, 10);
            BitConverter.GetBytes((ushort)32).CopyTo(icon, 12);
            BitConverter.GetBytes(png.Length).CopyTo(icon, 14);
            BitConverter.GetBytes(22).CopyTo(icon, 18);
            png.CopyTo(icon, 22);
            Assert.Throws<InvalidOperationException>(() => BackgroundPictures.Decode(icon));

            var huge = BackgroundPictures.Decode(Png(Halves(5000, 20, Colors.Red, Colors.Blue)));
            Assert.Equal(BackgroundPictures.MaxSide, huge.PixelWidth);
        });
    }

    [Fact]
    public void The_bar_gets_the_part_its_focus_asks_for_scaled_to_its_pixels()
    {
        wpf.Invoke(() =>
        {
            var picture = Halves(200, 100, Colors.Red, Colors.Blue);
            var left = BackgroundPictures.Scaled(picture, 60, 60, new BackgroundImage { File = "x", FocusX = 0 });
            var right = BackgroundPictures.Scaled(picture, 60, 60, new BackgroundImage { File = "x", FocusX = 1 });

            Assert.Equal((60, 60), (left.PixelWidth, left.PixelHeight));
            Assert.Equal(Colors.Red, PixelAt(left, 30, 30));
            Assert.Equal(Colors.Blue, PixelAt(right, 30, 30));
        });
    }

    [Fact]
    public void Unused_pictures_are_deleted_at_start_and_nothing_else_in_the_folder()
    {
        var used = "0123456789abcdef0123456789abcdef.jpg";
        var unused = "fedcba9876543210fedcba9876543210.png";
        foreach (var name in new[] { used, unused, "desktop.ini" })
        {
            File.WriteAllText(Path.Combine(_directory, name), "x");
        }

        BackgroundPictures.Prune(_directory, used);

        Assert.Equal(["0123456789abcdef0123456789abcdef.jpg", "desktop.ini"], Directory.GetFiles(_directory).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void A_picture_this_PC_lacks_is_none_and_one_it_has_is_read_once()
    {
        wpf.Invoke(() =>
        {
            Assert.Null(BackgroundPictures.Load(_directory, new BackgroundImage { File = "0123456789abcdef0123456789abcdef.jpg" }));
            Assert.Null(BackgroundPictures.Load(_directory, new BackgroundImage { File = @"..\elsewhere.jpg" }));

            var (name, _) = BackgroundPictures.Store(Halves(40, 20, Colors.Red, Colors.Blue), _directory);
            var image = new BackgroundImage { File = name };
            var first = BackgroundPictures.Load(_directory, image);
            Assert.NotNull(first);
            Assert.Same(first, BackgroundPictures.Load(_directory, image)); // kept while it is the one in use
        });
    }

    /// <summary>Answers every request as told, and remembers that one came.</summary>
    private sealed class Server(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            var response = answer(request);
            response.RequestMessage ??= request;
            return Task.FromResult(response);
        }
    }

    private static async Task<DownloadFailure> FailureOf(Server server, string address)
    {
        using var http = new HttpClient(server);
        var failed = await Assert.ThrowsAsync<DownloadFailedException>(() => IconDownloader.FetchAsync(http, new Uri(address), httpsOnly: true, CancellationToken.None));
        return failed.Failure;
    }

    [Fact]
    public async Task The_web_picture_takes_the_download_rules_https_only_also_after_redirects()
    {
        var never = new Server(_ => new HttpResponseMessage(HttpStatusCode.OK));
        Assert.Equal(DownloadFailure.NotHttps, await FailureOf(never, "http://example.test/a.jpg"));
        Assert.Equal(0, never.Requests); // nothing fetched

        var downgraded = new Server(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([1]),
            RequestMessage = new HttpRequestMessage(HttpMethod.Get, "http://example.test/a.jpg"), // where the redirects ended
        });
        Assert.Equal(DownloadFailure.NotHttps, await FailureOf(downgraded, "https://example.test/a.jpg"));

        var large = new Server(_ =>
        {
            var content = new ByteArrayContent([1]);
            content.Headers.ContentLength = IconDownloader.MaxBytes + 1;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        });
        Assert.Equal(DownloadFailure.TooLarge, await FailureOf(large, "https://example.test/a.jpg"));

        var page = new Server(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html></html>", System.Text.Encoding.UTF8, "text/html") });
        Assert.Equal(DownloadFailure.NotAPicture, await FailureOf(page, "https://example.test/a.jpg"));

        var missing = new Server(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        Assert.Equal(DownloadFailure.NoAnswer, await FailureOf(missing, "https://example.test/a.jpg"));

        Assert.Equal(string.Format(DesktopIconGrid.Resources.Strings.Picture_TooLarge, 10), PictureAddressWindow.Reason(DownloadFailure.TooLarge));
        Assert.Equal(DesktopIconGrid.Resources.Strings.Picture_NotHttps, PictureAddressWindow.Reason(DownloadFailure.NotHttps));
    }

    [Fact]
    public void A_picked_file_over_50_MB_is_refused_by_its_length_before_it_is_read()
    {
        // Review 41: the limit for a file from this PC, with the "too large" text the download has (10 MB there).
        var large = Path.Combine(_directory, "large.jpg");
        using (var stream = File.Create(large))
        {
            stream.SetLength(BackgroundPictures.MaxFileBytes + 1); // never written: only its length is looked at
        }

        var refused = Assert.Throws<PictureTooLargeException>(() => BackgroundPictures.ReadFile(large));
        Assert.Equal(50, refused.LimitMegabytes);
        var english = DesktopIconGrid.Resources.Strings.ResourceManager.GetString(nameof(DesktopIconGrid.Resources.Strings.Picture_TooLarge), System.Globalization.CultureInfo.InvariantCulture)!;
        Assert.Equal("The picture is larger than 50 MB.", string.Format(System.Globalization.CultureInfo.InvariantCulture, english, refused.LimitMegabytes));
        Assert.IsAssignableFrom<InvalidOperationException>(refused); // the window's other refusals still catch it last

        var small = Path.Combine(_directory, "small.bin");
        File.WriteAllBytes(small, [1, 2, 3]);
        Assert.Equal([1, 2, 3], BackgroundPictures.ReadFile(small));
        Assert.Equal(50L * 1024 * 1024, BackgroundPictures.MaxFileBytes);
    }

    [Fact]
    public void Settings_takes_a_stored_picture_with_its_defaults_and_saves_or_removes_it()
    {
        var settings = new SettingsViewModel(new LauncherSettings { Theme = ThemeCatalog.CustomId }) { PicturesFolder = _directory };
        Assert.False(settings.HasPicture);

        settings.UsePicture("0123456789abcdef0123456789abcdef.jpg", "#101010");
        Assert.True(settings.HasPicture);
        Assert.Equal(100, settings.PictureStrength);
        settings.PictureStrength = 10; // held to 20
        Assert.Equal(20, settings.PictureStrength);
        Assert.Equal(new BackgroundImage { File = "0123456789abcdef0123456789abcdef.jpg", Average = "#101010", Strength = 20 }, settings.ToSettings().BackgroundImage);

        settings.RemovePicture();
        Assert.Null(settings.ToSettings().BackgroundImage);
    }

    [Fact]
    public void With_a_picture_this_PC_has_the_focus_warning_judges_the_picture_blended_into_the_colour()
    {
        var name = "0123456789abcdef0123456789abcdef.jpg";
        var settings = new SettingsViewModel(new LauncherSettings { Theme = ThemeCatalog.CustomId, BackgroundColor = "#F3F3F3" }) { PicturesFolder = _directory };
        settings.FocusAutomatic = false;
        settings.FocusColorText = "#202020"; // plain on the light colour
        Assert.False(settings.IsFocusHardToSee);

        settings.UsePicture(name, "#101010"); // a dark picture, not on this PC yet: still the colour alone
        Assert.False(settings.IsFocusHardToSee);

        File.WriteAllText(Path.Combine(_directory, name), "x");
        settings.PictureStrength = 100; // a change, so the check runs again
        settings.PictureStrength = 95;
        Assert.True(settings.IsFocusHardToSee); // near-black on a near-black picture
    }

    [Fact]
    public void The_bar_shows_a_picture_only_with_the_custom_theme_on_a_PC_that_has_it()
    {
        var model = new DesktopIconGrid.Core.LauncherModel();
        model.Settings.Theme = ThemeCatalog.CustomId;
        model.Settings.BackgroundColor = "#F3F3F3";
        model.Settings.BackgroundImage = new BackgroundImage { File = "0123456789abcdef0123456789abcdef.jpg", Average = "#101010" };
        using var bar = new TestBar(_directory, model);
        var viewModel = bar.ViewModel;

        viewModel.PictureAvailable = _ => true;
        Assert.Equal(model.Settings.BackgroundImage, viewModel.ShownPicture);
        Assert.Equal("#FFFFFF", viewModel.Palette.Foreground); // the text chosen for the dark picture

        viewModel.PictureAvailable = _ => false; // another PC: the colour alone
        Assert.Null(viewModel.ShownPicture);
        Assert.Equal("#202020", viewModel.Palette.Foreground);

        viewModel.PictureAvailable = _ => true;
        model.Settings.Theme = "graphite";
        Assert.Null(viewModel.ShownPicture);
    }

    [Fact]
    public void The_layer_covers_its_place_at_the_strength_times_the_transparency_and_text_takes_its_edge_at_every_opacity()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        wpf.Invoke(() =>
        {
            var layer = new Rectangle();
            var cover = new Grid { Width = 120, Height = 40 };
            cover.Children.Add(layer);
            var window = new Window { Content = cover, SizeToContent = SizeToContent.WidthAndHeight, WindowStyle = WindowStyle.None };
            try
            {
                window.Show();
                window.UpdateLayout();
                var picture = new BarPicture(cover, layer);
                var scale = VisualTreeHelper.GetDpi(cover);

                picture.Show(Halves(300, 100, Colors.Red, Colors.Blue), new BackgroundImage { File = "x", Strength = 60 }, opacity: 0.5);
                Assert.Equal(Visibility.Visible, layer.Visibility);
                Assert.Equal(0.3, layer.Opacity, 6);
                Assert.Equal(((int)Math.Ceiling(120 * scale.DpiScaleX), (int)Math.Ceiling(40 * scale.DpiScaleY)), (picture.Drawn!.PixelWidth, picture.Drawn.PixelHeight));

                picture.Show(null, new BackgroundImage { File = "x" }, 1); // this PC lacks the file
                Assert.Equal(Visibility.Collapsed, layer.Visibility);
                Assert.Null(layer.Fill);

                var source = HwndSource.FromHwnd(new WindowInteropHelper(window).Handle)!;
                var surface = new BarSurface(window, 0, "test") { HighContrast = () => false };
                surface.Apply(source, LauncherSettings.BackdropSolid, ThemeCatalog.Classic, LauncherSettings.MaxOpacity, 32, picture: true);
                Assert.False(surface.SeeThrough);
                Assert.True(window.Resources.Contains(MainWindow.TextEdgeKey), "no text edge over a picture at 100 %");
                surface.Apply(source, LauncherSettings.BackdropSolid, ThemeCatalog.Classic, LauncherSettings.MaxOpacity, 32, picture: false);
                Assert.False(window.Resources.Contains(MainWindow.TextEdgeKey));
            }
            finally
            {
                window.Close();
            }
        });
    }
}
