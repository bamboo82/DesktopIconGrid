using DesktopIconGrid.Core.Launching;

namespace DesktopIconGrid.Core.Tests;

/// <summary>A Store app's frame carries <c>Family!AppId</c>; a desktop program's id has no family.</summary>
public class AppUserModelIdTests
{
    [Theory]
    [InlineData("Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", "Microsoft.WindowsCalculator_8wekyb3d8bbwe")]
    [InlineData("Microsoft.WindowsTerminal_8wekyb3d8bbwe!App", "Microsoft.WindowsTerminal_8wekyb3d8bbwe")]
    [InlineData("Microsoft.Windows.Explorer", null)] // a desktop program's own id
    [InlineData("Contoso.App_1.0_x64!App", null)] // a full package name, not a family
    [InlineData("!App", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void The_family_is_the_part_before_the_bang(string? appUserModelId, string? family)
    {
        Assert.Equal(family, PackagedApps.FamilyOfAppUserModelId(appUserModelId));
    }
}

/// <summary>Store apps run from package folders, so their processes are matched by package family.</summary>
public class PackagedAppsTests
{
    private const string Calculator = "Microsoft.WindowsCalculator_8wekyb3d8bbwe";

    [Theory]
    [InlineData(@"shell:AppsFolder\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", Calculator)]
    [InlineData(@"  SHELL:appsfolder\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App  ", Calculator)]
    [InlineData(@"shell:AppsFolder\{6D809377-6AF0-444B-8957-A3773F02200E}\Tool\tool.exe", null)]
    [InlineData(@"shell:AppsFolder\Microsoft.Office.WINWORD.EXE.15", null)]
    [InlineData(@"shell:AppsFolder\NotAFamily!App", null)]
    [InlineData("shell:Desktop", null)]
    [InlineData(@"C:\Windows\System32\calc.exe", null)]
    [InlineData(null, null)]
    public void Store_apps_from_the_Apps_folder_carry_their_package_family(string? location, string? family)
    {
        Assert.Equal(family, PackagedApps.FamilyOfAppsFolderItem(location));
    }

    [Theory]
    [InlineData(@"C:\Program Files\WindowsApps\Microsoft.WindowsCalculator_11.2405.2.0_x64__8wekyb3d8bbwe\CalculatorApp.exe", true)]
    [InlineData(@"c:\program files\windowsapps\microsoft.windowscalculator_11.2405.2.0_arm64__8wekyb3d8bbwe\sub\x.exe", true)]
    [InlineData(@"C:\Program Files\WindowsApps\Microsoft.WindowsCalculatorPro_1.0.0.0_x64__8wekyb3d8bbwe\x.exe", false)]
    [InlineData(@"C:\Program Files\WindowsApps\Microsoft.WindowsCalculator_11.2405.2.0_x64__otherpublisher\x.exe", false)]
    [InlineData(@"C:\Tools\Microsoft.WindowsCalculator_11.2405.2.0_x64__8wekyb3d8bbwe\x.exe", false)]
    [InlineData(@"C:\Windows\System32\calc.exe", false)]
    public void Processes_are_in_a_family_when_they_run_from_one_of_its_package_folders(string imagePath, bool inFamily)
    {
        Assert.Equal(inFamily, PackagedApps.IsInFamily(imagePath, Calculator));
    }

    [Fact]
    public void Inbox_programs_replaced_by_Store_apps_map_to_their_package_only_in_the_Windows_folders()
    {
        string[] windowsFolders = [@"C:\Windows", @"C:\Windows\System32\", @"C:\Windows\SysWOW64"];

        Assert.Equal(Calculator, PackagedApps.ReplacementFamily(@"C:\Windows\System32\calc.exe", windowsFolders));
        Assert.Equal("Microsoft.WindowsNotepad_8wekyb3d8bbwe", PackagedApps.ReplacementFamily(@"c:\windows\NOTEPAD.EXE", windowsFolders));
        Assert.Null(PackagedApps.ReplacementFamily(@"C:\Tools\calc.exe", windowsFolders));
        Assert.Null(PackagedApps.ReplacementFamily(@"C:\Windows\System32\cmd.exe", windowsFolders));
        Assert.All(PackagedApps.InboxReplacements.Values, family => Assert.True(PackagedApps.IsFamilyName(family), family));
    }
}
