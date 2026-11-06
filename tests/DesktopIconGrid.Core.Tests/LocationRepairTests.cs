using DesktopIconGrid.Core.Launching;
using DesktopIconGrid.Core.Models;

namespace DesktopIconGrid.Core.Tests;

/// <summary>Where a shortcut's program went on this PC, and paths of its own per PC.</summary>
public sealed class LocationRepairTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("dig-repair-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Make(params string[] parts)
    {
        var path = Path.Combine([_root, .. parts]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, []);
        return path;
    }

    private string Folder(params string[] parts) => Path.Combine([_root, .. parts]);

    [Fact]
    public void A_folder_that_differs_only_by_its_version_is_offered_newest_first()
    {
        var newest = Make("apps", "app-1.0.9203", "App.exe");
        var older = Make("apps", "app-1.0.9180", "App.exe");
        Make("apps", "other-1.0", "App.exe"); // another program
        Directory.CreateDirectory(Folder("apps", "app-1.0.9300")); // no App.exe in it

        var found = LocationRepair.Candidates(Folder("apps", "app-1.0.9172", "App.exe"), []);

        Assert.Equal([new RepairCandidate(newest, RepairReason.OtherVersion), new RepairCandidate(older, RepairReason.OtherVersion)], found);
    }

    [Fact]
    public void A_version_further_up_the_path_is_found_too()
    {
        var moved = Make("tools", "tool-2.3", "bin", "tool.exe");

        Assert.Equal([new RepairCandidate(moved, RepairReason.OtherVersion)], LocationRepair.Candidates(Folder("tools", "tool-2.1", "bin", "tool.exe"), []));
    }

    [Fact]
    public void The_registered_path_and_the_program_folders_a_few_levels_deep_are_offered()
    {
        var registered = Make("reg", "WinSCP.exe");
        var deep = Make("pf", "Vendor", "Suite", "bin", "WinSCP.exe");
        Make("pf", "a", "b", "c", "d", "WinSCP.exe"); // too deep
        var likely = Make("pf86", "WinSCP", "WinSCP.exe");

        var found = LocationRepair.Candidates(@"U:\E\WinSCP\WinSCP.exe", [Folder("pf"), Folder("pf86"), Folder("absent")], name => name == "WinSCP.exe" ? registered : null);

        Assert.Equal(
            [
                new RepairCandidate(registered, RepairReason.Registered),
                new RepairCandidate(deep, RepairReason.InstalledProgram),
                new RepairCandidate(likely, RepairReason.InstalledProgram),
            ],
            found);
    }

    [Fact]
    public void The_path_itself_and_bare_names_are_never_offered()
    {
        var there = Make("pf86", "WinSCP", "WinSCP.exe");

        Assert.Empty(LocationRepair.Candidates(there, [Folder("pf86")], _ => there));
        Assert.Empty(LocationRepair.Candidates("notepad.exe", [Folder("pf86")]));
    }

    [Fact]
    public void A_PC_of_its_own_location_wins_there_and_travels_with_copies()
    {
        var shortcut = new ShortcutItem { Location = @"U:\E\WinSCP\WinSCP.exe" };
        shortcut.SetLocationFor("laptop", @" C:\Program Files (x86)\WinSCP\WinSCP.exe ");

        Assert.Equal(@"C:\Program Files (x86)\WinSCP\WinSCP.exe", shortcut.LocationFor("laptop"));
        Assert.Equal(@"U:\E\WinSCP\WinSCP.exe", shortcut.LocationFor("desk"));
        Assert.Equal(@"U:\E\WinSCP\WinSCP.exe", shortcut.LocationFor(null));

        var json = System.Text.Json.JsonSerializer.Serialize<LauncherItem>(shortcut, Storage.LayoutStorage.JsonOptions);
        var read = Assert.IsType<ShortcutItem>(System.Text.Json.JsonSerializer.Deserialize<LauncherItem>(json, Storage.LayoutStorage.JsonOptions));
        var copy = (ShortcutItem)read.Clone();
        read.SetLocationFor("laptop", null);

        Assert.Equal(@"C:\Program Files (x86)\WinSCP\WinSCP.exe", copy.LocationFor("laptop")); // the clone has its own map
        Assert.Null(read.Locations); // the last one removed: nothing left in the file
        Assert.DoesNotContain("locations", System.Text.Json.JsonSerializer.Serialize<LauncherItem>(read, Storage.LayoutStorage.JsonOptions), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(@"C:\Tools\app.exe", @"C:\Tools\app.exe")]
    [InlineData(@"""D:\Games\x.exe""", @"D:\Games\x.exe")]
    [InlineData(@"\\server\share\app.exe", null)]
    [InlineData("notepad.exe", null)]
    [InlineData("https://example.com", null)]
    [InlineData(@"shell:AppsFolder\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", null)]
    [InlineData("special:show-desktop", null)]
    [InlineData("", null)]
    public void Only_paths_on_a_drive_letter_are_checked(string location, string? checkedPath)
    {
        Assert.Equal(checkedPath, LocationProbe.CheckablePath(location));
    }

    [Fact]
    public void A_missing_target_is_reported_unless_it_is_on_slow_storage()
    {
        var shortcut = new ShortcutItem { Location = @"C:\Gone\app.exe" };

        Assert.True(LocationProbe.IsMissing(shortcut, _ => false, _ => false));
        Assert.False(LocationProbe.IsMissing(shortcut, _ => false, _ => true));
        Assert.False(LocationProbe.IsMissing(shortcut, _ => true, _ => false)); // a network or removable drive is not checked
        Assert.False(LocationProbe.IsMissing(new ShortcutItem { Location = "notepad.exe" }, _ => false, _ => false));
    }
}
