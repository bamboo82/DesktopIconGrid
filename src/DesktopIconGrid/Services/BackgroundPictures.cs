using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopIconGrid.Core.Diagnostics;
using DesktopIconGrid.Core.Models;

namespace DesktopIconGrid.Services;

/// <summary>
/// The custom theme's background pictures (0.15.0 WP5). A picture from a file or from the web is decoded by Windows and
/// written anew into <c>backgrounds</c>, never the bytes that came in: at most <see cref="MaxSide"/> pixels on its longer
/// side, as JPEG (quality <see cref="JpegQuality"/>) or, with transparency, PNG, named by its content. For drawing, the
/// picture in use is decoded once and kept; the bar gets it scaled to its size (<see cref="Scaled"/>), not per frame.
/// Must be used on the UI thread (WPF imaging).
/// </summary>
public static class BackgroundPictures
{
    public const int MaxSide = 3840;
    public const int JpegQuality = 90;

    private static (string Path, BitmapSource Picture)? _loaded;

    /// <summary>
    /// Decodes picture bytes for storing: PNG, JPEG, BMP, GIF (its first frame), TIFF or WebP where Windows can, a very large
    /// one at <see cref="MaxSide"/> on its longer side. An icon is refused, and so is what Windows cannot decode:
    /// <see cref="InvalidOperationException"/>.
    /// </summary>
    public static BitmapSource Decode(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        try
        {
            using var probe = new MemoryStream(bytes);
            var header = BitmapDecoder.Create(probe, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            if (header is IconBitmapDecoder)
            {
                throw new InvalidOperationException("An icon is no background picture.");
            }

            var first = header.Frames[0];
            if (Math.Max(first.PixelWidth, first.PixelHeight) > MaxSide)
            {
                var reduced = new BitmapImage();
                reduced.BeginInit();
                reduced.StreamSource = new MemoryStream(bytes);
                reduced.CacheOption = BitmapCacheOption.OnLoad;
                reduced.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                reduced.DecodePixelWidth = first.PixelWidth >= first.PixelHeight ? MaxSide : 0;
                reduced.DecodePixelHeight = first.PixelWidth >= first.PixelHeight ? 0 : MaxSide;
                reduced.EndInit();
                reduced.Freeze();
                return reduced;
            }

            using var input = new MemoryStream(bytes);
            var frame = BitmapDecoder.Create(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
            frame.Freeze();
            return frame;
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or ArgumentException or IOException or OverflowException or System.Runtime.InteropServices.COMException)
        {
            throw new InvalidOperationException("Not a picture Windows can decode.", ex);
        }
    }

    /// <summary>
    /// Writes <paramref name="picture"/> anew into <paramref name="folder"/> and returns its stored name and average
    /// colour. The same picture is written once: its name is its content's.
    /// </summary>
    public static (string Name, string Average) Store(BitmapSource picture, string folder)
    {
        ArgumentNullException.ThrowIfNull(picture);
        var fitted = Fit(picture, MaxSide);
        var pixels = new FormatConvertedBitmap(fitted, PixelFormats.Bgra32, null, 0);
        var transparent = HasTransparency(pixels);
        BitmapEncoder encoder = transparent ? new PngBitmapEncoder() : new JpegBitmapEncoder { QualityLevel = JpegQuality };
        encoder.Frames.Add(BitmapFrame.Create(transparent ? pixels : new FormatConvertedBitmap(fitted, PixelFormats.Bgr24, null, 0)));
        using var memory = new MemoryStream();
        encoder.Save(memory);
        var bytes = memory.ToArray();

        var name = BackgroundImage.NameFor(bytes, png: transparent);
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, name);
        if (!File.Exists(path))
        {
            var temporary = path + ".tmp";
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, path, overwrite: true);
        }

        AppLog.Default.Info("Theme", $"Background picture stored as {name} ({fitted.PixelWidth}×{fitted.PixelHeight}, {(transparent ? "PNG" : "JPEG")})");
        return (name, Average(fitted));
    }

