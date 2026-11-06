namespace DesktopIconGrid.Core.Storage;

/// <summary>
/// Where the app keeps its files: <c>%LocalAppData%\DesktopIconGrid</c>, or, in portable mode, the program's own folder
/// when a file called <c>portable.txt</c> sits next to the program, so a tools drive carries the bar from PC to PC.
/// <c>%APPDRIVE%</c> names the drive the program runs from, so shortcuts on that drive keep working when its letter
/// changes.
/// </summary>
public static class DataLocation
{
    /// <summary>The file next to the program that turns portable mode on.</summary>
    public const string PortableMarker = "portable.txt";

    /// <summary>The environment variable set to the program's drive (<c>E:</c>).</summary>
    public const string AppDriveVariable = "APPDRIVE";

    private static readonly Lazy<(string Directory, bool Portable, bool ReadOnlyPortable)> Here = new(() => Resolve(
        AppContext.BaseDirectory,
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify),
        File.Exists,
        CanWriteTo));

    /// <summary>The folder for <c>layout.json</c>, <c>sync.json</c>, logs and caches.</summary>
    public static string Directory => Here.Value.Directory;

    public static bool IsPortable => Here.Value.Portable;

    /// <summary>
    /// <c>portable.txt</c> is next to the program, but its folder cannot be written (under Program Files without
    /// elevation, on read-only media), so the data is in <c>%LocalAppData%</c> after all; the app says so.
    /// </summary>
    public static bool PortableFolderIsReadOnly => Here.Value.ReadOnlyPortable;

    /// <summary>
    /// <c>portable.txt</c> is next to the program, whether or not its data could stay there: the copy travels, and
    /// asks before it adds a Run entry on a PC it visits.
    /// </summary>
    public static bool IsPortableCopy => IsPortable || PortableFolderIsReadOnly;

    /// <summary>
    /// The data folder for a program in <paramref name="programDirectory"/>, whether that is portable mode, and whether
    /// portable mode was asked for in a folder <paramref name="canWrite"/> says cannot be written (the data then goes to
    /// <c>%LocalAppData%</c>, as without <c>portable.txt</c>, instead of stopping the app).
    /// </summary>
    public static (string Directory, bool Portable, bool ReadOnlyPortable) Resolve(
        string programDirectory, string localAppData, Func<string, bool> fileExists, Func<string, bool>? canWrite = null)
    {
        ArgumentNullException.ThrowIfNull(fileExists);
        var local = Path.Combine(localAppData ?? string.Empty, LayoutStorage.ApplicationFolderName);
        if (!string.IsNullOrWhiteSpace(programDirectory) && fileExists(Path.Combine(programDirectory, PortableMarker)))
        {
            var portable = Path.TrimEndingDirectorySeparator(programDirectory);
            return canWrite is null || canWrite(portable) ? (portable, true, false) : (local, false, true);
        }

        return (local, false, false);
    }

    /// <summary>Whether a file can be made in <paramref name="directory"/>.</summary>
    private static bool CanWriteTo(string directory)
    {
        try
        {
            using (File.Create(Path.Combine(directory, $".write-test-{Guid.NewGuid():N}"), 1, FileOptions.DeleteOnClose))
            {
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

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
