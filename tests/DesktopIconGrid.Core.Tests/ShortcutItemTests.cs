using DesktopIconGrid.Core.Models;

namespace DesktopIconGrid.Core.Tests;

public class ShortcutItemTests
{
    [Theory]
    [InlineData("C:\\Tools\\WinSCP\\WinSCP.exe", "WinSCP")]
    [InlineData("C:\\Users\\me\\Projects\\", "Projects")]
    [InlineData("/home/me/.bashrc", ".bashrc")]
    [InlineData("https://example.com/", "example.com")]
    [InlineData("https://github.com/foo/bar", "bar")]
    [InlineData("notepad.exe", "notepad")]
    public void SuggestName_derives_caption(string location, string expected)
    {
        var name = ShortcutItem.SuggestName(location);

        Assert.Equal(expected, name);
    }

    [Fact]
    public void FromPath_trims_and_names()
    {
        var item = ShortcutItem.FromPath("  C:\\apps\\tool.exe ");

        Assert.Equal("C:\\apps\\tool.exe", item.Location);
        Assert.Equal("tool", item.Name);
        Assert.NotEqual(Guid.Empty, item.Id);
    }

    [Fact]
    public void Clone_is_deep_for_groups()
    {
        var group = new GroupItem { Name = "g", Items = [new ShortcutItem { Name = "a", Location = "a" }] };

        var copy = (GroupItem)group.Clone();
        copy.Items[0].Name = "changed";
        copy.Items.Add(new ShortcutItem());

        Assert.Equal("a", group.Items[0].Name);
        Assert.Single(group.Items);
        Assert.Equal(group.Id, copy.Id);
    }
}
