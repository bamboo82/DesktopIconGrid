using DesktopIconGrid.Core.Launching;

namespace DesktopIconGrid.Core.Tests;

/// <summary>What a folder stack lists.</summary>
public sealed class FolderStackTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("dig-stack-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private void File_(string name, int minutesAgo)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllText(path, "x");
        File.SetLastWriteTime(path, DateTime.Now.AddMinutes(-minutesAgo));
    }

    [Fact]
    public void By_name_puts_folders_first_and_newest_first_goes_by_time()
    {
        File_("b.txt", 30);
        File_("a.txt", 10);
        File_("Tool.lnk", 20);
        // A hidden file: the attribute on Windows; on Linux .NET cannot set it and reads a dot file as hidden instead.
        var hidden = OperatingSystem.IsWindows() ? "hidden.txt" : ".hidden";
        File_(hidden, 1);
        File.SetAttributes(Path.Combine(_folder, hidden), FileAttributes.Hidden);
        Directory.CreateDirectory(Path.Combine(_folder, "zeta"));
        Directory.SetLastWriteTime(Path.Combine(_folder, "zeta"), DateTime.Now.AddMinutes(-40));

        var byName = FolderStack.List(_folder, newestFirst: false);
        var newest = FolderStack.List(_folder, newestFirst: true);

        Assert.Equal(["zeta", "a.txt", "b.txt", "Tool"], byName.Entries.Select(e => e.Name)); // .lnk shown without its extension
        Assert.True(byName.Entries[0].IsFolder);
        Assert.Equal(4, byName.Total); // a hidden file is left out, as Explorer leaves it out
        Assert.Equal(["a.txt", "Tool", "b.txt", "zeta"], newest.Entries.Select(e => e.Name));
        Assert.EndsWith("Tool.lnk", newest.Entries[1].Path);
    }

    [Fact]
    public void A_dot_file_is_listed_and_counted_where_Explorer_shows_it()
    {
        File_(".editorconfig", 5);
        File_("a.txt", 10);

        var list = FolderStack.List(_folder, newestFirst: false, max: 1);

        // Review 24: on Windows a dot file is an ordinary file, so it is listed and counted in "N more"; .NET on Linux
        // reports it as hidden, so there it is left out like any hidden file.
        var shown = OperatingSystem.IsWindows();
        Assert.Equal(shown ? 2 : 1, list.Total);
        Assert.Equal(shown ? ".editorconfig" : "a.txt", Assert.Single(list.Entries).Name);
    }

    [Fact]
    public void A_long_folder_is_cut_and_a_missing_one_is_empty()
    {
        for (var i = 0; i < 5; i++)
        {
            File_($"f{i}.txt", i);
        }

        var cut = FolderStack.List(_folder, newestFirst: false, max: 3);

        Assert.Equal(3, cut.Entries.Count);
        Assert.Equal(5, cut.Total);
        var missing = FolderStack.List(Path.Combine(_folder, "nope"), false);
        Assert.Empty(missing.Entries);
        Assert.Equal(FolderStack.Unreadable, missing.Total); // not "this folder is empty"
        Assert.Equal(0, FolderStack.List(Directory.CreateDirectory(Path.Combine(_folder, "empty")).FullName, false).Total);
    }

    [Fact]
    public void A_dragged_file_can_be_dropped_on_a_program_but_not_on_a_document_a_folder_or_itself()
    {
        var program = new StackEntry(@"C:\S\tool.exe", "tool.exe", false, DateTime.Now);
        var shortcut = new StackEntry(@"C:\S\Editor.lnk", "Editor", false, DateTime.Now);
        var document = new StackEntry(@"C:\S\a.txt", "a.txt", false, DateTime.Now);
        var folder = new StackEntry(@"C:\S\sub.exe", "sub.exe", true, DateTime.Now);

        Assert.True(FolderStack.TakesDrop(program, [@"C:\S\a.txt"]));
        Assert.True(FolderStack.TakesDrop(shortcut, [@"C:\S\a.txt", @"C:\S\b.txt"]));
        Assert.False(FolderStack.TakesDrop(document, [@"C:\S\b.txt"]));
        Assert.False(FolderStack.TakesDrop(folder, [@"C:\S\a.txt"]));
        Assert.False(FolderStack.TakesDrop(program, [@"c:\s\TOOL.exe"])); // onto itself
        Assert.False(FolderStack.TakesDrop(program, []));
    }
}
