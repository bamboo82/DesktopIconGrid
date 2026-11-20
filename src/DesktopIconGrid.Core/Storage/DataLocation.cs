using System.Text;
using System.Text.RegularExpressions;

namespace DesktopIconGrid.Core.Storage;

/// <summary>Where the data goes, as the start found it (0.13.0 WP1).</summary>
/// <param name="Directory">The data folder: what <c>portable.txt</c> names, else <c>%LocalAppData%\DesktopIconGrid</c>.</param>
/// <param name="Portable"><c>portable.txt</c> is next to the program and the folder it names is used.</param>
/// <param name="Unusable"><c>portable.txt</c> is there, but the folder it names cannot be used, so the data is in <c>%LocalAppData%</c>.</param>
/// <param name="Named">The folder <c>portable.txt</c> names, in full, or its line as written when no folder can be made of it; <see langword="null"/> without the file.</param>
public sealed record DataPlace(string Directory, bool Portable, bool Unusable, string? Named);

/// <summary>
/// Where the app keeps its files: <c>%LocalAppData%\DesktopIconGrid</c>, or, in portable mode, the folder that
/// <c>portable.txt</c> next to the program names (since 0.13.0; empty or <c>.</c> is the program's own folder, as before),
/// so a tools drive carries the bar from PC to PC. <c>%APPDRIVE%</c> names the drive the program runs from, so shortcuts
/// on that drive keep working when its letter changes.
/// </summary>
public static partial class DataLocation
{
    /// <summary>The file next to the program that turns portable mode on, and names the data folder.</summary>
    public const string PortableMarker = "portable.txt";

    /// <summary>The environment variable set to the program's drive (<c>E:</c>).</summary>
    public const string AppDriveVariable = "APPDRIVE";

    /// <summary>Only the start of <c>portable.txt</c> is read: a path is short, and a large file is not this app's.</summary>
    public const int MaxMarkerBytes = 4096;

    /// <summary>A line longer than this names no folder.</summary>
    public const int MaxLineLength = 1024;

    /// <summary>A data folder's full name is at most this long, so the files in it stay under Windows' 260.</summary>
    public const int MaxFolderLength = 248;

