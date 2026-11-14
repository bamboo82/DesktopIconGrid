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
        Assert.False(FolderStack.TakesDrop(shortcut with { LinksToFolder = true }, [@"C:\S\a.txt"])); // review 27
    }

    /// <summary>The first 28 bytes of a shell link (MS-SHLLINK 2.1): header size, class id, flags, the target's attributes.</summary>
    private void Link(string name, int attributes, bool shellLink = true)
    {
        var header = new byte[76];
        BitConverter.GetBytes(0x4C).CopyTo(header, 0);
        (shellLink ? new Guid("00021401-0000-0000-c000-000000000046") : Guid.NewGuid()).TryWriteBytes(header.AsSpan(4, 16));
        BitConverter.GetBytes(attributes).CopyTo(header, 24);
        File.WriteAllBytes(Path.Combine(_folder, name), header);
    }

    [Fact]
    public void A_shortcut_whose_target_is_a_folder_takes_no_drop_and_its_target_is_not_touched()
    {
        // Review 27: the header keeps the target's attributes, so a target on a share that sleeps is not asked.
        Link("Projects.lnk", 0x10); // FILE_ATTRIBUTE_DIRECTORY
        Link("Editor.lnk", 0x20); // FILE_ATTRIBUTE_ARCHIVE: a file
        Link("Odd.lnk", 0x10, shellLink: false); // not a shell link, whatever it says
        File.WriteAllText(Path.Combine(_folder, "Short.lnk"), "x"); // too short to be one

        var entries = FolderStack.List(_folder, newestFirst: false).Entries.ToDictionary(entry => entry.Name);

        Assert.True(entries["Projects"].LinksToFolder);
        Assert.False(entries["Editor"].LinksToFolder);
        Assert.False(entries["Odd"].LinksToFolder);
        Assert.False(entries["Short"].LinksToFolder);
        Assert.False(FolderStack.TakesDrop(entries["Projects"], [Path.Combine(_folder, "a.txt")]));
        Assert.True(FolderStack.TakesDrop(entries["Editor"], [Path.Combine(_folder, "a.txt")]));
    }
}
