using DesktopIconGrid.Core;
using DesktopIconGrid.Core.Models;

namespace DesktopIconGrid.Core.Tests;

public class LauncherModelTests
{
    private static ShortcutItem Shortcut(string name) => new() { Name = name, Location = $"C:\\apps\\{name}.exe" };

    private static LauncherModel ModelWith(params string[] names)
    {
        var model = new LauncherModel();
        foreach (var name in names)
        {
            model.Add(Shortcut(name));
        }

        return model;
    }

    private static string[] Names(IEnumerable<LauncherItem> items) => items.Select(i => i.Name).ToArray();

    [Fact]
    public void Add_appends_in_order()
    {
        var model = ModelWith("a", "b", "c");

        Assert.Equal(["a", "b", "c"], Names(model.Items));
    }

    [Fact]
    public void Insert_clamps_index_to_list()
    {
        var model = ModelWith("a", "b");

        model.Insert(Shortcut("first"), null, -5);
        model.Insert(Shortcut("last"), null, 99);

        Assert.Equal(["first", "a", "b", "last"], Names(model.Items));
    }

    [Fact]
    public void Insert_rejects_duplicates_and_nested_groups()
    {
        var model = ModelWith("a");
        var group = new GroupItem { Name = "g" };
        model.Add(group);

        Assert.Throws<InvalidOperationException>(() => model.Add(model.Items[0]));
        Assert.Throws<InvalidOperationException>(() => model.Add(new GroupItem { Name = "nested" }, group));
    }

    [Fact]
    public void Move_forward_within_root_uses_pre_move_index()
    {
        var model = ModelWith("a", "b", "c", "d");
        var a = model.Items[0];

        // Drop "a" before "d" (index 3 of the list as displayed) → a ends up right before d.
        model.Move(a.Id, null, 3);

        Assert.Equal(["b", "c", "a", "d"], Names(model.Items));
    }

    [Fact]
    public void Move_backward_within_root()
    {
        var model = ModelWith("a", "b", "c", "d");
        var d = model.Items[3];

        model.Move(d.Id, null, 1);

        Assert.Equal(["a", "d", "b", "c"], Names(model.Items));
    }

    [Fact]
    public void Move_to_end_and_onto_itself_are_stable()
    {
        var model = ModelWith("a", "b", "c");
        var b = model.Items[1];

        model.Move(b.Id, null, 1);
        Assert.Equal(["a", "b", "c"], Names(model.Items));

        model.Move(b.Id, null, 2);
        Assert.Equal(["a", "b", "c"], Names(model.Items));

        model.Move(b.Id, null, int.MaxValue);
        Assert.Equal(["a", "c", "b"], Names(model.Items));
    }

    [Fact]
    public void Move_into_group_and_back_out()
    {
        var model = ModelWith("a", "b");
        var group = new GroupItem { Name = "g" };
        model.Add(group);
        var a = model.Items[0];

        model.MoveIntoGroup(a.Id, group);
        Assert.Equal(["b", "g"], Names(model.Items));
        Assert.Equal(["a"], Names(group.Items));
        Assert.Same(group, Assert.IsType<GroupItem>(ParentOf(model, a.Id)));

        model.Move(a.Id, null, 0);
        Assert.Equal(["a", "b", "g"], Names(model.Items));
        Assert.Empty(group.Items);
    }

    [Fact]
    public void Move_group_into_group_is_rejected()
    {
        var model = new LauncherModel();
        var g1 = new GroupItem { Name = "g1" };
        var g2 = new GroupItem { Name = "g2" };
        model.Add(g1);
        model.Add(g2);

        Assert.Throws<InvalidOperationException>(() => model.MoveIntoGroup(g1.Id, g2));
    }

    [Fact]
    public void Move_unknown_item_throws()
    {
        var model = ModelWith("a");

        Assert.Throws<KeyNotFoundException>(() => model.Move(Guid.NewGuid(), null, 0));
    }

    [Fact]
    public void Remove_works_at_root_and_inside_groups()
    {
        var model = ModelWith("a");
        var group = new GroupItem { Name = "g" };
        model.Add(group);
        var inner = Shortcut("inner");
        model.Add(inner, group);

        Assert.True(model.Remove(inner.Id));
        Assert.True(model.Remove(model.Items[0].Id));
        Assert.False(model.Remove(Guid.NewGuid()));
        Assert.Equal(["g"], Names(model.Items));
    }

