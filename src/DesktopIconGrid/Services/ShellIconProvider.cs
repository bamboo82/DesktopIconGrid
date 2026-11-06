using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopIconGrid.Core;
using DesktopIconGrid.Core.Launching;
using DesktopIconGrid.Core.Models;

namespace DesktopIconGrid.Services;

/// <summary>
/// Resolves icons the way Explorer does, with an in-memory cache. Resolution order:
/// image files → <c>path,index</c> resources → shell items (files, folders, .lnk, shell: and CLSID
/// names) → the system image list by extension → <see langword="null"/> (caller shows a placeholder).
/// </summary>
public sealed class ShellIconProvider : IIconProvider
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".ico", ".tif", ".tiff",
    };

    private static readonly HashSet<string> ResourceExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".ico", ".icl", ".cpl", ".ocx", ".scr", ".mun",
    };

    private readonly ConcurrentDictionary<string, Lazy<ImageSource?>> _cache = new(StringComparer.OrdinalIgnoreCase);

    public ImageSource? GetIcon(string source, int pixelSize)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return null;
        }

        var trimmed = source.Trim();
        return Cached($"{pixelSize}|{trimmed}", () => Load(trimmed, Math.Clamp(pixelSize, 16, 256)));
    }

    public ImageSource? GetItemIcon(LauncherItem item, int pixelSize)
    {
        ArgumentNullException.ThrowIfNull(item);
        var requested = RequestSize(pixelSize);

        if (item.HasCustomIcon)
        {
            var custom = GetIcon(item.IconPath!, requested);
            if (custom is not null)
            {
                return custom;
            }
        }

        return item switch
        {
            GroupItem => GetFolderIcon(requested),
            ShortcutItem shortcut => GetIcon(shortcut.EffectiveLocation, requested),
            _ => null,
        };
    }

    public ImageSource? GetFolderIcon(int pixelSize) =>
        Cached($"{pixelSize}|folder", () => TrySystemImageListIcon("folder", pixelSize, NativeMethods.FILE_ATTRIBUTE_DIRECTORY));

    /// <summary>
    /// One lookup per key even when several cells ask at the same time (the others wait for it); misses are
    /// not kept, so a drive that comes back later is asked again.
    /// </summary>
    private ImageSource? Cached(string key, Func<ImageSource?> load)
    {
        var entry = _cache.GetOrAdd(key, _ => new Lazy<ImageSource?>(() => Frozen(load()), LazyThreadSafetyMode.ExecutionAndPublication));
        ImageSource? image;
        try
        {
            image = entry.Value;
        }
        catch
        {
            _cache.TryRemove(KeyValuePair.Create(key, entry));
            throw;
        }

        if (image is null)
        {
            _cache.TryRemove(KeyValuePair.Create(key, entry));
        }

        return image;
    }

    /// <summary>
    /// Icons cross from the worker thread to the UI thread, so every image handed out must be frozen; a bitmap that
    /// cannot be frozen in place (some decoder frames and interop bitmaps) is copied into one that can.
    /// </summary>
    private static ImageSource? Frozen(ImageSource? image)
    {
        if (image is null || image.IsFrozen)
        {
            return image;
        }

        if (image.CanFreeze)
        {
            image.Freeze();
            return image;
        }

        if (image is BitmapSource bitmap)
        {
            var copy = new WriteableBitmap(bitmap);
            copy.Freeze();
            return copy;
        }

        Debug.Fail($"Unfreezable icon of type {image.GetType().Name}");
        return null;
    }

    public bool CanResolve(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return false;
        }

        var expanded = Environment.ExpandEnvironmentVariables(source.Trim());
        if (TryParseResource(expanded, out _, out _))
        {
            return true;
        }

        return File.Exists(expanded)
            || Directory.Exists(expanded)
            || LaunchPlanner.IsUri(expanded)
            || LaunchPlanner.IsShellNamespace(expanded)
            || expanded.StartsWith("::{", StringComparison.Ordinal)
            || ExecutableLocator.Exists(expanded);
    }

    public void ClearCache() => _cache.Clear();

    /// <summary>Ask the shell for twice the display size (capped) so icons stay crisp on high-DPI screens.</summary>
    private static int RequestSize(int pixelSize) => Math.Clamp(pixelSize * 2, 32, 256);

    private static ImageSource? Load(string source, int pixelSize)
    {
        var expanded = Environment.ExpandEnvironmentVariables(source);

        if (LaunchPlanner.IsSpecial(expanded))
        {
            return null;
        }

        if (TryParseResource(expanded, out var resourcePath, out var resourceIndex))
        {
            var fromResource = TryExtractResourceIcon(resourcePath, resourceIndex, pixelSize);
            if (fromResource is not null)
            {
                return fromResource;
            }
        }

        var extension = Path.GetExtension(expanded);
        if (ImageExtensions.Contains(extension) && File.Exists(expanded))
        {
            var fromImage = TryLoadImageFile(expanded, pixelSize);
            if (fromImage is not null)
            {
                return fromImage;
            }
        }

        if (LaunchPlanner.IsShellNamespace(expanded))
        {
            return TryShellItemImage(expanded, pixelSize)
                ?? (ShellLocations.TryGetParsingName(expanded, out var parsingName) ? TryShellItemImage(parsingName, pixelSize) : null)
                ?? TrySystemImageListIcon("folder", pixelSize, NativeMethods.FILE_ATTRIBUTE_DIRECTORY);
        }

        if (expanded.StartsWith("::{", StringComparison.Ordinal))
        {
            return TryShellItemImage(expanded, pixelSize);
        }

        if (File.Exists(expanded) || Directory.Exists(expanded))
        {
            return TryShellItemImage(expanded, pixelSize)
                ?? TrySystemImageListIcon(expanded, pixelSize, attributes: null);
        }

        if (LaunchPlanner.IsUri(expanded))
        {
            var scheme = new Uri(expanded).Scheme;
            var stand_in = scheme is "http" or "https" ? "link.html" : "link.url";
            return TrySystemImageListIcon(stand_in, pixelSize, NativeMethods.FILE_ATTRIBUTE_NORMAL);
        }

        var resolved = ExecutableLocator.Find(expanded);
        if (resolved is not null)
        {
            return TryShellItemImage(resolved, pixelSize)
                ?? TrySystemImageListIcon(resolved, pixelSize, attributes: null);
        }

        // Unknown path: fall back to the icon for its extension.
        return TrySystemImageListIcon(
            string.IsNullOrEmpty(extension) ? expanded + ".exe" : expanded,
            pixelSize,
            NativeMethods.FILE_ATTRIBUTE_NORMAL);
    }

    /// <summary>Parses <c>path,index</c> for icon resources. The path must exist and have a resource-bearing extension.</summary>
    internal static bool TryParseResource(string value, out string path, out int index)
    {
        path = string.Empty;
        index = 0;

        var comma = value.LastIndexOf(',');
        if (comma > 0
            && int.TryParse(value.AsSpan(comma + 1).Trim(), out var parsedIndex))
        {
            var candidate = value[..comma].Trim();
            if (ResourceExtensions.Contains(Path.GetExtension(candidate)) && File.Exists(candidate))
            {
                path = candidate;
                index = parsedIndex;
                return true;
            }
        }

        if (string.Equals(Path.GetExtension(value), ".dll", StringComparison.OrdinalIgnoreCase) && File.Exists(value))
        {
            path = value;
            index = 0;
            return true;
        }

        return false;
    }

    private static ImageSource? TryLoadImageFile(string path, int pixelSize)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count == 0)
            {
                return null;
            }

            // Prefer the smallest frame that is at least the requested size; otherwise the largest available.
            var frames = decoder.Frames
                .OrderBy(f => f.PixelWidth)
                .ThenByDescending(f => f.Format.BitsPerPixel)
                .ToList();
            return frames.FirstOrDefault(f => f.PixelWidth >= pixelSize)
                   ?? frames.OrderByDescending(f => f.PixelWidth).ThenByDescending(f => f.Format.BitsPerPixel).First();
        }
        catch (Exception ex) when (ex is IOException
                                   or UnauthorizedAccessException
                                   or NotSupportedException
                                   or FileFormatException
                                   or ArgumentException
                                   or InvalidOperationException)
        {
            return null;
        }
    }

    private static ImageSource? TryExtractResourceIcon(string path, int index, int pixelSize)
    {
        var large = IntPtr.Zero;
        var small = IntPtr.Zero;
        try
        {
            var size = (uint)Math.Clamp(pixelSize, 16, 256);
            var hr = NativeMethods.SHDefExtractIcon(path, index, 0, out large, out small, size | (size << 16));
            if (hr != 0 || large == IntPtr.Zero)
            {
                return null;
            }

            return Imaging.CreateBitmapSourceFromHIcon(large, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        }
        catch (Exception ex) when (ex is COMException or ExternalException or EntryPointNotFoundException or DllNotFoundException)
        {
            return null;
        }
        finally
        {
            if (large != IntPtr.Zero)
            {
                NativeMethods.DestroyIcon(large);
            }

            if (small != IntPtr.Zero)
            {
                NativeMethods.DestroyIcon(small);
            }
        }
    }

    private static ImageSource? TryShellItemImage(string parsingName, int pixelSize)
    {
        var hBitmap = IntPtr.Zero;
        try
        {
            var iid = typeof(NativeMethods.IShellItemImageFactory).GUID;
            var hr = NativeMethods.SHCreateItemFromParsingName(parsingName, IntPtr.Zero, ref iid, out var factory);
            if (hr != 0 || factory is null)
            {
                return null;
            }

            hr = factory.GetImage(
                new NativeMethods.SIZE(pixelSize, pixelSize),
                NativeMethods.SIIGBF.IconOnly | NativeMethods.SIIGBF.BiggerSizeOk,
                out hBitmap);
            if (hr != 0 || hBitmap == IntPtr.Zero)
            {
                return null;
            }

            return BitmapFromHBitmap(hBitmap);
        }
        catch (Exception ex) when (ex is COMException or ExternalException or EntryPointNotFoundException or DllNotFoundException or InvalidCastException)
        {
            return null;
        }
        finally
        {
            if (hBitmap != IntPtr.Zero)
            {
                NativeMethods.DeleteObject(hBitmap);
            }
        }
    }

    /// <summary>Copies a 32-bpp GDI bitmap (premultiplied BGRA, as the shell produces) into a WPF bitmap.</summary>
    private static BitmapSource? BitmapFromHBitmap(IntPtr hBitmap)
    {
        var bitmap = default(NativeMethods.BITMAP);
        if (NativeMethods.GetObject(hBitmap, Marshal.SizeOf<NativeMethods.BITMAP>(), ref bitmap) == 0)
        {
            return null;
        }

        var width = bitmap.bmWidth;
        var height = Math.Abs(bitmap.bmHeight);
        if (width <= 0 || height <= 0 || width > 1024 || height > 1024)
        {
            return null;
        }

        var info = new NativeMethods.BITMAPINFO
        {
            bmiHeader = new NativeMethods.BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<NativeMethods.BITMAPINFOHEADER>(),
                biWidth = width,
                biHeight = -height, // top-down
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0, // BI_RGB
            },
        };

        var stride = width * 4;
        var pixels = new byte[stride * height];
        var hdc = NativeMethods.GetDC(IntPtr.Zero);
        try
        {
            if (NativeMethods.GetDIBits(hdc, hBitmap, 0, (uint)height, pixels, ref info, 0) == 0)
            {
                return null;
            }
        }
        finally
        {
            NativeMethods.ReleaseDC(IntPtr.Zero, hdc);
        }

        // Bitmaps without an alpha channel come back with alpha = 0 everywhere; treat them as opaque.
        if (bitmap.bmBitsPixel < 32 || !HasAnyAlpha(pixels))
        {
            for (var i = 3; i < pixels.Length; i += 4)
            {
                pixels[i] = 255;
            }
        }

        return BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, pixels, stride);
    }

    private static bool HasAnyAlpha(byte[] pixels)
    {
        for (var i = 3; i < pixels.Length; i += 4)
        {
            if (pixels[i] != 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Icon from the system image list. With <paramref name="attributes"/> set, the path does not have to exist
    /// and the icon is chosen by extension (or the folder icon for <see cref="NativeMethods.FILE_ATTRIBUTE_DIRECTORY"/>).
    /// </summary>
    private static ImageSource? TrySystemImageListIcon(string path, int pixelSize, uint? attributes)
    {
        var hIcon = IntPtr.Zero;
        try
        {
            var info = default(NativeMethods.SHFILEINFO);
            var flags = NativeMethods.SHGFI_SYSICONINDEX;
            if (attributes.HasValue)
            {
                flags |= NativeMethods.SHGFI_USEFILEATTRIBUTES;
            }

            var list = NativeMethods.SHGetFileInfo(
                path,
                attributes ?? NativeMethods.FILE_ATTRIBUTE_NORMAL,
                ref info,
                (uint)Marshal.SizeOf<NativeMethods.SHFILEINFO>(),
                flags);
            if (list == IntPtr.Zero)
            {
                return null;
            }

            var listId = pixelSize switch
            {
                <= 32 => NativeMethods.SHIL_LARGE,
                <= 48 => NativeMethods.SHIL_EXTRALARGE,
                _ => NativeMethods.SHIL_JUMBO,
            };

            var iid = typeof(NativeMethods.IImageList).GUID;
            if (NativeMethods.SHGetImageList(listId, ref iid, out var imageList) != 0 || imageList is null)
            {
                return null;
            }

            if (imageList.GetIcon(info.iIcon, NativeMethods.ILD_TRANSPARENT, out hIcon) != 0 || hIcon == IntPtr.Zero)
            {
                return null;
            }

            return Imaging.CreateBitmapSourceFromHIcon(hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        }
        catch (Exception ex) when (ex is COMException or ExternalException or EntryPointNotFoundException or DllNotFoundException or InvalidCastException)
        {
            return null;
        }
        finally
        {
            if (hIcon != IntPtr.Zero)
            {
                NativeMethods.DestroyIcon(hIcon);
            }
        }
    }
}
