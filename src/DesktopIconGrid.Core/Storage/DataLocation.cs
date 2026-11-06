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

    private static readonly Lazy<(string Directory, bool Portable)> Here = new(() => Resolve(
        AppContext.BaseDirectory,
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify),
        File.Exists));

    /// <summary>The folder for <c>layout.json</c>, <c>sync.json</c>, logs and caches.</summary>
    public static string Directory => Here.Value.Directory;

    public static bool IsPortable => Here.Value.Portable;

    /// <summary>The data folder for a program in <paramref name="programDirectory"/>, and whether that is portable mode.</summary>
    public static (string Directory, bool Portable) Resolve(string programDirectory, string localAppData, Func<string, bool> fileExists)
    {
        ArgumentNullException.ThrowIfNull(fileExists);
        if (!string.IsNullOrWhiteSpace(programDirectory) && fileExists(Path.Combine(programDirectory, PortableMarker)))
        {
            return (Path.TrimEndingDirectorySeparator(programDirectory), true);
        }

        return (Path.Combine(localAppData ?? string.Empty, LayoutStorage.ApplicationFolderName), false);
    }

    /// <summary>The drive letter of <paramref name="directory"/> as <c>%APPDRIVE%</c> expands (<c>E:</c>); <see langword="null"/> for a UNC path.</summary>
    public static string? DriveOf(string? directory) =>
        directory is { Length: >= 2 } && char.IsAsciiLetter(directory[0]) && directory[1] == ':'
            ? char.ToUpperInvariant(directory[0]) + ":"
            : null;

    /// <summary>Sets <c>%APPDRIVE%</c> for this process, and so for the programs it starts, to the program's drive.</summary>
    public static void PublishProgramDrive()
    {
        if (DriveOf(AppContext.BaseDirectory) is { } drive)
        {
            Environment.SetEnvironmentVariable(AppDriveVariable, drive);
        }
    }

    /// <summary>In portable mode, a path on the program's own drive is written with <c>%APPDRIVE%</c>; otherwise as it is.</summary>
    public static string ForStorage(string path) => IsPortable ? WithAppDrive(path, DriveOf(AppContext.BaseDirectory)) : path;

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
