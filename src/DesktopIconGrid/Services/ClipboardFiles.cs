using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Media.Imaging;
using DesktopIconGrid.Core.Diagnostics;
using DesktopIconGrid.Core.SpecialIcons;

namespace DesktopIconGrid.Services;

/// <summary>
/// What the clipboard holds, as files to upload: copied files as they are; a picture saved as a PNG and text as a
/// UTF-8 <c>.txt</c> in <paramref name="directory"/> (the Drive cache, which is emptied of old files at start-up).
/// </summary>
public static class ClipboardFiles
{
    /// <summary>The files, or an empty list when the clipboard holds nothing usable or cannot be read.</summary>
    public static IReadOnlyList<string> Take(string directory, DateTime now)
    {
        try
        {
            if (Clipboard.ContainsFileDropList())
            {
                return Clipboard.GetFileDropList().Cast<string>().Where(path => File.Exists(path) || Directory.Exists(path)).ToList();
            }

            if (Clipboard.ContainsImage() && Clipboard.GetImage() is { } image)
            {
                var path = Path.Combine(Directory.CreateDirectory(directory).FullName, PasteNames.Image(now));
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(image));
                using var stream = File.Create(path);
                encoder.Save(stream);
                return [path];
            }

            if (Clipboard.ContainsText() && Clipboard.GetText() is { Length: > 0 and <= PasteNames.MaxTextChars } text && !string.IsNullOrWhiteSpace(text))
            {
                var path = Path.Combine(Directory.CreateDirectory(directory).FullName, PasteNames.Text(now));
                File.WriteAllText(path, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)); // the mark: Notepad and the Drive preview then read non-ASCII text right
                return [path];
            }
        }
        catch (Exception ex) when (ex is ExternalException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // The clipboard is held by another program, or the picture could not be written.
            AppLog.Default.Warning("Bucket", $"The clipboard could not be pasted: {ex.Message}");
        }

        return [];
    }
}
