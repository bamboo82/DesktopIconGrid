using DesktopIconGrid.Core.Imports;
using DesktopIconGrid.Core.Launching;
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

    [Fact]
    public void A_registered_id_is_read_through_its_property_store_as_Edge_shows()
    {
        // The review of round 12: the test above passes on any {known folder} entry, even if the property store read nothing.
        (string? Target, string? Arguments) edge = default;
        IReadOnlyList<InstalledApp> apps = [];
        wpf.Invoke(() =>
        {
            edge = InstalledApps.LinkOf("MSEdge"); // Edge's registered id; Edge is on every Windows image CI runs on
            apps = InstalledApps.Enumerate();
        });

        Assert.True(AppsFolderEntry.NeedsLinkTarget("MSEdge"));
        Assert.EndsWith(@"\msedge.exe", edge.Target ?? "(nothing read)", StringComparison.OrdinalIgnoreCase);
        var stored = AppsFolderEntry.ProgramOf(edge.Target, edge.Arguments) ?? PackagedApps.AppsFolderPrefix + "MSEdge"; // its arguments decide
        Assert.Contains(apps, app => string.Equals(app.Location, stored, StringComparison.OrdinalIgnoreCase));
    }
}
