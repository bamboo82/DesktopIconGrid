using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopIconGrid.Services;

namespace DesktopIconGrid.App.Tests;

/// <summary>Icons cross from the worker thread to the UI thread, so every image the provider hands out must be frozen.</summary>
public sealed class ShellIconProviderTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("dig-icons-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void Png_icons_are_frozen_and_cached()
    {
        var path = Path.Combine(_directory, "icon.png");
        File.WriteAllBytes(path, PngBytes(24));
        var provider = new ShellIconProvider();

        var icon = provider.GetIcon(path, 32);

        Assert.NotNull(icon);
        Assert.True(icon.IsFrozen);
        Assert.Same(icon, provider.GetIcon(path, 32));
    }

    [Fact]
    public void Ico_files_with_png_compressed_frames_are_frozen()
    {
        var path = Path.Combine(_directory, "icon.ico");
        File.WriteAllBytes(path, IcoBytes(PngBytes(16)));
        var provider = new ShellIconProvider();

        var icon = provider.GetIcon(path, 16);

        Assert.NotNull(icon);
        Assert.True(icon.IsFrozen);
    }

    private static byte[] PngBytes(int side)
    {
        var bitmap = new WriteableBitmap(side, side, 96, 96, PixelFormats.Bgra32, null);
        var pixels = new byte[side * side * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 0xC0; // blue
            pixels[i + 3] = 0xFF; // alpha
        }

        bitmap.WritePixels(new System.Windows.Int32Rect(0, 0, side, side), pixels, side * 4, 0);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    /// <summary>An .ico container with one PNG-compressed 16×16 entry (the format Vista and later write).</summary>
    private static byte[] IcoBytes(byte[] png)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)0); // reserved
        writer.Write((ushort)1); // icon
        writer.Write((ushort)1); // one image
        writer.Write((byte)16); // width
        writer.Write((byte)16); // height
        writer.Write((byte)0); // colours in palette
        writer.Write((byte)0); // reserved
        writer.Write((ushort)1); // planes
        writer.Write((ushort)32); // bits per pixel
        writer.Write(png.Length);
        writer.Write(6 + 16); // offset of the image data
        writer.Write(png);
        writer.Flush();
        return stream.ToArray();
    }
}
