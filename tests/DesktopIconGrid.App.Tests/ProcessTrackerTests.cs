using System.IO;
using DesktopIconGrid.Services;

namespace DesktopIconGrid.App.Tests;

public sealed class ProcessTrackerTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("dig-tracker-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void Executables_resolve_to_their_full_path_with_a_default_extension()
    {
        var exe = Path.Combine(_directory, "tool.exe");
        File.WriteAllBytes(exe, []);

        Assert.Equal(exe, ProcessTracker.ExecutablePathOf(exe), ignoreCase: true);
        Assert.Equal(exe, ProcessTracker.ExecutablePathOf(Path.Combine(_directory, "tool")), ignoreCase: true);
        Assert.Equal(exe, ProcessTracker.ExecutablePathOf($"  {exe}  "), ignoreCase: true);
    }

    [Theory]
    [InlineData("")]
    [InlineData("https://example.com")]
    [InlineData("shell:Desktop")]
    [InlineData("special:show-desktop")]
    public void Links_shell_locations_and_specials_are_not_matched(string location) =>
        Assert.Null(ProcessTracker.ExecutablePathOf(location));

    [Fact]
    public void Folders_documents_and_missing_programs_are_not_matched()
    {
        File.WriteAllText(Path.Combine(_directory, "notes.txt"), "x");

        Assert.Null(ProcessTracker.ExecutablePathOf(_directory));
        Assert.Null(ProcessTracker.ExecutablePathOf(Path.Combine(_directory, "notes.txt")));
        Assert.Null(ProcessTracker.ExecutablePathOf(Path.Combine(_directory, "missing.exe")));
        Assert.Null(ProcessTracker.ExecutablePathOf(Path.Combine(_directory, "missing")));
    }

    [Fact]
    public void The_shell_itself_is_never_matched()
    {
        var explorer = Path.Combine(_directory, "explorer.exe");
        File.WriteAllBytes(explorer, []);

        Assert.Null(ProcessTracker.ExecutablePathOf(explorer));
        Assert.Null(ProcessTracker.ExecutablePathOf("explorer.exe"));
    }

    [Fact]
    public void App_execution_aliases_fall_back_to_launched_process_tracking()
    {
        var aliases = Path.Combine(_directory, "WindowsApps");
        Directory.CreateDirectory(aliases);
        var alias = Path.Combine(aliases, "wt.exe");
        File.WriteAllBytes(alias, []);
        var previous = ProcessTracker.AppAliasDirectory;
        ProcessTracker.AppAliasDirectory = aliases;
        try
        {
            Assert.Null(ProcessTracker.ExecutablePathOf(alias));
            Assert.Null(ProcessTracker.ExecutablePathOf(Path.Combine(aliases, "wt")));
            Assert.Null(ProcessTracker.WatchTargetOf(alias)); // a plain file, not a reparse point: nothing to resolve
        }
        finally
        {
            ProcessTracker.AppAliasDirectory = previous;
        }
    }

    [Fact]
    public void Executables_are_watched_by_path_and_package_folders_by_prefix()
    {
        var exe = Path.Combine(_directory, "tool.exe");
        File.WriteAllBytes(exe, []);

        var target = ProcessTracker.WatchTargetOf(exe);
        Assert.NotNull(target);
        Assert.False(target.IsFolder);
        Assert.True(target.Matches(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { exe.ToUpperInvariant() }));
        Assert.False(target.Matches(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Path.Combine(_directory, "other.exe") }));

        var package = new ProcessTracker.WatchTarget(@"C:\Program Files\WindowsApps\Pkg_1.0_x64__abc", IsFolder: true);
        Assert.True(package.Matches(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"c:\program files\windowsapps\pkg_1.0_x64__abc\WindowsTerminal.exe" }));
        Assert.False(package.Matches(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"C:\Program Files\WindowsApps\Pkg_1.0_x64__abcdef\x.exe" }));
        Assert.False(package.Matches(new HashSet<string>(StringComparer.OrdinalIgnoreCase)));
    }
}
