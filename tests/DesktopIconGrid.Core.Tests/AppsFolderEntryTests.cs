using DesktopIconGrid.Core.Imports;

namespace DesktopIconGrid.Core.Tests;

/// <summary>Add app…: what the bar stores for each kind of Start menu entry.</summary>
public class AppsFolderEntryTests
{
    private static readonly Guid ProgramFilesX86 = new("7C5A40EF-A0FB-4BFC-874A-C0F2E0B9FA8E");

    private static string? Known(Guid id) => id == ProgramFilesX86 ? @"C:\Program Files (x86)" : null;

    [Fact]
    public void A_path_below_a_known_folder_becomes_the_real_path()
    {
        Assert.Equal(
            new InstalledApp("WinSCP", Path.Combine(@"C:\Program Files (x86)", @"WinSCP\WinSCP.exe"), IsStoreApp: false),
            AppsFolderEntry.ToApp("WinSCP", "{7C5A40EF-A0FB-4BFC-874A-C0F2E0B9FA8E}\\WinSCP\\WinSCP.exe", Known));
    }

    [Fact]
    public void Store_apps_and_registered_ids_are_started_through_the_shell()
    {
        Assert.Equal(
            new InstalledApp("Calculator", @"shell:AppsFolder\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", IsStoreApp: true),
            AppsFolderEntry.ToApp(" Calculator ", "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", Known));
        Assert.Equal(
            new InstalledApp("File Explorer", @"shell:AppsFolder\Microsoft.Windows.Explorer", IsStoreApp: false),
            AppsFolderEntry.ToApp("File Explorer", "Microsoft.Windows.Explorer", Known));
        Assert.Equal( // a known folder this PC cannot resolve: left to the shell
            new InstalledApp("Tool", @"shell:AppsFolder\{11111111-2222-3333-4444-555555555555}\Tool\tool.exe", IsStoreApp: false),
            AppsFolderEntry.ToApp("Tool", @"{11111111-2222-3333-4444-555555555555}\Tool\tool.exe", Known));
    }

    [Fact]
    public void Links_stay_links_and_nameless_entries_are_skipped()
    {
        Assert.Equal(new InstalledApp("Help", "https://example.com/help", IsStoreApp: false), AppsFolderEntry.ToApp("Help", "https://example.com/help", Known));
        Assert.Null(AppsFolderEntry.ToApp("", "x!App", Known));
        Assert.Null(AppsFolderEntry.ToApp("Name", null, Known));
    }
}