    [Fact]
    public void Update_copies_content_but_not_identity()
    {
        var model = ModelWith("a");
        var original = (ShortcutItem)model.Items[0];
        var edited = (ShortcutItem)original.Clone();
        edited.Name = "renamed";
        edited.Location = " D:\\new.exe ";
        edited.Parameters = "  ";
        edited.IconPath = " C:\\icon.png ";
        edited.MultiLaunch = true;

        model.Update(edited);

        Assert.Same(original, model.Items[0]);
        Assert.Equal("renamed", original.Name);
        Assert.Equal("D:\\new.exe", original.Location);
        Assert.Null(original.Parameters);
        Assert.Equal("C:\\icon.png", original.IconPath);
        Assert.True(original.MultiLaunch);
    }

    [Fact]
    public void Update_rejects_kind_change()
    {
        var model = ModelWith("a");
        var asGroup = new GroupItem { Id = model.Items[0].Id, Name = "g" };

        Assert.Throws<InvalidOperationException>(() => model.Update(asGroup));
    }

    [Fact]
    public void MultiLaunchMembers_are_in_bar_order_including_groups()
    {
        var model = new LauncherModel();
        var a = Shortcut("a");
        a.MultiLaunch = true;
        var group = new GroupItem { Name = "g" };
        var inner = Shortcut("inner");
        inner.MultiLaunch = true;
        var b = Shortcut("b");
        b.MultiLaunch = true;
        model.Add(a);
        model.Add(group);
        model.Add(inner, group);
        model.Add(b);
        model.Add(Shortcut("not-member"));

        Assert.Equal(["a", "inner", "b"], Names(model.MultiLaunchMembers()));
        Assert.Equal(4, model.TotalShortcutCount);
    }

    [Fact]
    public void FromLayout_repairs_ids_flattens_nested_groups_and_fills_names()
    {
        var sharedId = Guid.NewGuid();
        var layout = new LauncherLayout
        {
            Settings = new LauncherSettings { IconSize = 30, CellPadding = 500 },
            Window = new WindowPlacement { Width = 0, Height = 0 },
            Items =
            [
                new ShortcutItem { Id = sharedId, Name = "", Location = "C:\\tools\\first.exe" },
                new ShortcutItem { Id = sharedId, Name = "dup", Location = "x" },
                new GroupItem
                {
                    Id = Guid.Empty,
                    Name = " ",
                    Items =
                    [
                        new ShortcutItem { Name = "inner" , Location = "y" },
                        new GroupItem { Name = "nested", Items = [new ShortcutItem { Name = "deep", Location = "z" }] },
                    ],
                },
            ],
        };

        var model = LauncherModel.FromLayout(layout);

        Assert.Equal(32, model.Settings.IconSize);
        Assert.Equal(LauncherSettings.MaxCellPadding, model.Settings.CellPadding);
        Assert.Null(model.Window);
        Assert.Equal(["first", "dup", "Group"], Names(model.Items));
        Assert.Equal(model.Items.Select(i => i.Id).Distinct().Count(), model.Items.Count);
        var group = Assert.IsType<GroupItem>(model.Items[2]);
        Assert.Equal(["inner", "deep"], Names(group.Items));
        Assert.True(model.RepairCount >= 3);
    }

    [Fact]
    public void ToLayout_round_trips_through_FromLayout()
    {
        var model = ModelWith("a", "b");
        var group = new GroupItem { Name = "g", IconPath = "C:\\g.png" };
        model.Add(group);
        model.Add(Shortcut("inner"), group);
        model.Window = new WindowPlacement { Left = 1, Top = 2, Width = 300, Height = 100 };
        model.Settings.ShowLabels = true;

        var copy = LauncherModel.FromLayout(model.ToLayout());

        Assert.Equal(0, copy.RepairCount);
        Assert.Equal(Names(model.Items), Names(copy.Items));
        Assert.Equal(["inner"], Names(Assert.IsType<GroupItem>(copy.Items[2]).Items));
        Assert.Equal(model.Items.Select(i => i.Id), copy.Items.Select(i => i.Id));
        Assert.True(copy.Settings.ShowLabels);
        Assert.Equal(300, copy.Window!.Width);
    }

    [Fact]
    public void ToLayout_returns_copies()
    {
        var model = ModelWith("a");

        var layout = model.ToLayout();
        layout.Items[0].Name = "changed";

        Assert.Equal("a", model.Items[0].Name);
    }

    private static LauncherItem? ParentOf(LauncherModel model, Guid id)
    {
        model.Find(id, out var parent);
        return parent;
    }
}
