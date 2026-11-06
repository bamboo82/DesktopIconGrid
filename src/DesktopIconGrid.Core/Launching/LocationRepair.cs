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

/// <summary>A place a missing shortcut target may have gone.</summary>
public sealed record RepairCandidate(string Path, RepairReason Reason);

/// <summary>
/// Proposes where a shortcut's missing program is on this PC: the same file in a sibling folder that differs only by
/// a version number (newest first), the program's <c>App Paths</c> entry, and the same file name in the folders for
/// installed programs (Program Files, Program Files (x86), <c>%LocalAppData%\Programs</c>), searched a few levels deep
/// with a cap on the folders looked at, so the search never runs away on a big disk.
/// </summary>
public static partial class LocationRepair
{
    /// <summary>How deep under a program folder the file name is looked for (<c>Root\Vendor\App\bin\app.exe</c> is 3).</summary>
    public const int SearchDepth = 3;

    /// <summary>The most folders looked at under all program folders together.</summary>
    public const int MaxFolders = 5000;

    /// <summary>How many folders up a version number is looked for (<c>app-1.2\bin\app.exe</c> is 2).</summary>
    private const int VersionLevels = 3;

    /// <param name="location">The shortcut's location on this PC (environment variables are expanded).</param>
    /// <param name="programFolders">Where installed programs live, in order of preference.</param>
    /// <param name="registeredPathOf">The <c>App Paths</c> entry for a file name, or <see langword="null"/>.</param>
    public static IReadOnlyList<RepairCandidate> Candidates(
        string location, IEnumerable<string> programFolders, Func<string, string?>? registeredPathOf = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(programFolders);
        var path = Environment.ExpandEnvironmentVariables(location.Trim().Trim('"')).TrimEnd('\\', '/');
        var parts = path.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            return [];
        }

        var fileName = parts[^1];
        var found = new List<RepairCandidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { System.IO.Path.GetFullPath(path) };
        void Add(string candidate, RepairReason reason)
        {
            if (File.Exists(candidate) && seen.Add(System.IO.Path.GetFullPath(candidate)))
            {
                found.Add(new RepairCandidate(candidate, reason));
            }
        }

        foreach (var candidate in OtherVersions(path, parts))
        {
            Add(candidate, RepairReason.OtherVersion);
        }

        if (registeredPathOf?.Invoke(fileName) is { } registered)
        {
            Add(registered, RepairReason.Registered);
        }

        var parentName = parts.Length >= 2 ? parts[^2] : null;
        var budget = MaxFolders;
        foreach (var root in programFolders.Where(f => !string.IsNullOrWhiteSpace(f) && Directory.Exists(f)))
        {
            if (parentName is not null)
            {
                Add(System.IO.Path.Combine(root, parentName, fileName), RepairReason.InstalledProgram); // the likely spot first
            }

            foreach (var candidate in Search(root, fileName, ref budget, cancellationToken))
            {
                Add(candidate, RepairReason.InstalledProgram);
            }
        }

        return found;
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

    /// <summary>Breadth-first, up to <see cref="SearchDepth"/> levels, while folders are left in the budget.</summary>
    private static List<string> Search(string root, string fileName, ref int budget, CancellationToken cancellationToken)
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

                foreach (var child in children)
                {
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
