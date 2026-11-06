using System.Text.RegularExpressions;

namespace DesktopIconGrid.Core.Launching;

/// <summary>Why a path is offered by <see cref="LocationRepair"/>.</summary>
public enum RepairReason
{
    /// <summary>The same program in a folder that differs only by its version (<c>app-1.0.9172</c> → <c>app-1.0.9203</c>).</summary>
    OtherVersion,

    /// <summary>Registered with Windows under <c>App Paths</c>.</summary>
    Registered,

    /// <summary>The same file name in a folder for installed programs.</summary>
    InstalledProgram,
}

/// <summary>
/// A place a missing shortcut target may have gone. <paramref name="Likely"/> when more than its file name matches:
/// another version of its folder, its registration with Windows, or a folder of the same name as the one it was in.
/// Only a likely one may be chosen for the user; a match on the file name alone may be another program
/// (<c>launcher.exe</c>).
/// </summary>
public sealed record RepairCandidate(string Path, RepairReason Reason, bool Likely = true);

/// <summary>What <see cref="LocationRepair.Search"/> found, and whether it ran out of folders it was allowed to look in.</summary>
public sealed record RepairSearch(IReadOnlyList<RepairCandidate> Candidates, bool StoppedEarly);

/// <summary>
/// Proposes where a shortcut's missing program is on this PC: the same file in a sibling folder that differs only by
/// a version number (newest first), the program's <c>App Paths</c> entry, a folder of the same name in each folder for
/// installed programs (Program Files, Program Files (x86), <c>%LocalAppData%\Programs</c>), and then the same file name
/// a few levels deep in each of them. Each program folder has its own cap on the folders looked at, so the search
/// never runs away on a big disk and one big folder cannot leave the others unsearched. The Store's package folder
/// (<c>WindowsApps</c>) is searched only for the package the program came from.
/// </summary>
public static partial class LocationRepair
{
    /// <summary>How deep under a program folder the file name is looked for (<c>Root\Vendor\App\bin\app.exe</c> is 3).</summary>
    public const int SearchDepth = 3;

    /// <summary>The most folders looked at under each program folder.</summary>
    public const int MaxFolders = 5000;

    private const string StoreFolderName = "WindowsApps";

    /// <summary>How many folders up a version number is looked for (<c>app-1.2\bin\app.exe</c> is 2).</summary>
    private const int VersionLevels = 3;

    /// <param name="location">The shortcut's location on this PC (environment variables are expanded).</param>
    /// <param name="programFolders">Where installed programs live, in order of preference.</param>
    /// <param name="registeredPathOf">The <c>App Paths</c> entry for a file name, or <see langword="null"/>.</param>
    public static IReadOnlyList<RepairCandidate> Candidates(
        string location, IEnumerable<string> programFolders, Func<string, string?>? registeredPathOf = null, CancellationToken cancellationToken = default) =>
        Search(location, programFolders, registeredPathOf, cancellationToken).Candidates;

