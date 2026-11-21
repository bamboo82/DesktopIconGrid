using System.IO;
using System.Runtime.InteropServices;

namespace DesktopIconGrid.Services;

/// <summary>
/// The frames of an <c>.ico</c> file, and the icon handle of the one that suits a size (0.14.0 WP7): the tray takes
/// <c>tray.ico</c>'s frame for the notification area's size, drawn on the pixel grid at 16, 20, 24 and 32, rather than a
/// larger picture scaled down.
/// </summary>
public sealed class IconFrames
{
    private readonly IReadOnlyList<(int Size, byte[] Data)> _frames;

    private IconFrames(IReadOnlyList<(int Size, byte[] Data)> frames) => _frames = frames;

    /// <summary>The frames' sizes, as the file lists them.</summary>
    public IReadOnlyList<int> Sizes => [.. _frames.Select(frame => frame.Size)];

    /// <summary>
    /// Reads an <c>.ico</c>: its directory (a 6-byte header, then 16 bytes for each frame) and each frame's data, PNG or a
    /// bitmap. <see langword="null"/> when it is not one.
    /// </summary>
    public static IconFrames? Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        var bytes = copy.ToArray();
        if (bytes.Length < 6 || BitConverter.ToUInt16(bytes, 0) != 0 || BitConverter.ToUInt16(bytes, 2) != 1)
        {
            return null;
        }

        var count = BitConverter.ToUInt16(bytes, 4);
        var frames = new List<(int, byte[])>(count);
        for (var i = 0; i < count; i++)
        {
            var entry = 6 + (16 * i);
            if (entry + 16 > bytes.Length)
            {
                return null;
            }

            var width = bytes[entry] == 0 ? 256 : bytes[entry];
            var length = BitConverter.ToInt32(bytes, entry + 8);
            var offset = BitConverter.ToInt32(bytes, entry + 12);
            if (length <= 0 || offset < 0 || (long)offset + length > bytes.Length)
            {
                return null;
            }

            frames.Add((width, bytes[offset..(offset + length)]));
        }

        return frames.Count == 0 ? null : new IconFrames(frames);
    }

    /// <summary>
    /// The size of the frame for <paramref name="wanted"/>: that size when the file has it (never a larger one scaled down
    /// then), else the nearest, the larger of two as near.
    /// </summary>
    public static int Pick(IReadOnlyList<int> sizes, int wanted)
    {
        ArgumentNullException.ThrowIfNull(sizes);
        return sizes.Contains(wanted)
            ? wanted
            : sizes.OrderBy(size => Math.Abs(size - wanted)).ThenByDescending(size => size).First();
    }

    /// <summary>An icon handle of <paramref name="size"/> pixels from the frame that suits it; <see cref="IntPtr.Zero"/> when Windows cannot make one.</summary>
    public IntPtr CreateIcon(int size)
    {
        var pick = Pick(Sizes, size);
        var data = _frames.First(frame => frame.Size == pick).Data;
        return CreateIconFromResourceEx(data, (uint)data.Length, fIcon: true, 0x00030000, size, size, 0);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreateIconFromResourceEx(byte[] presbits, uint dwResSize, [MarshalAs(UnmanagedType.Bool)] bool fIcon, uint dwVer, int cxDesired, int cyDesired, uint flags);
}