    private static readonly Lazy<DataPlace> Here = new(() => Resolve(
        AppContext.BaseDirectory,
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify),
        File.Exists,
        CanWriteTo,
        ReadMarker,
        MakeFolder));

    /// <summary>The folder for <c>layout.json</c>, <c>sync.json</c>, logs and caches.</summary>
    public static string Directory => Here.Value.Directory;

    /// <summary><c>portable.txt</c> is next to the program and its data is where the file says, wherever that is.</summary>
    public static bool IsPortable => Here.Value.Portable;

    /// <summary>
    /// <c>portable.txt</c> is next to the program, but the folder it names cannot be used (a name that is no folder's, one
    /// that cannot be made or written: under Program Files without elevation, on read-only media), so the data is in
    /// <c>%LocalAppData%</c> after all; the app says so.
    /// </summary>
    public static bool PortableFolderIsUnusable => Here.Value.Unusable;

    /// <summary>
    /// <c>portable.txt</c> is next to the program, whether or not its data could go where it says: the copy travels, and
    /// asks before it adds a Run entry on a PC it visits.
    /// </summary>
    public static bool IsPortableCopy => IsPortable || PortableFolderIsUnusable;

    /// <summary>What <c>portable.txt</c> names (<see cref="DataPlace.Named"/>).</summary>
    public static string? NamedFolder => Here.Value.Named;

    /// <summary>The program's folder, without the separator at its end.</summary>
    public static string ProgramDirectory => Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);

    /// <summary><c>portable.txt</c> beside the program.</summary>
    public static string MarkerFile => Path.Combine(ProgramDirectory, PortableMarker);

    /// <summary>The data folder without <c>portable.txt</c>: <c>%LocalAppData%\DesktopIconGrid</c>.</summary>
    public static string ThisPcDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify),
        LayoutStorage.ApplicationFolderName);

    /// <summary>
    /// The data folder for a program in <paramref name="programDirectory"/> (0.13.0 WP1). Without <c>portable.txt</c>:
    /// <c>%LocalAppData%</c>. With it: the folder its first line names (<see cref="FolderOf"/>), made when missing; when that
    /// line names no folder, or the folder cannot be made or written, <c>%LocalAppData%</c> again, marked
    /// <see cref="DataPlace.Unusable"/>, instead of stopping the app.
    /// </summary>
    /// <param name="readMarker">The start of <c>portable.txt</c> as text; none reads it as empty, the program's own folder.</param>
    /// <param name="makeFolder">Makes a missing folder; false when it cannot. None leaves folders as they are.</param>
    /// <param name="expand">Expands environment variables; by default this process's.</param>
    public static DataPlace Resolve(
        string programDirectory,
        string localAppData,
        Func<string, bool> fileExists,
        Func<string, bool>? canWrite = null,
        Func<string, string?>? readMarker = null,
        Func<string, bool>? makeFolder = null,
        Func<string, string>? expand = null)
    {
        ArgumentNullException.ThrowIfNull(fileExists);
        var local = Path.Combine(localAppData ?? string.Empty, LayoutStorage.ApplicationFolderName);
        if (string.IsNullOrWhiteSpace(programDirectory) || !fileExists(Path.Combine(programDirectory, PortableMarker)))
        {
            return new DataPlace(local, false, false, null);
        }

        var text = readMarker?.Invoke(Path.Combine(programDirectory, PortableMarker));
        var line = FirstLine(text);
        if (line is null || FolderOf(line, programDirectory, expand) is not { } folder)
        {
            return new DataPlace(local, false, true, Shown(line ?? text ?? string.Empty));
        }

        var usable = (makeFolder is null || makeFolder(folder)) && (canWrite is null || canWrite(folder));
        return usable ? new DataPlace(folder, true, false, folder) : new DataPlace(local, false, true, folder);
    }

    /// <summary>
    /// The first line of <paramref name="text"/> with something in it, trimmed; empty for no such line (the program's own
    /// folder); <see langword="null"/> when that line names no folder: over <see cref="MaxLineLength"/> characters, or with
    /// a NUL in it.
    /// </summary>
    public static string? FirstLine(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim().Trim('\uFEFF').Trim();
            if (line.Length == 0)
            {
                continue;
            }

            return raw.Length > MaxLineLength || line.Contains('\0') ? null : line;
        }

        return string.Empty;
    }

    /// <summary>
    /// The full name of the folder <paramref name="line"/> names for a program in <paramref name="programDirectory"/>, by
    /// Windows' rules applied here (so they hold the same in the tests, which run on Linux too): empty or <c>.</c> is the
    /// program's folder; environment variables are expanded, <c>%APPDRIVE%</c> as the program's drive even before it is
    /// published; one pair of quotes around it is taken off; <c>\folder</c> is on the program's drive and a name without a
    /// drive is under the program's folder (<c>data</c>, <c>..\DIG-data</c>). <see langword="null"/> when it is no folder's
    /// name: a variable that is not set, a character Windows refuses in a name, <c>E:data</c>, a full name over
    /// <see cref="MaxFolderLength"/> characters.
    /// </summary>
    public static string? FolderOf(string line, string programDirectory, Func<string, string>? expand = null)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(programDirectory);
        var program = programDirectory.TrimEnd('\\', '/');
        var text = line.Trim();
        if (text.Length >= 2 && text[0] == '"' && text[^1] == '"')
        {
            text = text[1..^1].Trim();
        }

        if (text.Length == 0 || text == ".")
        {
            return WithoutEndingSeparator(programDirectory);
        }

        if (DriveOf(programDirectory) is { } programDrive)
        {
            text = AppDrivePattern().Replace(text, programDrive);
        }

        text = (expand ?? Environment.ExpandEnvironmentVariables)(text).Trim().Replace('/', '\\');
        if (UnsetVariable().IsMatch(text) || text.Any(c => c < ' ' || "\"<>|?*".Contains(c)))
        {
            return null;
        }

        string root;
        string rest;
        if (text.StartsWith(@"\\", StringComparison.Ordinal))
        {
            var parts = text[2..].Split('\\', 3);
            if (parts.Length < 2 || parts[0].Length == 0 || parts[1].Length == 0)
            {
                return null; // \\server alone is no folder
            }

            root = $@"\\{parts[0]}\{parts[1]}";
            rest = parts.Length == 3 ? parts[2] : string.Empty;
        }
        else if (text.Length >= 2 && char.IsAsciiLetter(text[0]) && text[1] == ':')
        {
            if (text.Length > 2 && text[2] != '\\')
            {
                return null; // E:data is relative to a folder Windows keeps per drive and process
            }

            root = char.ToUpperInvariant(text[0]) + ":";
            rest = text.Length > 3 ? text[3..] : string.Empty;
        }
        else if (text[0] == '\\')
        {
            if (DriveOf(programDirectory) is not { } drive)
            {
                return null; // a program on a share has no drive for \folder to be on
            }

            root = drive;
            rest = text[1..];
        }
        else
        {
            return FolderOf(program.Replace('/', '\\') + @"\" + text, programDirectory, static value => value);
        }

        if (rest.Contains(':'))
        {
            return null; // a stream's name, not a folder's
        }

        var segments = new List<string>();
        foreach (var segment in rest.Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                if (segments.Count > 0)
                {
                    segments.RemoveAt(segments.Count - 1); // as Windows does, never above the root
                }

                continue;
            }

            segments.Add(segment);
        }

        var full = segments.Count == 0 ? root + @"\" : root + @"\" + string.Join('\\', segments);
        return full.Length > MaxFolderLength ? null : full;
    }

    /// <summary>
    /// What Settings writes into <c>portable.txt</c> for <paramref name="folder"/> chosen with Browse: <c>.</c> for the
    /// program's folder, a name under it as it is from there (<c>data</c>), any other in full.
    /// </summary>
    public static string AsTyped(string folder, string programDirectory)
    {
        ArgumentNullException.ThrowIfNull(folder);
        var full = FolderOf(folder, programDirectory, static value => value) ?? folder.Trim();
        var program = FolderOf(".", programDirectory, static value => value)!.Replace('/', '\\').TrimEnd('\\');
        if (string.Equals(full.TrimEnd('\\'), program, StringComparison.OrdinalIgnoreCase))
        {
            return ".";
        }

        return full.StartsWith(program + @"\", StringComparison.OrdinalIgnoreCase) ? full[(program.Length + 1)..] : full;
    }

    /// <summary>
    /// <paramref name="path"/> without the separators at its end, either kind, as Windows sees them (so the tests, on
    /// Linux too, see the same); a root keeps its own (<c>E:\</c>).
    /// </summary>
    private static string WithoutEndingSeparator(string path)
    {
        var trimmed = path.TrimEnd('\\', '/');
        return trimmed.Length == 0 || (trimmed.Length == 2 && trimmed[1] == ':') ? path[..Math.Min(path.Length, trimmed.Length + 1)] : trimmed;
    }

    /// <summary>Whether two folder names are the same folder (case and a separator at the end aside).</summary>
    public static bool SameFolder(string? one, string? other) =>
        one is not null && other is not null
        && string.Equals(one.Replace('/', '\\').TrimEnd('\\'), other.Replace('/', '\\').TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

    /// <summary>A line as it may be shown in a message: control characters as spaces, at most 120 characters.</summary>
    private static string Shown(string line)
    {
        var plain = new string([.. line.Select(c => c < ' ' ? ' ' : c)]).Trim();
        return plain.Length > 120 ? plain[..119] + "…" : plain;
    }

    [GeneratedRegex("%APPDRIVE%", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AppDrivePattern();

    /// <summary>A variable left as it was by the expansion: not set on this PC.</summary>
    [GeneratedRegex(@"%[^%\\]+%", RegexOptions.CultureInvariant)]
    private static partial Regex UnsetVariable();

    /// <summary>The start of <c>portable.txt</c> as text (a byte order mark decides the encoding, else UTF-8); none when it cannot be read.</summary>
    private static string? ReadMarker(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var buffer = new byte[MaxMarkerBytes];
            var read = 0;
            int got;
            while (read < buffer.Length && (got = stream.Read(buffer, read, buffer.Length - read)) > 0)
            {
                read += got;
            }

            using var reader = new StreamReader(new MemoryStream(buffer, 0, read), new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true);
            return reader.ReadToEnd();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null; // read as empty: the program's own folder, as before 0.13.0
        }
    }

    /// <summary>Makes <paramref name="directory"/> when it is missing; false when it cannot be made.</summary>
    private static bool MakeFolder(string directory)
    {
        try
        {
            System.IO.Directory.CreateDirectory(directory);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>Whether a file can be made in <paramref name="directory"/>.</summary>
    public static bool CanWriteTo(string directory)
    {
        try
        {
            using (File.Create(Path.Combine(directory, $".write-test-{Guid.NewGuid():N}"), 1, FileOptions.DeleteOnClose))
            {
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>
    /// Whether <paramref name="folder"/> can hold the data: made when it is missing, and written to. What Settings checks
    /// before it writes a new <c>portable.txt</c>.
    /// </summary>
    public static bool CanUse(string folder) => MakeFolder(folder) && CanWriteTo(folder);

    /// <summary>The drive letter of <paramref name="directory"/> as <c>%APPDRIVE%</c> expands (<c>E:</c>); <see langword="null"/> for a UNC path.</summary>
    public static string? DriveOf(string? directory) =>
        directory is { Length: >= 2 } && char.IsAsciiLetter(directory[0]) && directory[1] == ':'
            ? char.ToUpperInvariant(directory[0]) + ":"
            : null;

    /// <summary>
    /// Sets <c>%APPDRIVE%</c> for this process, and so for the programs it starts, to the program's drive. A copy run
    /// from a share has none, and clears the one it may have inherited: started from a stick's bar, it would otherwise
    /// expand <c>%APPDRIVE%</c> as the stick.
    /// </summary>
    public static void PublishProgramDrive() => Environment.SetEnvironmentVariable(AppDriveVariable, DriveOf(AppContext.BaseDirectory));

    /// <summary>
    /// In portable mode, a path on the program's own drive is written with <c>%APPDRIVE%</c>; otherwise, and on Windows'
    /// own drive, as it is (see <see cref="StorageDrive"/>).
    /// </summary>
    public static string ForStorage(string path) =>
        IsPortable ? WithAppDrive(path, StorageDrive(AppContext.BaseDirectory, Environment.GetEnvironmentVariable("SystemDrive"))) : path;

    /// <summary>
    /// The drive paths are written with <c>%APPDRIVE%</c> for: the program's, but not Windows' own drive. A portable copy
    /// run from <c>C:\Tools</c> would otherwise write every C: path that way, <c>C:\Windows\System32\cmd.exe</c> too, and
    /// a copy on a stick that loads them would look for them on the stick.
    /// </summary>
    public static string? StorageDrive(string? programDirectory, string? systemDrive)
    {
        var drive = DriveOf(programDirectory);
        return drive is not null && !string.Equals(drive, DriveOf(systemDrive), StringComparison.OrdinalIgnoreCase) ? drive : null;
    }

    /// <summary>The folders Windows keeps on its own drive. A second disk can have them too (D:\Program Files, Steam libraries).</summary>
    private static readonly string[] WindowsFolders = ["Windows", "Program Files", "Program Files (x86)", "ProgramData", "Users"];

    /// <summary>
    /// A <c>%APPDRIVE%</c> path in one of Windows' own folders, written on <paramref name="systemDrive"/> instead: what
    /// 0.8.0, run from Windows' own drive, meant by it (0.8.1 stores such paths as they are, see <see cref="StorageDrive"/>).
    /// Only a candidate: the same folders on a second disk, or on the stick, are the copy's own, so the caller repairs a
    /// path only when it is missing where <c>%APPDRIVE%</c> points and there on Windows' drive. <paramref name="path"/>
    /// itself for anything else.
    /// </summary>
    public static string? WithWindowsDrive(string? path, string? systemDrive)
    {
        var prefix = $"%{AppDriveVariable}%";
        if (path is null || systemDrive is not { Length: 2 } || !path.TrimStart().StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        var rest = path.TrimStart()[prefix.Length..];
        if (rest.Length < 2 || rest[0] is not ('\\' or '/'))
        {
            return path;
        }

        var folder = rest[1..].Split(['\\', '/'])[0];
        return WindowsFolders.Contains(folder, StringComparer.OrdinalIgnoreCase) ? systemDrive + rest : path;
    }

    /// <summary><paramref name="path"/> with its drive written as <c>%APPDRIVE%</c> when it is on <paramref name="drive"/>.</summary>
    public static string WithAppDrive(string path, string? drive)
    {
        ArgumentNullException.ThrowIfNull(path);
        var trimmed = path.Trim();
        return drive is not null && trimmed.Length >= 3 && string.Equals(trimmed[..2], drive, StringComparison.OrdinalIgnoreCase) && trimmed[2] is '\\' or '/'
            ? $"%{AppDriveVariable}%{trimmed[2..]}"
            : path;
    }
}
