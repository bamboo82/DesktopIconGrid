using DesktopIconGrid.Core.Storage;

namespace DesktopIconGrid.Core.Sync;

/// <summary>Why a folder cannot take the files from the other PCs (<see cref="ReceiveFolder.Check"/>).</summary>
public enum ReceiveFolderProblem
{
    /// <summary>It can.</summary>
    None,

    /// <summary>Not a full name with a drive or a share (<c>files</c>, <c>\files</c>, <c>E:files</c>), or a variable that is not set.</summary>
    NotAbsolute,

    /// <summary>Longer than Windows allows for a folder that files go in (<see cref="ReceiveFolder.MaxLength"/>).</summary>
    TooLong,

    /// <summary>The bar's data folder or a folder in it: the files would mix with the bar's own.</summary>
    InDataFolder,

    /// <summary>The sync folder or a folder in it: a file received there would be sent on to the other PCs.</summary>
    InSyncFolder,

    /// <summary>A file has that name.</summary>
    NotAFolder,

    /// <summary>It is missing and cannot be made, or nothing can be written in it (a pulled-out stick, a read-only share).</summary>
    CannotUse,
}

/// <summary>
/// The folder files from the other PCs are saved in (0.14.0 WP5, <see cref="SyncSettings.ReceiveFolder"/>): Windows'
/// Downloads folder unless this PC's user chose another in Settings. The same rules hold when Settings saves the choice
/// and each time a file arrives; when they fail then, the file goes to Downloads and the choice is kept.
/// </summary>
public static class ReceiveFolder
{
    /// <summary>
    /// The longest full name of a folder that files go in, as Windows allows without long paths: 260 for a file's full
    /// name, less 12 for the shortest file name and its separator.
    /// </summary>
    public const int MaxLength = 248;

    /// <summary>
    /// The folder <paramref name="setting"/> names, as it is used: one pair of quotes taken off, environment variables
    /// expanded (<c>%APPDRIVE%</c> as the program's drive, which the program sets for itself), and either separator
    /// written as <c>\</c> where Windows' names are written so. <see langword="null"/> for no setting: Downloads.
    /// </summary>
    public static string? Expand(string? setting, Func<string, string>? expand = null)
    {
        var text = setting?.Trim() ?? string.Empty;
        if (text.Length >= 2 && text[0] == '"' && text[^1] == '"')
        {
            text = text[1..^1].Trim();
        }

        return text.Length == 0 ? null : (expand ?? Environment.ExpandEnvironmentVariables)(text).Trim();
    }

    /// <summary>
    /// The rules a name must keep, without looking at the disk: a full name, within <see cref="MaxLength"/>, neither in the
    /// bar's data folder nor in the sync folder.
    /// </summary>
    public static ReceiveFolderProblem CheckName(string folder, string dataDirectory, string? syncFolder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        if (!IsFullName(folder) || folder.Contains('%'))
        {
            return ReceiveFolderProblem.NotAbsolute; // a % left after the expansion is a variable not set here
        }

        if (folder.TrimEnd('\\', '/').Length > MaxLength)
        {
            return ReceiveFolderProblem.TooLong;
        }

        if (IsWithin(folder, dataDirectory))
        {
            return ReceiveFolderProblem.InDataFolder;
        }

        return syncFolder is not null && IsWithin(folder, syncFolder) ? ReceiveFolderProblem.InSyncFolder : ReceiveFolderProblem.None;
    }

    /// <summary>
    /// All the rules: <see cref="CheckName"/>, then on the disk a folder that is there or can be made (it is made) and can
    /// be written in.
    /// </summary>
    public static ReceiveFolderProblem Check(string folder, string dataDirectory, string? syncFolder)
    {
        var problem = CheckName(folder, dataDirectory, syncFolder);
        if (problem != ReceiveFolderProblem.None)
        {
            return problem;
        }

        if (File.Exists(folder))
        {
            return ReceiveFolderProblem.NotAFolder;
        }

        return DataLocation.CanUse(folder) ? ReceiveFolderProblem.None : ReceiveFolderProblem.CannotUse;
    }

    /// <summary>
    /// A full name, by Windows' rules written out here so they hold the same in the tests on Linux: <c>C:\…</c> or
    /// <c>\\server\share\…</c> (and on the system the program runs on, its own full names).
    /// </summary>
    internal static bool IsFullName(string folder)
    {
        var name = folder.Replace('/', '\\');
        var windows = (name.Length >= 3 && char.IsAsciiLetter(name[0]) && name[1] == ':' && name[2] == '\\')
            || (name.StartsWith(@"\\", StringComparison.Ordinal) && name.Length > 2 && name[2] != '\\' && name.IndexOf('\\', 2) > 2);
        return windows || (!OperatingSystem.IsWindows() && Path.IsPathFullyQualified(folder));
    }

    /// <summary>Whether <paramref name="folder"/> is <paramref name="outer"/> or a folder in it (case and separators aside).</summary>
    internal static bool IsWithin(string folder, string outer)
    {
        if (string.IsNullOrWhiteSpace(outer))
        {
            return false;
        }

        var inner = folder.Replace('/', '\\').TrimEnd('\\');
        var around = outer.Replace('/', '\\').TrimEnd('\\');
        return string.Equals(inner, around, StringComparison.OrdinalIgnoreCase)
            || inner.StartsWith(around + "\\", StringComparison.OrdinalIgnoreCase);
    }
}
