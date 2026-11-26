using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Core.SpecialIcons;

namespace DesktopIconGrid.Core.Tests;

public class LauncherLayoutTests
{
    [Fact]
    public void A_special_icon_is_found_on_the_bar_in_a_group_and_in_a_group_in_a_group()
    {
        // 0.16.1 WP2: the sync window shows the Google account with a folder in use only when a bucket icon needs it.
        var bucket = SpecialIconKinds.GoogleDriveBucket;
        var layout = new LauncherLayout();
        layout.Items.Add(new ShortcutItem { Name = "Editor", Location = @"C:\Tools\editor.exe" });
        layout.Items.Add(new SpecialIconItem { Kind = SpecialIconKinds.SendToDevice, Name = "Send" });
        Assert.False(layout.HasSpecialIcon(bucket));
        Assert.True(layout.HasSpecialIcon(SpecialIconKinds.SendToDevice));

        layout.Items.Add(new SpecialIconItem { Kind = " " + bucket.ToUpperInvariant() + " ", Name = "Drop" }); // as a hand-edited file may have it
        Assert.True(layout.HasSpecialIcon(bucket));

        var inGroup = new LauncherLayout();
        inGroup.Items.Add(new GroupItem { Name = "Inbox", Items = [new SpecialIconItem { Kind = bucket, Name = "Drop" }] });
        Assert.True(inGroup.HasSpecialIcon(bucket));

        var deeper = new LauncherLayout();
        deeper.Items.Add(new GroupItem { Name = "Outer", Items = [new GroupItem { Name = "Inner", Items = [new SpecialIconItem { Kind = bucket, Name = "Drop" }] }] });
        Assert.True(deeper.HasSpecialIcon(bucket));
        Assert.False(new LauncherLayout().HasSpecialIcon(bucket));
    }
}
