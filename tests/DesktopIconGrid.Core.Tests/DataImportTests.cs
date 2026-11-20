using System.Text.Json;
using DesktopIconGrid.Core.Launching;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Core.SpecialIcons;
using DesktopIconGrid.Core.Storage;
using DesktopIconGrid.Core.Sync;

namespace DesktopIconGrid.Core.Tests;

/// <summary>0.13.0 WP1: this PC's bar brought into a portable copy, or the data to a folder chosen in Settings.</summary>
public sealed class DataImportTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 9, 30, 0, TimeSpan.Zero);

    private readonly string _root = Directory.CreateTempSubdirectory("dig-import-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Folder(string name)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private static void Write(string folder, string name, string text = "{}")
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(folder, name))!);
        File.WriteAllText(Path.Combine(folder, name), text);
    }

    private static LauncherLayout Bar() => new()
    {
        Items =
        [
            new ShortcutItem { Name = "PCMan", Location = @"E:\Tools\PCMan\PCMan.exe", StartIn = @"E:\Tools\PCMan", IconPath = @"E:\Icons\pcman.ico" },
            new ShortcutItem { Name = "Notepad", Location = @"C:\Windows\notepad.exe" },
            new GroupItem
            {
                Name = "Work",
                IconPath = @"E:\Icons\work.ico",
                Items =
                [
                    new ShortcutItem
                    {
                        Name = "Data",
                        Location = @"D:\Data",
                        Locations = new Dictionary<string, string> { ["aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"] = @"E:\Data" },
                    },
                ],
            },
        ],
    };

    [Fact]
    public void The_bar_its_workspaces_and_its_counts_are_copied_and_nothing_of_one_copy_or_one_PC()
    {
        var source = Folder("this-pc");
        new LayoutStorage(Path.Combine(source, LayoutStorage.FileName)).Save(Bar());
        Write(source, WorkspacePositions.FileName);
        Write(source, UsageLog.FileName);
        foreach (var own in new[] { CommandTrust.FileName, SyncSettingsStore.FileName, LocalSettings.FileName, "google-token.bin", @"logs/app.log", "history/layout-20261120.json", "drive-cache/x", "icons/a.png", "sounds/s.wav" })
        {
            Write(source, own);
        }

        var target = Path.Combine(_root, "portable", "data"); // made when missing
        var result = DataImport.Run(source, target, appDrive: null, Now);

        Assert.Null(result.Problem);
        Assert.Equal([WorkspacePositions.FileName, UsageLog.FileName, LayoutStorage.FileName], result.Copied);
        Assert.Empty(result.KeptAside);
        Assert.Equal(
            [LayoutStorage.FileName, UsageLog.FileName, WorkspacePositions.FileName],
            Directory.EnumerateFileSystemEntries(target).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.Equal(File.ReadAllBytes(Path.Combine(source, LayoutStorage.FileName)), File.ReadAllBytes(Path.Combine(target, LayoutStorage.FileName))); // no drive: as it is
        Assert.True(DataImport.HasBar(source));
        Assert.False(DataImport.HasBar(Folder("empty")));
    }

    [Fact]
    public void A_file_already_there_is_kept_aside_first_and_a_second_import_keeps_both()
    {
        var source = Folder("this-pc");
        Write(source, LayoutStorage.FileName, """{"items":[]}""");
        Write(source, UsageLog.FileName, "source counts");
        var target = Folder("portable");
        Write(target, LayoutStorage.FileName, "the portable bar");
        Write(target, UsageLog.FileName, "portable counts");

        var first = DataImport.Run(source, target, appDrive: null, Now);
        var second = DataImport.Run(source, target, appDrive: null, Now); // the same second

        var stamp = Now.ToLocalTime().ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal([$"usage.json.before-import-{stamp}", $"layout.json.before-import-{stamp}"], first.KeptAside);
        Assert.Equal([$"usage.json.before-import-{stamp}-2", $"layout.json.before-import-{stamp}-2"], second.KeptAside);
        Assert.Equal("the portable bar", File.ReadAllText(Path.Combine(target, $"layout.json.before-import-{stamp}")));
        Assert.Equal("portable counts", File.ReadAllText(Path.Combine(target, $"usage.json.before-import-{stamp}")));
        Assert.Equal("source counts", File.ReadAllText(Path.Combine(target, UsageLog.FileName)));
    }

    [Fact]
    public void Into_a_portable_copy_the_paths_on_its_drive_are_written_with_APPDRIVE()
    {
        var source = Folder("this-pc");
        new LayoutStorage(Path.Combine(source, LayoutStorage.FileName)).Save(Bar());
        var target = Folder("portable");

        Assert.Null(DataImport.Run(source, target, appDrive: "E:", Now).Problem);

        var copied = new LayoutStorage(Path.Combine(target, LayoutStorage.FileName)).Load().Layout;
        var pcman = Assert.IsType<ShortcutItem>(copied.Items[0]);
        Assert.Equal((@"%APPDRIVE%\Tools\PCMan\PCMan.exe", @"%APPDRIVE%\Tools\PCMan", @"%APPDRIVE%\Icons\pcman.ico"), (pcman.Location, pcman.StartIn, pcman.IconPath));
        Assert.Equal(@"C:\Windows\notepad.exe", Assert.IsType<ShortcutItem>(copied.Items[1]).Location); // another drive stays
        var work = Assert.IsType<GroupItem>(copied.Items[2]);
        Assert.Equal(@"%APPDRIVE%\Icons\work.ico", work.IconPath);
        var data = Assert.IsType<ShortcutItem>(Assert.Single(work.Items));
        Assert.Equal(@"D:\Data", data.Location);
        Assert.Equal(@"%APPDRIVE%\Data", data.Locations!["aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"]);

        // The source is only read.
        Assert.Equal(@"E:\Tools\PCMan\PCMan.exe", Assert.IsType<ShortcutItem>(new LayoutStorage(Path.Combine(source, LayoutStorage.FileName)).Load().Layout.Items[0]).Location);
    }

    [Fact]
    public void A_damaged_bar_or_one_of_a_newer_version_is_copied_as_it_is_and_the_source_is_left_alone()
    {
        var source = Folder("this-pc");
        Write(source, LayoutStorage.FileName, "{ not json");
        var target = Folder("portable");

        Assert.Null(DataImport.Run(source, target, appDrive: "E:", Now).Problem);
        Assert.Equal("{ not json", File.ReadAllText(Path.Combine(target, LayoutStorage.FileName)));
        Assert.Equal([LayoutStorage.FileName], Directory.EnumerateFiles(source).Select(Path.GetFileName)); // not moved aside there

        var newer = JsonSerializer.Serialize(new { version = LauncherLayout.CurrentVersion + 5, items = Array.Empty<object>(), futureThing = @"E:\x" });
        Write(source, LayoutStorage.FileName, newer);
        var again = Folder("portable-2");
        Assert.Null(DataImport.Run(source, again, appDrive: "E:", Now).Problem);
        Assert.Equal(newer, File.ReadAllText(Path.Combine(again, LayoutStorage.FileName)));
    }

    [Fact]
    public void A_portable_copy_without_a_bar_asks_at_start_when_this_PC_has_one()
    {
        var portable = Folder("stick-data");
        var thisPc = Folder("this-pc");
        Assert.False(DataImport.OffersThisPcBar(portable: true, portable, thisPc)); // this PC has no bar

        File.WriteAllText(Path.Combine(thisPc, LayoutStorage.FileName), "{}");
        Assert.True(DataImport.OffersThisPcBar(portable: true, portable, thisPc));
        Assert.False(DataImport.OffersThisPcBar(portable: false, portable, thisPc)); // not a portable copy
        Assert.False(DataImport.OffersThisPcBar(portable: true, thisPc, thisPc)); // portable.txt names this PC's folder

        File.WriteAllText(Path.Combine(portable, LayoutStorage.FileName), "{}"); // "Start empty" saved one: not asked again
        Assert.False(DataImport.OffersThisPcBar(portable: true, portable, thisPc));
    }

    [Fact]
    public void The_same_folder_or_a_source_without_files_copies_nothing()
    {
        var folder = Folder("data");
        Write(folder, LayoutStorage.FileName, "{}");

        foreach (var result in new[] { DataImport.Run(folder, folder + Path.DirectorySeparatorChar, null, Now), DataImport.Run(Folder("nothing"), Folder("target"), null, Now) })
        {
            Assert.Empty(result.Copied);
            Assert.Empty(result.KeptAside);
            Assert.Null(result.Problem);
        }

        Assert.Equal("{}", File.ReadAllText(Path.Combine(folder, LayoutStorage.FileName)));
    }
}
