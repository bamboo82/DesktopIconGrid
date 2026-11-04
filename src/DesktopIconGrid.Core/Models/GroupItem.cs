namespace DesktopIconGrid.Core.Models;

/// <summary>A folder on the bar that holds its own ordered list of shortcuts.</summary>
public sealed class GroupItem : LauncherItem
{
    public List<LauncherItem> Items { get; set; } = [];

    public override LauncherItem Clone()
    {
        var copy = (GroupItem)MemberwiseClone();
        copy.Items = Items.Select(i => i.Clone()).ToList();
        return copy;
    }
}
