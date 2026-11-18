using System.Text;

namespace DesktopIconGrid.Core.Fonts;

/// <summary>A font file to download: where from, and exactly what it must be.</summary>
/// <param name="Url">Where it is downloaded from.</param>
/// <param name="FileName">The file's name, the same wherever it is put.</param>
/// <param name="Size">Its length in bytes.</param>
/// <param name="Sha256">Its SHA-256 digest, lowercase hexadecimal.</param>
public sealed record FontSource(Uri Url, string FileName, long Size, string Sha256);

/// <summary>
/// Taipei Sans TC Beta Light (台北黑體), the font of the app's Chinese text when this PC has it (0.12.0 WP5): SIL Open
/// Font License 1.1, by JT Foundry, based on Source Han Sans. It is never shipped with the app; Settings installs it for
/// the user on a click. Size, digest and address are those of <c>vp-tw/cjk-web-fonts</c>' <c>source.json</c>, checked
/// against the file itself.
/// </summary>
public static class TaipeiSans
{
    public const string FileName = "TaipeiSansTCBeta-Light.ttf";

    /// <summary>The name the file gives itself (name 4), for its registry value when the file says nothing.</summary>
    public const string FullName = "Taipei Sans TC Beta Light";

    /// <summary>The file, from the locked mirror of commit 24ca88e of <c>vp-tw/taipei-sans-tc</c>.</summary>
    public static FontSource Light { get; } = new(
        new Uri("https://media.githubusercontent.com/media/vp-tw/taipei-sans-tc/24ca88e96d1e842558df3ad6fad71993c07754c4/packages/core/src/TaipeiSansTCBeta-Light.ttf"),
        FileName,
        20_279_264,
        "d69a9ea6a77f694c3a1a1fb766c764aa74cb3e2c4c69f1b532edf98b3f2bb662");

    /// <summary>The author's page, for installing it by hand.</summary>
    public static Uri ManualPage { get; } = new("https://sites.google.com/view/jtfoundry/zh-tw/downloads");
}

/// <summary>The fonts Windows lists in the registry (<c>…\Windows NT\CurrentVersion\Fonts</c>), by value name and data.</summary>
public interface IFontRegistry
{
    /// <summary>The fonts installed for this user (HKCU).</summary>
    IEnumerable<KeyValuePair<string, string>> UserFonts();

    /// <summary>The fonts installed for every user (HKLM).</summary>
    IEnumerable<KeyValuePair<string, string>> MachineFonts();

    /// <summary>Lists a font installed for this user.</summary>
    void AddUserFont(string name, string path);

    /// <summary>Takes a user font's value away again (an install that could not finish).</summary>
    void RemoveUserFont(string name);
}

/// <summary>
/// Finds a font file the app may use, in this order (0.12.0 WP5, T1): <c>Fonts\</c> next to the program (a portable
/// copy can carry it), a font installed for this user, a font installed for every user. A registry value counts when
/// its data names the file, as a full path or a bare file name (in the user's or Windows' font folder), and that file
/// is there. The app refers to the font by this file, never by a family name, which another weight of the same family
/// installed on the PC would answer.
/// </summary>
/// <param name="registry">The registry's font lists.</param>
/// <param name="fileExists">Whether a file is there.</param>
/// <param name="programDirectory">The folder of the program.</param>
/// <param name="userFontsDirectory"><c>%LOCALAPPDATA%\Microsoft\Windows\Fonts</c>.</param>
/// <param name="windowsFontsDirectory"><c>%WINDIR%\Fonts</c>.</param>
public sealed class FontLocator(IFontRegistry registry, Func<string, bool> fileExists, string programDirectory, string userFontsDirectory, string windowsFontsDirectory)
{
    /// <summary>The full path of <paramref name="fileName"/> as the first place that has it, or <see langword="null"/>.</summary>
    public string? Find(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        var carried = Path.Combine(programDirectory, "Fonts", fileName);
        if (fileExists(carried))
        {
            return carried;
        }

        return InRegistry(registry.UserFonts(), userFontsDirectory) ?? InRegistry(registry.MachineFonts(), windowsFontsDirectory);

        string? InRegistry(IEnumerable<KeyValuePair<string, string>> values, string folder)
        {
            foreach (var (_, data) in values)
            {
                var path = data?.Trim().Trim('"') ?? string.Empty;
                if (!string.Equals(FileNameOf(path), fileName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var full = Path.IsPathRooted(path) ? path : Path.Combine(folder, path);
                if (fileExists(full))
                {
                    return full;
                }
            }

            return null;
        }
    }

    /// <summary>The file name of a path written with either kind of slash (the registry's are Windows paths).</summary>
    private static string FileNameOf(string path) => path[(path.LastIndexOfAny(['\\', '/']) + 1)..];
}

/// <summary>Reads a TrueType or OpenType file's own names from its <c>name</c> table.</summary>
public static class FontNames
{
    /// <summary>
    /// The font's full name (name 4) in US English where it has one, else in the first language it gives, or
    /// <see langword="null"/> when the file has no readable name table.
    /// </summary>
    public static string? FullName(Stream font)
    {
        ArgumentNullException.ThrowIfNull(font);
        try
        {
            var header = Read(font, 0, 12);
            var tables = ReadUInt16(header, 4);
            var directory = Read(font, 12, tables * 16);
            for (var i = 0; i < tables; i++)
            {
                if (Encoding.ASCII.GetString(directory, i * 16, 4) != "name")
                {
                    continue;
                }

                var offset = ReadUInt32(directory, (i * 16) + 8);
                var length = ReadUInt32(directory, (i * 16) + 12);
                if (length is < 6 or > 1_000_000)
                {
                    return null;
                }

                return FullNameIn(Read(font, offset, (int)length));
            }
        }
        catch (Exception ex) when (ex is EndOfStreamException or IOException or ArgumentException)
        {
            return null;
        }

        return null;
    }

    private static string? FullNameIn(byte[] table)
    {
        var count = ReadUInt16(table, 2);
        var strings = ReadUInt16(table, 4);
        string? first = null;
        for (var i = 0; i < count && 6 + (i * 12) + 12 <= table.Length; i++)
        {
            var record = 6 + (i * 12);
            var (platform, language, nameId) = (ReadUInt16(table, record), ReadUInt16(table, record + 4), ReadUInt16(table, record + 6));
            var (length, offset) = (ReadUInt16(table, record + 8), ReadUInt16(table, record + 10));
            if (nameId != 4 || platform is not (0 or 3) || strings + offset + length > table.Length)
            {
                continue; // Unicode and Windows names only: both are UTF-16, big-endian
            }

            var name = Encoding.BigEndianUnicode.GetString(table, strings + offset, length);
            if (platform == 3 && language == 0x0409)
            {
                return name;
            }

            first ??= name;
        }

        return first;
    }

    private static byte[] Read(Stream stream, long offset, int count)
    {
        stream.Seek(offset, SeekOrigin.Begin);
        var bytes = new byte[count];
        stream.ReadExactly(bytes);
        return bytes;
    }

    private static int ReadUInt16(byte[] bytes, int at) => (bytes[at] << 8) | bytes[at + 1];

    private static long ReadUInt32(byte[] bytes, int at) => ((long)bytes[at] << 24) | ((long)bytes[at + 1] << 16) | ((long)bytes[at + 2] << 8) | bytes[at + 3];
}
