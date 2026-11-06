using DesktopIconGrid.Core;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Core.Storage;

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

    private static LauncherLayout Layout(params LauncherItem[] items) => new() { Version = 1, Items = [.. items] };

    [Fact]
    public void A_copy_on_a_second_disk_keeps_its_own_paths_in_Windows_folders()
    {
        // Review 16 R4′: D:\Program Files (x86)\Steam was rewritten to C: at every start.
        var steam = DataLocation.WithAppDrive(@"D:\Program Files (x86)\Steam\steam.exe", "D:"); // as ForStorage stores it on D:
        var there = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"D:\Program Files (x86)\Steam\steam.exe", @"C:\Program Files (x86)\Steam\steam.exe" };

        var model = LauncherModel.FromLayout(Layout(new ShortcutItem { Name = "Steam", Location = steam }), there.Contains, "D:", "C:", "desk", _ => false);

        var shortcut = (ShortcutItem)model.Items[0];
        Assert.Equal(steam, shortcut.Location);
        Assert.Null(shortcut.Locations);
        Assert.Equal(0, model.RepairCount);
    }

    [Fact]
    public void A_path_0_8_0_wrote_for_Windows_drive_gives_this_PC_its_own_location_and_changes_nothing_else()
    {
        // Review 17 R4″: the repair decided the shared location for every PC from this PC's disks, behind a version number
        // that 0.8.0 shows a box for at every start.
        var notepad = new ShortcutItem
        {
            Name = "Notepad",
            Location = @"%APPDRIVE%\Windows\System32\notepad.exe",
            IconPath = @"%APPDRIVE%\Windows\System32\shell32.dll,3",
            Locations = new() { ["laptop"] = @"%APPDRIVE%\Windows\notepad.exe" },
        };
        var there = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"C:\Windows\System32\notepad.exe" };

        var model = LauncherModel.FromLayout(Layout(notepad), there.Contains, "E:", "C:", "stick-pc", _ => true); // a stick: no look at E:

        var repaired = (ShortcutItem)model.Items[0];
        Assert.Equal(@"%APPDRIVE%\Windows\System32\notepad.exe", repaired.Location); // the shared location is everyone's
        Assert.Equal(@"C:\Windows\System32\notepad.exe", repaired.Locations!["stick-pc"]); // this PC's own
        Assert.Equal(@"%APPDRIVE%\Windows\notepad.exe", repaired.Locations["laptop"]); // another PC's
        Assert.Equal(@"%APPDRIVE%\Windows\System32\shell32.dll,3", repaired.IconPath);
        Assert.Equal(1, model.RepairCount);
        Assert.Equal(1, model.ToLayout().Version);

        var again = LauncherModel.FromLayout(model.ToLayout(), there.Contains, "E:", "C:", "stick-pc", _ => true);
        Assert.Equal(0, again.RepairCount); // safe to repeat
    }

    [Fact]
    public void The_repair_takes_this_PCs_id_from_ThisDevice_when_none_is_passed()
    {
        // Review 18 D1: the start-up load ran before ThisDevice.Id was set, so the repair never ran there. The other tests
        // passed a device id and could not see it.
        var previous = ThisDevice.Id;
        ThisDevice.Id = "stick-pc";
        try
        {
            var notepad = new ShortcutItem { Name = "Notepad", Location = @"%APPDRIVE%\Windows\notepad.exe" };

            var model = LauncherModel.FromLayout(Layout(notepad), path => path == @"C:\Windows\notepad.exe", "E:", "C:", isSlowDrive: _ => true);

            Assert.Equal(@"C:\Windows\notepad.exe", ((ShortcutItem)model.Items[0]).Locations!["stick-pc"]);
        }
        finally
        {
            ThisDevice.Id = previous;
        }
    }

    [Fact]
    public void This_PCs_own_entry_on_the_stick_is_looked_at_before_it_is_replaced()
    {
        // Review 18 D4: a stick counted as missing without a look, and an "only this PC" path on it was replaced.
        var tool = new ShortcutItem
        {
            Name = "Tool",
            Location = @"C:\Tools\tool.exe",
            Locations = new() { ["stick-pc"] = @"%APPDRIVE%\Program Files\Tool\tool.exe" },
        };
        var there = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"C:\Program Files\Tool\tool.exe", @"E:\Program Files\Tool\tool.exe" };

        var model = LauncherModel.FromLayout(Layout(tool), there.Contains, "E:", "C:", "stick-pc", _ => true);

        Assert.Equal(@"%APPDRIVE%\Program Files\Tool\tool.exe", ((ShortcutItem)model.Items[0]).Locations!["stick-pc"]);
        Assert.Equal(0, model.RepairCount);
    }

    [Theory]
    [InlineData(2, 1)] // review 18 D2: written only by test builds of 0.8.1, and nothing new in it
    [InlineData(1, 1)]
    [InlineData(3, 3)] // a real newer version stays newer
    public void Version_2_reads_as_1(int written, int kept)
    {
        Assert.Equal(kept, LauncherModel.FromLayout(new LauncherLayout { Version = written }).ToLayout().Version);
    }

    [Theory]
    [InlineData("C:")] // a copy on Windows' drive: %APPDRIVE% is C: already
    [InlineData(null)] // a copy on a share has no %APPDRIVE%
    public void A_copy_that_cannot_repair_changes_neither_paths_nor_the_version(string? appDrive)
    {
        var model = LauncherModel.FromLayout(Layout(new ShortcutItem { Name = "Notepad", Location = @"%APPDRIVE%\Windows\notepad.exe" }), _ => true, appDrive ?? "", "C:", "desk", _ => false);

        Assert.Null(((ShortcutItem)model.Items[0]).Locations);
        Assert.Equal(1, model.ToLayout().Version);
    }

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
    public void Insert_update_and_rename_stamp_the_modified_time()
    {
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);
        var model = ModelWith("a");
        var item = model.Items[0];
        Assert.NotNull(item.ModifiedAt);
        Assert.True(item.ModifiedAt >= before);

        var stamped = item.ModifiedAt;
        var edited = (ShortcutItem)item.Clone();
        edited.Parameters = "x";
        model.Update(edited);
        Assert.True(item.ModifiedAt >= stamped);

        Assert.True(model.Rename(item.Id, "  Renamed "));
        Assert.Equal("Renamed", item.Name);
        Assert.False(model.Rename(item.Id, "Renamed"));
        Assert.False(model.Rename(item.Id, "  "));
        Assert.False(model.Rename(Guid.NewGuid(), "x"));

        var preset = new ShortcutItem { Name = "old", Location = "x", ModifiedAt = before.AddDays(-1) };
        model.Add(preset);
        Assert.Equal(before.AddDays(-1), preset.ModifiedAt);
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
    public void ToLayout_keeps_the_newer_format_version_of_a_loaded_layout()
    {
        var newer = LauncherModel.FromLayout(new LauncherLayout { Version = LauncherLayout.CurrentVersion + 6 });
        var older = LauncherModel.FromLayout(new LauncherLayout { Version = 0 });

        Assert.Equal(LauncherLayout.CurrentVersion + 6, newer.ToLayout().Version);
        Assert.Equal(LauncherLayout.CurrentVersion, older.ToLayout().Version);
        Assert.Equal(LauncherLayout.CurrentVersion, new LauncherModel().ToLayout().Version);
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