    /// <summary>
    /// The candidates, the likely ones first (see <see cref="RepairCandidate.Likely"/>), and whether a program folder
    /// held more folders than <paramref name="maxFoldersPerRoot"/>, so the program may be there unseen.
    /// </summary>
    public static RepairSearch Search(
        string location,
        IEnumerable<string> programFolders,
        Func<string, string?>? registeredPathOf = null,
        CancellationToken cancellationToken = default,
        int maxFoldersPerRoot = MaxFolders)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(programFolders);
        var path = Environment.ExpandEnvironmentVariables(location.Trim().Trim('"')).TrimEnd('\\', '/');
        var parts = path.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            return new RepairSearch([], StoppedEarly: false);
        }

        var fileName = parts[^1];
        var parentName = parts[^2];
        var found = new List<RepairCandidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { System.IO.Path.GetFullPath(path) };
        void Add(string candidate, RepairReason reason, bool likely)
        {
            if (File.Exists(candidate) && seen.Add(System.IO.Path.GetFullPath(candidate)))
            {
                found.Add(new RepairCandidate(candidate, reason, likely));
            }
        }

        foreach (var candidate in OtherVersions(path, parts))
        {
            Add(candidate, RepairReason.OtherVersion, likely: true);
        }

        if (registeredPathOf?.Invoke(fileName) is { } registered)
        {
            Add(registered, RepairReason.Registered, likely: true);
        }

        var roots = programFolders.Where(f => !string.IsNullOrWhiteSpace(f) && Directory.Exists(f)).ToList();
        foreach (var root in roots)
        {
            Add(System.IO.Path.Combine(root, parentName, fileName), RepairReason.InstalledProgram, likely: true); // the likely spots first, in every root
        }

        var package = StorePackageOf(parts);
        var stoppedEarly = false;
        foreach (var root in roots)
        {
            var budget = maxFoldersPerRoot;
            foreach (var candidate in Search(root, fileName, package, ref budget, cancellationToken))
            {
                var folder = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(candidate));
                Add(candidate, RepairReason.InstalledProgram, likely: string.Equals(folder, parentName, StringComparison.OrdinalIgnoreCase));
            }

            stoppedEarly |= budget < 0;
        }

        return new RepairSearch([.. found.Where(c => c.Likely), .. found.Where(c => !c.Likely)], stoppedEarly);
    }

    /// <summary>The Store package folder name (<c>Name_Version_Arch__Publisher</c>) a path lies in, or <see langword="null"/>.</summary>
    private static (string Name, string Publisher)? StorePackageOf(string[] parts)
    {
        for (var i = 0; i < parts.Length - 1; i++)
        {
            if (parts[i].Equals(StoreFolderName, StringComparison.OrdinalIgnoreCase))
            {
                var fields = parts[i + 1].Split('_');
                return fields.Length >= 3 ? (fields[0], fields[^1]) : null;
            }
        }

        return null;
    }

    /// <summary>
    /// For each of the last few folders on the path that carries a version number, the siblings whose names differ only
    /// in their numbers, with the rest of the path appended; the highest version first.
    /// </summary>
    private static IEnumerable<string> OtherVersions(string path, string[] parts)
    {
        var results = new List<(string Path, long[] Version)>();
        var separator = path.Contains('\\', StringComparison.Ordinal) ? '\\' : '/';
        for (var level = 1; level <= VersionLevels && parts.Length - 1 - level >= 1; level++)
        {
            var folderIndex = parts.Length - 1 - level;
            var folderName = parts[folderIndex];
            if (!Digits().IsMatch(folderName))
            {
                continue;
            }

            var parent = string.Join(separator, parts[..folderIndex]);
            if (path.StartsWith(@"\\", StringComparison.Ordinal))
            {
                parent = @"\\" + parent; // a UNC path keeps its two leading backslashes
            }
            else if (path.StartsWith(separator) && !parent.StartsWith(separator))
            {
                parent = separator + parent; // a rooted Unix path keeps its leading slash
            }

            if (parent.Length == 2 && parent[1] == ':')
            {
                parent += separator; // C: alone means "the current folder on C:"
            }

            IEnumerable<string> siblings;
            try
            {
                siblings = Directory.EnumerateDirectories(parent).ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                continue;
            }

            var pattern = Mask(folderName);
            var rest = parts[(folderIndex + 1)..];
            foreach (var sibling in siblings)
            {
                var name = System.IO.Path.GetFileName(sibling);
                if (!string.Equals(name, folderName, StringComparison.OrdinalIgnoreCase) && string.Equals(Mask(name), pattern, StringComparison.OrdinalIgnoreCase))
                {
                    results.Add((System.IO.Path.Combine([sibling, .. rest]), Version(name)));
                }
            }
        }

        return results.OrderByDescending(r => r.Version, VersionComparer.Instance).Select(r => r.Path);
    }

    /// <summary>
    /// Breadth-first, up to <see cref="SearchDepth"/> levels, while folders are left in the budget (it ends below zero
    /// when they ran out). <c>WindowsApps</c> is entered only for folders of <paramref name="package"/>: it holds hundreds
    /// of packages, and only one of them can have been the program's.
    /// </summary>
    private static List<string> Search(string root, string fileName, (string Name, string Publisher)? package, ref int budget, CancellationToken cancellationToken)
    {
        var results = new List<string>();
        var level = new List<string> { root };
        for (var depth = 1; depth <= SearchDepth && level.Count > 0 && budget > 0; depth++)
        {
            var next = new List<string>();
            foreach (var folder in level)
            {
                cancellationToken.ThrowIfCancellationRequested();
                IEnumerable<string> children;
                try
                {
                    children = Directory.EnumerateDirectories(folder).ToList();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    continue;
                }

                var inStoreFolder = System.IO.Path.GetFileName(folder).Equals(StoreFolderName, StringComparison.OrdinalIgnoreCase);
                foreach (var child in children)
                {
                    var name = System.IO.Path.GetFileName(child);
                    if (name.Equals(StoreFolderName, StringComparison.OrdinalIgnoreCase) && package is null)
                    {
                        continue; // not a Store program: none of the packages is it
                    }

                    if (inStoreFolder
                        && (package is not { } p
                            || !name.StartsWith(p.Name + "_", StringComparison.OrdinalIgnoreCase)
                            || !name.EndsWith("_" + p.Publisher, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue; // another package
                    }

                    if (--budget < 0)
                    {
                        return results;
                    }

                    var candidate = System.IO.Path.Combine(child, fileName);
                    if (File.Exists(candidate))
                    {
                        results.Add(candidate);
                    }

                    next.Add(child);
                }
            }

            level = next;
        }

        return results;
    }

    /// <summary>The name with every run of digits replaced, so <c>app-1.0.9172</c> and <c>app-1.0.9203</c> look alike.</summary>
    private static string Mask(string name) => Digits().Replace(name, "#");

    private static long[] Version(string name) =>
        Digits().Matches(name).Select(m => long.TryParse(m.Value, out var n) ? n : 0).ToArray();

    [GeneratedRegex("[0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex Digits();

    private sealed class VersionComparer : IComparer<long[]>
    {
        public static readonly VersionComparer Instance = new();

        public int Compare(long[]? x, long[]? y)
        {
            x ??= [];
            y ??= [];
            for (var i = 0; i < Math.Max(x.Length, y.Length); i++)
            {
                var a = i < x.Length ? x[i] : 0;
                var b = i < y.Length ? y[i] : 0;
                if (a != b)
                {
                    return a.CompareTo(b);
                }
            }

            return 0;
        }
    }
}
