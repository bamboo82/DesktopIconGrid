using DesktopIconGrid.Core.Imports;
using DesktopIconGrid.Core.Models;

namespace DesktopIconGrid.Core.Tests;

/// <summary>Import…: which candidates may go on the bar, and what the chosen ones become.</summary>
[Collection("This device")] // these set ThisDevice.Id, which is process-wide
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
    [InlineData(ImportSource.WinScp, "web", @"C:\WinSCP\WinSCP.exe", true)]
    [InlineData(ImportSource.WinScp, "/script=C:\\x.txt", @"C:\WinSCP\WinSCP.exe", false)] // review 19 I1: a WinSCP switch once the quotes are gone
    [InlineData(ImportSource.Putty, "/log=x", "putty.exe", false)]
    [InlineData(ImportSource.WindowsTerminal, "Ubuntu\\", "wt.exe", false)] // I3: would escape its closing quote
    [InlineData(ImportSource.Putty, "%USERPROFILE%", "putty.exe", false)] // I4: expanded again at every click
    [InlineData(ImportSource.Putty, "web\uFF02x", "putty.exe", false)] // the fullwidth quote
    [InlineData(ImportSource.WinScp, "folder/web", @"C:\WinSCP\WinSCP.exe", true)] // WinSCP's own folder separator, inside
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
    public void A_session_whose_stored_name_decodes_to_a_switch_is_not_offered()
    {
        // Review 19 I1: [Sessions\%2Fscript=…] in a portable WinSCP.ini decodes to "/script=…".
        var names = ImportSources.SessionNames(ImportSources.WinScpIniSessions("[Sessions\\%2Fscript=C:%5Cx.txt]\n[Sessions\\web]\n").Select(Uri.EscapeDataString));

        Assert.Equal(["/script=C:\\x.txt", "web"], names);
        Assert.Equal(["web"], names.Select(n => new ImportCandidate(ImportSource.WinScp, n, "WinSCP.exe", $"\"{n}\"")).Where(ImportPlan.IsUsable).Select(c => c.Name));
    }

    [Fact]
    public void A_pin_with_a_variable_in_its_target_matches_the_shortcut_on_the_bar_both_ways()
    {
        // Review 19 I2: a pin's raw target (%windir%\…) never matched, so each Import offered it again.
        var windir = Environment.GetEnvironmentVariable("windir") is { Length: > 0 } w ? w : null;
        var variable = windir is null ? "%HOME%" : "%windir%";
        var expanded = Environment.ExpandEnvironmentVariables(variable);
        var candidate = new ImportCandidate(ImportSource.TaskbarPins, "Remote Desktop", variable + @"\system32\mstsc.exe");

        Assert.Equal(candidate.Key, ImportPlan.KeyOf(new ShortcutItem { Name = "R", Location = expanded + @"\system32\mstsc.exe" }));
        Assert.Equal(candidate.Key, ImportPlan.KeyOf(new ShortcutItem { Name = "R", Location = variable + @"\system32\mstsc.exe" }));
    }

    [Fact]
    public void This_PCs_own_location_is_the_one_compared()
    {
        var previous = ThisDevice.Id;
        ThisDevice.Id = "this-pc";
        try
        {
            var shortcut = new ShortcutItem { Name = "web", Location = @"D:\PuTTY\putty.exe", Parameters = "-load \"web\"", Locations = new() { ["this-pc"] = @"C:\PuTTY\putty.exe" } };

            Assert.Equal(new ImportCandidate(ImportSource.Putty, "web", @"C:\PuTTY\putty.exe", "-load \"web\"").Key, ImportPlan.KeyOf(shortcut));
        }
        finally
        {
            ThisDevice.Id = previous;
        }
    }

    [Fact]
    public void A_shortcut_on_the_bar_has_the_key_its_candidate_has()
    {
        var candidate = new ImportCandidate(ImportSource.Putty, "web", @"C:\PuTTY\putty.exe", "-load \"web\"");
        var shortcut = new ShortcutItem { Name = "web", Location = @"C:\PuTTY\putty.exe", Parameters = " -load \"web\" " };

        Assert.Equal(candidate.Key, ImportPlan.KeyOf(shortcut));
    }
}
