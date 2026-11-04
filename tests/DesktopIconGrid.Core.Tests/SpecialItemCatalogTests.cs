using DesktopIconGrid.Core;
using DesktopIconGrid.Core.Launching;
using DesktopIconGrid.Core.SpecialItems;

namespace DesktopIconGrid.Core.Tests;

public class SpecialItemCatalogTests
{
    [Fact]
    public void Catalog_covers_the_reference_entries()
    {
        var names = SpecialItemCatalog.All.Select(i => i.Name).ToList();

        foreach (var expected in new[] { "This PC", "Network", "Network Connections", "Control Panel", "Printers",
                                         "Recycle Bin", "Show Desktop", "Search", "File Explorer", "Calculator",
                                         "Notepad", "Paint", "WordPad", "Command Prompt" })
        {
            Assert.Contains(expected, names);
        }
    }

    [Fact]
    public void Entries_with_missing_executables_are_filtered_out()
    {
        var sections = SpecialItemCatalog.GetAvailableSections(exe => exe != "write.exe");

        var names = sections.SelectMany(s => s.Items).Select(i => i.Name).ToList();
        Assert.DoesNotContain("WordPad", names);
        Assert.Contains("Windows Terminal", names);
        Assert.Contains("Notepad", names);
    }

    [Fact]
    public void Shell_locations_used_by_the_catalog_plan_and_resolve()
    {
        foreach (var definition in SpecialItemCatalog.All)
        {
            var plan = LaunchPlanner.Plan(definition.CreateShortcut());
            Assert.True(plan.Action != LaunchAction.StartProcess || plan.StartInfo is not null, definition.Name);
        }

        Assert.True(ShellLocations.TryGetParsingName("shell:MyComputerFolder", out var pc));
        Assert.StartsWith("::{", pc);
        Assert.False(ShellLocations.TryGetParsingName("shell:Unknown", out _));
    }

    [Fact]
    public void CreateShortcut_copies_definition()
    {
        var definition = new SpecialItemDefinition("Name", "loc", "args", "icon,1");

        var shortcut = definition.CreateShortcut();

        Assert.Equal("Name", shortcut.Name);
        Assert.Equal("loc", shortcut.Location);
        Assert.Equal("args", shortcut.Parameters);
        Assert.Equal("icon,1", shortcut.IconPath);
    }
}
