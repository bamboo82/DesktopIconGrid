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
        File_(".hidden", 1);
        Directory.CreateDirectory(Path.Combine(_folder, "zeta"));
        Directory.SetLastWriteTime(Path.Combine(_folder, "zeta"), DateTime.Now.AddMinutes(-40));

        var byName = FolderStack.List(_folder, newestFirst: false);
        var newest = FolderStack.List(_folder, newestFirst: true);

        Assert.Equal(["zeta", "a.txt", "b.txt", "Tool"], byName.Entries.Select(e => e.Name)); // .lnk shown without its extension
        Assert.True(byName.Entries[0].IsFolder);
        Assert.Equal(4, byName.Total); // the dot file is left out
        Assert.Equal(["a.txt", "Tool", "b.txt", "zeta"], newest.Entries.Select(e => e.Name));
        Assert.EndsWith("Tool.lnk", newest.Entries[1].Path);
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
        Assert.Equal((0, 0), (FolderStack.List(Path.Combine(_folder, "nope"), false).Entries.Count, FolderStack.List(Path.Combine(_folder, "nope"), false).Total));
    }
}
