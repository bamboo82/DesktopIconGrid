using System.IO;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopIconGrid.Core.Icons;
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

    private static string Cmd => Path.Combine(Environment.SystemDirectory, "cmd.exe");

    /// <summary>
    /// A square of <paramref name="side"/> px with <paramref name="drawn"/> px drawn at <paramref name="x"/>,
    /// <paramref name="y"/> (centred when not given) and a one-pixel frame at the edge when asked: what the shell gave
    /// for PCMan on a 48 px bar (0.13.0 WP2).
    /// </summary>
    private static BitmapSource Drawing(int side, int drawn, bool frame = false, int? x = null, int? y = null)
    {
        var left = x ?? ((side - drawn) / 2);
        var top = y ?? ((side - drawn) / 2);
        var pixels = new byte[side * side * 4];
        for (var row = 0; row < side; row++)
        {
            for (var column = 0; column < side; column++)
            {
                var inside = column >= left && column < left + drawn && row >= top && row < top + drawn;
                var edge = frame && (column == 0 || row == 0 || column == side - 1 || row == side - 1);
                if (inside || edge)
                {
                    var at = ((row * side) + column) * 4;
                    pixels[at] = 0xC0;
                    pixels[at + 3] = 0xFF;
                }
            }
        }

        return BitmapSource.Create(side, side, 96, 96, PixelFormats.Pbgra32, null, pixels, side * 4);
    }

    private static byte[] PixelsOf(BitmapSource bitmap)
    {
        var bgra = bitmap.Format == PixelFormats.Bgra32 || bitmap.Format == PixelFormats.Pbgra32
            ? bitmap
            : new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[bgra.PixelWidth * bgra.PixelHeight * 4];
        bgra.CopyPixels(pixels, bgra.PixelWidth * 4, 0);
        return pixels;
    }

    /// <summary>Runs <paramref name="work"/> on an STA thread, as the icon worker runs the shell's calls.</summary>
    private static void OnSta(Action work)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                work();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private string Link(string name, string target, string? icon = null)
    {
        var path = Path.Combine(_directory, name);
        var link = (NativeMethods.IShellLinkW)new NativeMethods.CShellLink();
        try
        {
            Marshal.ThrowExceptionForHR(link.SetPath(target));
            if (icon is not null)
            {
                Marshal.ThrowExceptionForHR(link.SetIconLocation(icon, 0));
            }

            ((System.Runtime.InteropServices.ComTypes.IPersistFile)link).Save(path, true);
        }
        finally
        {
            Marshal.ReleaseComObject(link);
        }

        return path;
    }

    [Fact]
    public void A_padded_image_of_a_program_gives_way_to_the_programs_own_icon_at_the_size_asked()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        OnSta(() =>
        {
            var shown = ShellIconProvider.Unpadded(Drawing(96, 32, frame: true), Cmd, 96);

            Assert.IsNotType<CroppedBitmap>(shown);
            Assert.Equal(96, shown.PixelWidth);
            Assert.Null(ShellIconProvider.PaddedBox(shown));
        });
    }

    [Fact]
    public void A_padded_image_of_a_document_is_cut_out_as_a_square_without_the_frame()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var document = Path.Combine(_directory, "notes.txt");
        File.WriteAllText(document, "a document's icon is its type's: no file to take it from");
        OnSta(() =>
        {
            var shown = ShellIconProvider.Unpadded(Drawing(96, 32, frame: true), document, 96);

            Assert.IsType<CroppedBitmap>(shown);
            Assert.Equal((32, 32), (shown.PixelWidth, shown.PixelHeight));
            Assert.All(PixelsOf(shown).Where((_, index) => index % 4 == 3), alpha => Assert.Equal(0xFF, alpha));
        });
    }

    [Fact]
    public void An_image_that_is_not_padded_is_the_shells_own_object()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        OnSta(() =>
        {
            foreach (var image in new[]
                     {
                         Drawing(96, 96), // full
                         Drawing(96, 76), // margins by design, under the limit
                         Drawing(96, 32, x: 4, y: 4), // small, in a corner
                         Drawing(96, 0), // empty
                     })
            {
                Assert.Same(image, ShellIconProvider.Unpadded(image, Cmd, 96));
            }
        });
    }

    [Fact]
    public void A_links_icon_is_the_one_it_names_else_its_target_when_that_holds_icons()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var ico = Path.Combine(_directory, "small.ico");
        File.WriteAllBytes(ico, DibIcoBytes(32));
        var document = Path.Combine(_directory, "notes.txt");
        File.WriteAllText(document, "text");
        OnSta(() =>
        {
            var named = ShellIconProvider.IconLocationOf(Link("named.lnk", Cmd, ico));
            var plain = ShellIconProvider.IconLocationOf(Link("plain.lnk", Cmd));
            // The runner's temporary folder may be written in 8.3 names (RUNNER~1), and the link may give it back long.
            Assert.Equal("small.ico", Path.GetFileName(named?.Path), ignoreCase: true);
            Assert.True(File.Exists(named?.Path));
            Assert.Equal(0, named?.Index);
            Assert.Equal(Cmd, plain?.Path, ignoreCase: true);
            Assert.Null(ShellIconProvider.IconLocationOf(Link("document.lnk", document)));
            Assert.Null(ShellIconProvider.IconLocationOf(document));
            Assert.Equal((Cmd, 0), ShellIconProvider.IconLocationOf(Cmd));
        });
    }

    [Fact]
    public void A_link_whose_icon_has_only_a_32_px_image_fills_the_size_asked()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        // The case PCMan showed, through the shell: whether this Windows pads the small icon or not, what the bar gets is
        // not padded.
        var ico = Path.Combine(_directory, "small.ico");
        File.WriteAllBytes(ico, DibIcoBytes(32));
        var link = Link("small-icon.lnk", Cmd, ico);
        OnSta(() =>
        {
            foreach (var size in new[] { 64, 96, 128 })
            {
                var shown = Assert.IsAssignableFrom<BitmapSource>(new ShellIconProvider().GetIcon(link, size));
                Assert.Null(ShellIconProvider.PaddedBox(shown));
            }
        });
    }

    [Fact]
    public void Windows_own_programs_come_out_as_the_shell_draws_them_unless_the_shell_padded_them()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        // How "an icon right before comes out pixel-identical" is checked (0.13.0 WP2): the provider hands out the shell's
        // own pixels for every icon that is not padded.
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var programs = new[] { Cmd, Path.Combine(windows, "explorer.exe"), Path.Combine(windows, "regedit.exe"), Path.Combine(Environment.SystemDirectory, "notepad.exe") }
            .Where(File.Exists)
            .ToList();
        OnSta(() =>
        {
            foreach (var program in programs)
            {
                foreach (var size in new[] { 32, 64, 96, 128 })
                {
                    var raw = ShellIconProvider.TryShellItemImage(program, size);
                    var shown = new ShellIconProvider().GetIcon(program, size) as BitmapSource;
                    Assert.NotNull(raw);
                    Assert.NotNull(shown);
                    if (ShellIconProvider.PaddedBox(raw) is null)
                    {
                        Assert.Equal(PixelsOf(raw), PixelsOf(shown));
                    }
                    else
                    {
                        Assert.Null(ShellIconProvider.PaddedBox(shown));
                    }
                }
            }
        });
    }

    /// <summary>An .ico with one 32-bit DIB image of <paramref name="side"/> px, wholly drawn: a file with only a small icon.</summary>
    private static byte[] DibIcoBytes(int side)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        var maskStride = (side + 31) / 32 * 4;
        writer.Write((ushort)0); // reserved
        writer.Write((ushort)1); // icon
        writer.Write((ushort)1); // one image
        writer.Write((byte)side);
        writer.Write((byte)side);
        writer.Write((byte)0); // colours in palette
        writer.Write((byte)0); // reserved
        writer.Write((ushort)1); // planes
        writer.Write((ushort)32); // bits per pixel
        writer.Write(40 + (side * side * 4) + (maskStride * side));
        writer.Write(6 + 16); // offset of the image data
        writer.Write(40); // BITMAPINFOHEADER
        writer.Write(side);
        writer.Write(side * 2); // the image and its mask
        writer.Write((ushort)1);
        writer.Write((ushort)32);
        writer.Write(0); // BI_RGB
        writer.Write(side * side * 4);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        for (var i = 0; i < side * side; i++)
        {
            writer.Write((byte)0xC0);
            writer.Write((byte)0x40);
            writer.Write((byte)0x20);
            writer.Write((byte)0xFF);
        }

        writer.Write(new byte[maskStride * side]); // the mask: all drawn, the alpha decides
        writer.Flush();
        return stream.ToArray();
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
