using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DesktopIconGrid.Services;

/// <summary>
/// Fetches an image from the web and turns it into an icon: decoded, scaled down to at most
/// <see cref="TargetSize"/> px on its longer side, centred on a transparent square and saved as PNG
/// in the icons folder. The same URL always maps to the same file name.
/// </summary>
public static class IconDownloader
{
    public const int TargetSize = 256;
    public const long MaxBytes = 10 * 1024 * 1024;

    /// <summary>Images larger than this on a side are decoded at a reduced size.</summary>
    public const int MaxDecodedSide = 2048;

    public static bool IsImageUrl(string? text) =>
        Uri.TryCreate(text?.Trim(), UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    /// <summary>Downloads and normalises the image; must be called on the UI thread (WPF imaging). Returns the local path.</summary>
    public static async Task<string> DownloadAsync(HttpClient http, string url, string iconsDirectory, CancellationToken cancellationToken)
    {
        if (!IsImageUrl(url))
        {
            throw new InvalidOperationException("Enter a web address that starts with http:// or https://.");
        }

        var uri = new Uri(url.Trim());
        byte[] bytes;
        try
        {
            using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"The server answered {(int)response.StatusCode} {response.ReasonPhrase}.");
            }

            var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
            if (contentType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("That address is a web page, not an image. Use the image's own address (right-click the picture → Copy image address).");
            }

            if (response.Content.Headers.ContentLength is > MaxBytes)
            {
                throw new InvalidOperationException("The image is larger than 10 MB.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
            {
                buffer.Write(chunk, 0, read);
                if (buffer.Length > MaxBytes)
                {
                    throw new InvalidOperationException("The image is larger than 10 MB.");
                }
            }

            bytes = buffer.ToArray();
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"The image could not be downloaded: {ex.Message}", ex);
        }

        var normalised = Normalise(bytes);
        Directory.CreateDirectory(iconsDirectory);
        var path = Path.Combine(iconsDirectory, FileNameFor(uri));
        var temporary = path + ".tmp";
        await File.WriteAllBytesAsync(temporary, normalised, cancellationToken);
        File.Move(temporary, path, overwrite: true);
        return path;
    }

    public static string FileNameFor(Uri uri) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(uri.AbsoluteUri)))[..16].ToLowerInvariant() + ".png";

    /// <summary>Largest frame, scaled to fit <see cref="TargetSize"/>, centred on a transparent square, as PNG bytes.</summary>
    public static byte[] Normalise(byte[] imageBytes)
    {
        BitmapSource frame;
        try
        {
            using var probe = new MemoryStream(imageBytes);
            var header = BitmapDecoder.Create(probe, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            var largest = header.Frames.OrderByDescending(f => (long)f.PixelWidth * f.PixelHeight).First();
            if (Math.Max(largest.PixelWidth, largest.PixelHeight) > MaxDecodedSide)
            {
                // Decode huge images at a reduced size instead of allocating their full bitmap.
                var reduced = new BitmapImage();
                reduced.BeginInit();
                reduced.StreamSource = new MemoryStream(imageBytes);
                reduced.CacheOption = BitmapCacheOption.OnLoad;
                reduced.DecodePixelWidth = largest.PixelWidth >= largest.PixelHeight ? MaxDecodedSide : 0;
                reduced.DecodePixelHeight = largest.PixelWidth >= largest.PixelHeight ? 0 : MaxDecodedSide;
                reduced.EndInit();
                frame = reduced;
            }
            else
            {
                using var input = new MemoryStream(imageBytes);
                var decoder = BitmapDecoder.Create(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                frame = decoder.Frames.OrderByDescending(f => (long)f.PixelWidth * f.PixelHeight).First();
            }
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or InvalidOperationException or ArgumentException)
        {
            throw new InvalidOperationException("The downloaded data is not an image Windows can decode (PNG, JPEG, GIF, BMP, ICO or WebP).", ex);
        }

        var width = frame.PixelWidth;
        var height = frame.PixelHeight;
        var scale = Math.Min(1.0, (double)TargetSize / Math.Max(width, height));
        var drawWidth = Math.Max(1, (int)Math.Round(width * scale));
        var drawHeight = Math.Max(1, (int)Math.Round(height * scale));
        var side = Math.Max(drawWidth, drawHeight);

        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawImage(frame, new Rect((side - drawWidth) / 2.0, (side - drawHeight) / 2.0, drawWidth, drawHeight));
        }

        var canvas = new RenderTargetBitmap(side, side, 96, 96, PixelFormats.Pbgra32);
        canvas.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(canvas));
        using var output = new MemoryStream();
        encoder.Save(output);
        return output.ToArray();
    }
}