    /// <summary>The average colour of <paramref name="picture"/>, <c>#RRGGBB</c>, its transparent parts counting less.</summary>
    public static string Average(BitmapSource picture)
    {
        ArgumentNullException.ThrowIfNull(picture);
        var small = new FormatConvertedBitmap(Fit(picture, 32), PixelFormats.Bgra32, null, 0);
        var stride = small.PixelWidth * 4;
        var bytes = new byte[stride * small.PixelHeight];
        small.CopyPixels(bytes, stride, 0);
        double r = 0, g = 0, b = 0, weight = 0;
        for (var i = 0; i < bytes.Length; i += 4)
        {
            var alpha = bytes[i + 3] / 255.0;
            b += bytes[i] * alpha;
            g += bytes[i + 1] * alpha;
            r += bytes[i + 2] * alpha;
            weight += alpha;
        }

        if (weight <= 0)
        {
            return "#808080";
        }

        return ColourMath.ToHex(new Rgb((byte)Math.Round(r / weight), (byte)Math.Round(g / weight), (byte)Math.Round(b / weight)));
    }

    /// <summary>
    /// The stored picture of <paramref name="image"/>, decoded once and kept while it is the one in use; none when there is
    /// none, its name is no stored name, or this PC does not have the file (the colour alone then, and no error).
    /// </summary>
    public static BitmapSource? Load(string folder, BackgroundImage? image)
    {
        if (image is null || !BackgroundImage.IsStoredName(image.File))
        {
            return null;
        }

        var path = Path.Combine(folder, image.File);
        if (_loaded is { } loaded && string.Equals(loaded.Path, path, StringComparison.OrdinalIgnoreCase))
        {
            return loaded.Picture;
        }

        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(path);
            var picture = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
            picture.Freeze();
            _loaded = (path, picture);
            return picture;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException or ArgumentException)
        {
            AppLog.Default.Warning("Theme", $"The background picture {image.File} could not be read: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// The part of <paramref name="picture"/> a bar of <paramref name="width"/> × <paramref name="height"/> pixels shows
    /// (<see cref="BackgroundFit"/>), scaled to exactly that with high quality: what the bar draws until its size changes.
    /// </summary>
    public static BitmapSource Scaled(BitmapSource picture, int width, int height, BackgroundImage image)
    {
        ArgumentNullException.ThrowIfNull(picture);
        ArgumentNullException.ThrowIfNull(image);
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        var (x, y, w, h) = BackgroundFit.SourceRect(picture.PixelWidth, picture.PixelHeight, width, height, image.FocusX, image.FocusY, image.Zoom);
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using (var context = visual.RenderOpen())
        {
            // The picture placed so that its part (x, y, w, h) falls on the whole target.
            var scaleX = width / w;
            var scaleY = height / h;
            context.PushTransform(new MatrixTransform(scaleX, 0, 0, scaleY, -x * scaleX, -y * scaleY));
            context.DrawImage(picture, new Rect(0, 0, picture.PixelWidth, picture.PixelHeight));
            context.Pop();
        }

        var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        target.Freeze();
        return target;
    }

    /// <summary>Deletes the stored pictures no setting names any more (at start); nothing else in the folder is touched.</summary>
    public static void Prune(string folder, string? inUse)
    {
        try
        {
            if (!Directory.Exists(folder))
            {
                return;
            }

            foreach (var name in BackgroundImage.Unused(Directory.EnumerateFiles(folder).Select(Path.GetFileName).OfType<string>(), inUse))
            {
                File.Delete(Path.Combine(folder, name));
                AppLog.Default.Info("Theme", $"Background picture {name} deleted: no setting names it any more");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Default.Warning("Theme", $"Unused background pictures could not be deleted: {ex.Message}");
        }
    }

    /// <summary><paramref name="picture"/> scaled down to at most <paramref name="side"/> on its longer side (never up).</summary>
    private static BitmapSource Fit(BitmapSource picture, int side)
    {
        var longer = Math.Max(picture.PixelWidth, picture.PixelHeight);
        if (longer <= side)
        {
            return picture;
        }

        var scale = (double)side / longer;
        var width = Math.Max(1, (int)Math.Round(picture.PixelWidth * scale));
        var height = Math.Max(1, (int)Math.Round(picture.PixelHeight * scale));
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using (var context = visual.RenderOpen())
        {
            context.DrawImage(picture, new Rect(0, 0, width, height));
        }

        var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        target.Freeze();
        return target;
    }

    private static bool HasTransparency(BitmapSource bgra)
    {
        var stride = bgra.PixelWidth * 4;
        var row = new byte[stride];
        for (var y = 0; y < bgra.PixelHeight; y++)
        {
            bgra.CopyPixels(new Int32Rect(0, y, bgra.PixelWidth, 1), row, stride, 0);
            for (var i = 3; i < row.Length; i += 4)
            {
                if (row[i] < 255)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
