namespace DesktopIconGrid.Core.Launching;

/// <summary>Finds executables the way the shell would: absolute paths, then <c>PATH</c>, plus the Windows app-alias folder.</summary>
public static class ExecutableLocator
{
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
            return File.Exists(expanded) ? expanded : null;
        }

        foreach (var directory in SearchDirectories())
        {
            var candidate = Path.Combine(directory, expanded);
            if (File.Exists(candidate))
            {
                return candidate;
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
