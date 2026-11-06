using DesktopIconGrid.Core.Imports;
using DesktopIconGrid.Services;

namespace DesktopIconGrid.App.Tests;

/// <summary>
/// The real Start menu list through the COM declarations (<c>IShellItem</c>, <c>IEnumShellItems</c>, the property store
/// of each registered id). A wrong slot or GUID shows as an empty list, since every failure there is caught and logged.
/// </summary>
[Collection(WpfWindowsCollection.Name)]
public sealed class InstalledAppsTests(WpfThread wpf)
{
    [Fact]
    public void The_Start_menu_lists_programs_and_desktop_programs_are_stored_by_their_path()
    {
        IReadOnlyList<InstalledApp> apps = [];
        wpf.Invoke(() => apps = InstalledApps.Enumerate()); // an STA thread, as the icon worker is

        Assert.NotEmpty(apps);
        Assert.All(apps, app => Assert.False(string.IsNullOrWhiteSpace(app.Name) || string.IsNullOrWhiteSpace(app.Location), app.ToString()));
        Assert.Contains(apps, app => !app.IsStoreApp && app.Location.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && app.Location[1] == ':');
    }
}
