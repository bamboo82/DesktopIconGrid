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
    public void A_missing_folder_is_looked_for_as_a_folder_its_other_versions_too()
    {
        // The review's R6: only files were accepted, so a missing folder had nothing to repair it with.
        var newer = Folder("tools", "app-1.0.9203");
        Directory.CreateDirectory(newer);
        var file = Make("tools", "app-1.0.9300"); // a file of that name: offered too, since the old name cannot tell the kind

        var found = LocationRepair.Candidates(Folder("tools", "app-1.0.9172"), []);

        Assert.Equal([new RepairCandidate(file, RepairReason.OtherVersion), new RepairCandidate(newer, RepairReason.OtherVersion)], found);
    }

    [Fact]
    public void A_versioned_file_is_found_and_another_architecture_is_no_other_version()
    {
        // Review 16, R6′'s rest: level 0 listed folders only, and x86 counted as a version number, so win-x86 sorted first.
        var rufus = Make("tools", "rufus-4.4.exe");
        Assert.Equal([new RepairCandidate(rufus, RepairReason.OtherVersion)], LocationRepair.Candidates(Folder("tools", "rufus-4.3.exe"), []));

        var x64 = Folder("tools", "node-v22.1.0-win-x64");
        Directory.CreateDirectory(x64);
        Directory.CreateDirectory(Folder("tools", "node-v22.1.0-win-x86"));
        Assert.Equal([new RepairCandidate(x64, RepairReason.OtherVersion)], LocationRepair.Candidates(Folder("tools", "node-v20.11.0-win-x64"), []));
    }

    [Fact]
    public void Versioned_folders_and_files_whose_names_mislead_the_guess_are_found()
    {
        // Review 15 R6′: node-v20.11.0-win-x64 and Contoso.Web were taken for files, Makefile for a folder, so Repair
        // said "Nothing found" for R6's own case, a version renamed.
        var node = Folder("tools", "node-v22.1.0-win-x64");
        Directory.CreateDirectory(node);
        Assert.Equal([new RepairCandidate(node, RepairReason.OtherVersion)], LocationRepair.Candidates(Folder("tools", "node-v20.11.0-win-x64"), []));

        var web = Folder("pf", "Contoso", "Contoso.Web");
        Directory.CreateDirectory(web);
        Assert.Contains(web, LocationRepair.Candidates(Folder("old", "Contoso", "Contoso.Web"), [Folder("pf")]).Select(c => c.Path));

        var makefile = Make("pf", "Build", "Makefile");
        Assert.Contains(makefile, LocationRepair.Candidates(Folder("old", "Build", "Makefile"), [Folder("pf")]).Select(c => c.Path));
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
                new RepairCandidate(likely, RepairReason.InstalledProgram), // a folder of the same name, in any root, before any deep search
                new RepairCandidate(deep, RepairReason.InstalledProgram, Likely: false), // the file name alone
            ],
            found);
    }

    [Fact]
    public void Every_program_folder_is_searched_even_when_the_first_is_too_big_and_the_search_says_it_stopped()
    {
        // The review: 5,882 folders in Program Files used the whole budget, so Program Files (x86) was never searched.
        for (var i = 0; i < 30; i++)
        {
            Directory.CreateDirectory(Folder("pf", $"Vendor{i}", "x"));
        }

        var target = Make("pf86", "Acme", "Tools", "tool.exe");

        var search = LocationRepair.Search(@"U:\E\Tool\tool.exe", [Folder("pf"), Folder("pf86")], maxFoldersPerRoot: 10);

        Assert.True(search.StoppedEarly);
        Assert.Equal([new RepairCandidate(target, RepairReason.InstalledProgram, Likely: false)], search.Candidates);
        Assert.False(LocationRepair.Search(@"U:\E\Tool\tool.exe", [Folder("pf86")], maxFoldersPerRoot: 10).StoppedEarly);
    }

    [Fact]
    public void A_folder_of_the_same_name_comes_first_and_a_match_on_the_file_name_alone_comes_last()
    {
        // The review: seven launcher.exe on one PC, and Reg Organizer's was offered first for Opera.
        var other = Make("pf", "Reg Organizer", "launcher.exe");
        var opera = Make("programs", "Opera", "launcher.exe");

        var search = LocationRepair.Search(@"C:\Users\me\AppData\Local\Programs\Opera\launcher.exe", [Folder("pf"), Folder("programs")]);

        Assert.Equal(
            [new RepairCandidate(opera, RepairReason.InstalledProgram), new RepairCandidate(other, RepairReason.InstalledProgram, Likely: false)],
            search.Candidates);
        Assert.False(search.StoppedEarly);
    }

    [Fact]
    public void The_Store_folder_is_searched_only_for_the_package_the_program_came_from()
    {
        Make("pf", "WindowsApps", "Other.App_1.0.0.0_x64__abcdefghijklm", "tool.exe");
        var same = Make("pf", "WindowsApps", "Contoso.Tool_2.0.0.0_x64__abcdefghijklm", "bin", "tool.exe");

        Assert.Empty(LocationRepair.Search(@"U:\Tools\tool.exe", [Folder("pf")]).Candidates); // not a Store program: not entered

        var search = LocationRepair.Search(@"C:\Program Files\WindowsApps\Contoso.Tool_1.0.0.0_x64__abcdefghijklm\tool.exe", [Folder("pf")]);

        Assert.Contains(search.Candidates, c => c.Path == same);
        Assert.DoesNotContain(search.Candidates, c => c.Path.Contains("Other.App", StringComparison.Ordinal));
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
