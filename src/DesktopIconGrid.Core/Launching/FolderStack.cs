namespace DesktopIconGrid.Core.Launching;

/// <summary>One file or folder in a folder stack.</summary>
public sealed record StackEntry(string Path, string Name, bool IsFolder, DateTime Modified);

/// <summary>
/// The contents of a folder shortcut that opens as a stack (a small list by the icon instead of an Explorer window):
/// what is directly in the folder, without hidden and system entries, folders first when sorted by name.
/// </summary>
public static class FolderStack
{
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
                if ((info.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0 || info.Name.StartsWith('.'))
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
        return (sorted.Take(Math.Max(0, max)).ToList(), entries.Count);
    }
}
