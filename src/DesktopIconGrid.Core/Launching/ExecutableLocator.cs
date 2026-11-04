namespace DesktopIconGrid.Core.Launching;

/// <summary>
/// Finds executables the way the shell would: absolute paths, then <c>PATH</c>, plus the Windows app-alias
/// folder. A name without an extension also matches <c>.exe</c> and <c>.com</c> (the start of <c>PATHEXT</c>).
/// </summary>
public static class ExecutableLocator
{
    private static readonly string[] DefaultExtensions = [".exe", ".com"];

    public static bool Exists(string executable) => Find(executable) is not null;

    public static string? Find(string executable)
    {
        if (string.IsNullOrWhiteSpace(executable))
        {
            return null;
        }

        var expanded = Environment.ExpandEnvironmentVariables(executable.Trim());
        if (Path.IsPathRooted(expanded))
        {
            return FirstExisting(expanded);
        }

        foreach (var directory in SearchDirectories())
        {
            var found = FirstExisting(Path.Combine(directory, expanded));
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>The candidate itself, or the candidate with a default executable extension when it has none.</summary>
    private static string? FirstExisting(string candidate)
    {
        if (File.Exists(candidate))
        {
            return candidate;
        }

        if (Path.GetExtension(candidate).Length == 0)
        {
            foreach (var extension in DefaultExtensions)
            {
                if (File.Exists(candidate + extension))
                {
                    return candidate + extension;
                }
            }
        }

        return null;
    }

    private static IEnumerable<string> SearchDirectories()
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            yield return directory;
        }

        if (OperatingSystem.IsWindows())
        {
            yield return Environment.GetFolderPath(Environment.SpecialFolder.System);
            yield return Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            yield return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify),
                "Microsoft", "WindowsApps");
        }
    }
}
