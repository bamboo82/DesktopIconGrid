using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Core.SpecialIcons;
using DesktopIconGrid.Core.Storage;

namespace DesktopIconGrid.Core.Tests;

public class SpecialIconItemTests
{
    [Fact]
    public void Special_icons_round_trip_through_the_layout_file_including_unknown_kinds()
    {
        var directory = Path.Combine(Path.GetTempPath(), "DesktopIconGrid.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var storage = new LayoutStorage(Path.Combine(directory, "layout.json"));
            var cpu = new SpecialIconItem { Name = "CPU", Kind = SpecialIconKinds.CpuUsage };
            new MonitorIconSettings(GaugeStyle.Bar, null).ApplyTo(cpu);
            var future = new SpecialIconItem { Name = "Future", Kind = "from-a-newer-version", Settings = { ["x"] = "1" } };
            var group = new GroupItem { Name = "G", Items = [new SpecialIconItem { Name = "Disk", Kind = SpecialIconKinds.DiskUsage, Settings = { ["drive"] = "d:" } }] };

            storage.Save(new LauncherLayout { Items = [cpu, future, group] });
            var model = LauncherModel.FromLayout(storage.Load().Layout);

            var loadedCpu = Assert.IsType<SpecialIconItem>(model.Items[0]);
            Assert.Equal(GaugeStyle.Bar, MonitorIconSettings.From(loadedCpu).Style);
            var loadedFuture = Assert.IsType<SpecialIconItem>(model.Items[1]);
            Assert.Equal("from-a-newer-version", loadedFuture.Kind);
            Assert.Equal("1", loadedFuture.GetSetting("x"));
            var loadedDisk = Assert.IsType<SpecialIconItem>(Assert.Single(Assert.IsType<GroupItem>(model.Items[2]).Items));
            Assert.Equal("D:\\", MonitorIconSettings.From(loadedDisk).Drive);
            Assert.Equal(0, model.RepairCount);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void Clone_and_update_copy_settings_independently()
    {
        var model = new LauncherModel();
        var item = new SpecialIconItem { Name = "CPU", Kind = SpecialIconKinds.CpuUsage };
        model.Add(item);

        var draft = (SpecialIconItem)item.Clone();
        draft.SetSetting("style", "number");
        draft.Name = "Processor";
        Assert.Null(item.GetSetting("style"));

        model.Update(draft);
        Assert.Equal("number", item.GetSetting("style"));
        Assert.Equal("Processor", item.Name);

        draft.SetSetting("style", "  ");
        Assert.Null(draft.GetSetting("style"));
    }

    [Theory]
    [InlineData("c", "C:\\")]
    [InlineData("D:", "D:\\")]
    [InlineData("e:\\", "E:\\")]
    [InlineData("F:\\Users", "F:\\")]
    [InlineData("", null)]
    [InlineData("1:", null)]
    [InlineData("CD", null)]
    public void Drive_roots_are_normalized(string input, string? expected)
    {
        Assert.Equal(expected, MonitorIconSettings.NormalizeDrive(input));
    }

    [Fact]
    public void Special_icons_may_live_in_groups_but_groups_may_not()
    {
        var model = new LauncherModel();
        var group = new GroupItem { Name = "G" };
        model.Add(group);

        model.Add(new SpecialIconItem { Name = "CPU", Kind = SpecialIconKinds.CpuUsage }, group);
        Assert.Single(group.Items);
        Assert.Throws<InvalidOperationException>(() => model.Add(new GroupItem(), group));
    }
}
