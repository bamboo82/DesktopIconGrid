using DesktopIconGrid.Core.Imports;
using DesktopIconGrid.Core.Models;

namespace DesktopIconGrid.Core.Tests;

/// <summary>Import…: which candidates may go on the bar, and what the chosen ones become.</summary>
public class ImportPlanTests
{
    [Theory]
    [InlineData(ImportSource.WindowsTerminal, "Ubuntu", "wt.exe", true)]
    [InlineData(ImportSource.Putty, "web server", "putty.exe", true)]
    [InlineData(ImportSource.WindowsTerminal, "x\" ; calc", "wt.exe", false)] // would end its quotes
    [InlineData(ImportSource.WindowsTerminal, "a;b", "wt.exe", false)] // Windows Terminal starts another tab at ";"
    [InlineData(ImportSource.Putty, "a;b", "putty.exe", true)]
    [InlineData(ImportSource.SshHosts, "web", "wt.exe", true)]
    [InlineData(ImportSource.SshHosts, "-oProxyCommand=calc", "ssh.exe", false)] // an option, not a host
    [InlineData(ImportSource.SshHosts, "web&calc", "ssh.exe", false)]
    [InlineData(ImportSource.Desktop, "Two\nlines", @"C:\x.exe", false)]
    [InlineData(ImportSource.Desktop, " ", @"C:\x.exe", false)]
    public void Only_names_that_stay_one_argument_are_offered(ImportSource source, string name, string location, bool usable)
    {
        Assert.Equal(usable, ImportPlan.IsUsable(new ImportCandidate(source, name, location, "-p \"x\"")));
    }

    [Fact]
    public void Chosen_candidates_become_shortcuts_or_one_group_per_source()
    {
        ImportCandidate[] chosen =
        [
            new(ImportSource.WindowsTerminal, "Ubuntu", "wt.exe", "-p \"Ubuntu\""),
            new(ImportSource.Desktop, "Tool", @"E:\Tools\tool.exe", null, @"E:\Tools\data"),
            new(ImportSource.WindowsTerminal, "PowerShell", "wt.exe", "-p \"PowerShell\""),
        ];
        static string Stored(string path) => path.Replace("E:", "%APPDRIVE%", StringComparison.Ordinal);

        var flat = ImportPlan.Build(chosen, groupPerSource: false, source => source.ToString(), Stored);
        var grouped = ImportPlan.Build(chosen, groupPerSource: true, source => $"From {source}", Stored);

        Assert.Equal(["Ubuntu", "Tool", "PowerShell"], flat.Select(i => i.Name));
        var tool = Assert.IsType<ShortcutItem>(flat[1]);
        Assert.Equal((@"%APPDRIVE%\Tools\tool.exe", null, @"%APPDRIVE%\Tools\data"), (tool.Location, tool.Parameters, tool.StartIn));
        Assert.Equal("-p \"Ubuntu\"", ((ShortcutItem)flat[0]).Parameters);
        Assert.Equal(["From WindowsTerminal", "From Desktop"], grouped.Select(i => i.Name));
        Assert.Equal(["Ubuntu", "PowerShell"], Assert.IsType<GroupItem>(grouped[0]).Items.Select(i => i.Name));
    }

    [Fact]
    public void A_shortcut_on_the_bar_has_the_key_its_candidate_has()
    {
        var candidate = new ImportCandidate(ImportSource.Putty, "web", @"C:\PuTTY\putty.exe", "-load \"web\"");
        var shortcut = new ShortcutItem { Name = "web", Location = @"C:\PuTTY\putty.exe", Parameters = " -load \"web\" " };

        Assert.Equal(candidate.Key, ImportPlan.KeyOf(shortcut));
    }
}
