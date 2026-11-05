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
    public void Executables_are_watched_by_path_package_folders_by_prefix_and_packages_by_family()
    {
        var exe = Path.Combine(_directory, "tool.exe");
        File.WriteAllBytes(exe, []);

        var target = ProcessTracker.WatchTargetOf(exe);
        Assert.NotNull(target);
        Assert.Equal(exe, target.File, ignoreCase: true);
        Assert.True(target.Matches(exe.ToUpperInvariant()));
        Assert.False(target.Matches(Path.Combine(_directory, "other.exe")));

        var folder = new ProcessTracker.WatchTarget(Folder: @"C:\Program Files\WindowsApps\Pkg_1.0_x64__abc");
        Assert.True(folder.Matches(@"c:\program files\windowsapps\pkg_1.0_x64__abc\WindowsTerminal.exe"));
        Assert.False(folder.Matches(@"C:\Program Files\WindowsApps\Pkg_1.0_x64__abcdef\x.exe"));

        var store = ProcessTracker.WatchTargetOf(@"shell:AppsFolder\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App");
        Assert.NotNull(store);
        Assert.Equal("Microsoft.WindowsCalculator_8wekyb3d8bbwe", store.PackageFamily);
        Assert.True(store.Matches(@"C:\Program Files\WindowsApps\Microsoft.WindowsCalculator_11.2405.2.0_x64__8wekyb3d8bbwe\CalculatorApp.exe"));
        Assert.Null(ProcessTracker.WatchTargetOf(@"shell:AppsFolder\{6D809377-6AF0-444B-8957-A3773F02200E}\Tool\tool.exe"));
    }

    [Fact]
    public void A_shortcut_s_processes_come_from_the_scan_of_its_program()
    {
        using var tracker = new ProcessTracker(System.Windows.Threading.Dispatcher.CurrentDispatcher, Path.Combine(_directory, "logs"));
        var exe = Path.Combine(_directory, "tool.exe");
        File.WriteAllBytes(exe, []);
        var shortcut = new DesktopIconGrid.Core.Models.ShortcutItem { Location = exe };
        var changed = new List<Guid>();
        tracker.RunningChanged += (_, id) => changed.Add(id);
        tracker.Watch(shortcut);

        tracker.ApplySnapshot(new Dictionary<string, IReadOnlyList<int>>(StringComparer.OrdinalIgnoreCase)
        {
            [exe.ToUpperInvariant()] = [101, 102],
            [Path.Combine(_directory, "other.exe")] = [103],
        });

        Assert.True(tracker.IsRunning(shortcut.Id));
        Assert.Equal([101, 102], tracker.ProcessIdsOf(shortcut.Id).Order());
        Assert.Equal([shortcut.Id], changed);

        tracker.ApplySnapshot(new Dictionary<string, IReadOnlyList<int>>(StringComparer.OrdinalIgnoreCase));
        Assert.False(tracker.IsRunning(shortcut.Id));
        Assert.Empty(tracker.ProcessIdsOf(shortcut.Id));
        Assert.Equal([shortcut.Id, shortcut.Id], changed);
        Assert.Empty(tracker.ProcessIdsOf(Guid.NewGuid()));
    }
}
