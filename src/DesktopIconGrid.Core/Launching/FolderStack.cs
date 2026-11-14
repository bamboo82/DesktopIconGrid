namespace DesktopIconGrid.Core.Launching;

/// <summary>One file or folder in a folder stack.</summary>
/// <param name="LinksToFolder">A shortcut file (<c>.lnk</c>) whose target was a folder when the shortcut was made.</param>
public sealed record StackEntry(string Path, string Name, bool IsFolder, DateTime Modified, bool LinksToFolder = false);

/// <summary>
/// The contents of a folder shortcut that opens as a stack (a small list by the icon instead of an Explorer window):
/// what is directly in the folder, folders first when sorted by name. Hidden and system entries are left out, as Explorer
/// leaves them out by default; a name that starts with a dot is an ordinary file on Windows and is listed, as Explorer
/// lists it, so "N more" counts what Explorer shows (review 24). (.NET on Linux and macOS reports dot files as hidden.)
/// </summary>
public static class FolderStack
{
    private static readonly HashSet<string> ProgramExtensions = new(StringComparer.OrdinalIgnoreCase) { ".exe", ".com", ".bat", ".cmd", ".lnk" };

    /// <summary>
    /// Whether a file dragged inside the stack may be dropped onto <paramref name="entry"/> to be opened with it: a
    /// program, a batch file or a shortcut, and not the dragged file itself. A shortcut to a folder opens a window, not
    /// files, and takes no drop (review 27).
    /// </summary>
    public static bool TakesDrop(StackEntry entry, IReadOnlyList<string> dragged)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(dragged);
        return !entry.IsFolder
            && !entry.LinksToFolder
            && dragged.Count > 0
            && ProgramExtensions.Contains(Path.GetExtension(entry.Path))
            && !dragged.Any(path => string.Equals(path, entry.Path, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The most entries a stack shows; the rest is one click away in Explorer.</summary>
    public const int MaxEntries = 100;

    /// <summary>The total <see cref="List"/> reports for a folder it could not read.</summary>
    public const int Unreadable = -1;

    /// <summary>
    /// The first <paramref name="max"/> entries of <paramref name="folder"/>, by name (folders first) or newest first, and
    /// how many there are in all. When the folder cannot be read (gone, a share that does not answer, no access) the
    /// list is empty and the total is <see cref="Unreadable"/>, so "empty" and "could not be read" are told apart.
    /// </summary>
    public static (IReadOnlyList<StackEntry> Entries, int Total) List(string folder, bool newestFirst, int max = MaxEntries)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        var entries = new List<StackEntry>();
        try
        {
            foreach (var info in new DirectoryInfo(folder).EnumerateFileSystemInfos())
            {
                if ((info.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0)
                {
                    continue;
                }

                var isFolder = (info.Attributes & FileAttributes.Directory) != 0;
                var name = isFolder || !info.Name.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ? info.Name : info.Name[..^4];
                entries.Add(new StackEntry(info.FullName, name, isFolder, info.LastWriteTime));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return ([], Unreadable);
        }

        var sorted = newestFirst
            ? entries.OrderByDescending(entry => entry.Modified).ThenBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase)
            : entries.OrderByDescending(entry => entry.IsFolder).ThenBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase);
        var shown = sorted.Take(Math.Max(0, max))
                          .Select(entry => !entry.IsFolder && IsLink(entry.Path) && LinkPointsToFolder(entry.Path) ? entry with { LinksToFolder = true } : entry)
                          .ToList();
        return (shown, entries.Count);
    }

    private static bool IsLink(string path) => path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase);

    /// <summary>The shell link's class id, which every <c>.lnk</c> file carries after its header size.</summary>
    private static readonly Guid ShellLinkClassId = new("00021401-0000-0000-c000-000000000046");

    /// <summary>
    /// Whether a shortcut file says its target is a folder: the header of a <c>.lnk</c> file (MS-SHLLINK 2.1) keeps the
    /// target's attributes from when the shortcut was made, so the target itself, perhaps on a share that sleeps, is not
    /// touched. A file that is not a shell link, or cannot be read, reads as not a folder.
    /// </summary>
    internal static bool LinkPointsToFolder(string path)
    {
        const int HeaderSize = 0x4C;
        const int Directory = 0x10; // FILE_ATTRIBUTE_DIRECTORY
        try
        {
            Span<byte> header = stackalloc byte[28];
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, bufferSize: 1);
            if (stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) < header.Length)
            {
                return false;
            }

            return BitConverter.ToInt32(header[..4]) == HeaderSize
                && new Guid(header.Slice(4, 16)) == ShellLinkClassId
                && (BitConverter.ToInt32(header.Slice(24, 4)) & Directory) != 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return false;
        }
    }
}
