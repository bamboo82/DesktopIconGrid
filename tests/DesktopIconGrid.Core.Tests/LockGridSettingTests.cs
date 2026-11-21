using System.Text.Json;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Core.Storage;

namespace DesktopIconGrid.Core.Tests;

/// <summary>0.15.0 WP1: 鎖定格線 is a setting of the bar's, saved and synced with the others.</summary>
public class LockGridSettingTests
{
    [Fact]
    public void The_lock_is_off_by_default_written_as_lockGrid_and_copied_as_sync_copies_settings()
    {
        Assert.False(new LauncherSettings().LockGrid);

        var json = JsonSerializer.Serialize(new LauncherSettings { LockGrid = true }, LayoutStorage.JsonOptions);
        using (var document = JsonDocument.Parse(json))
        {
            Assert.True(document.RootElement.GetProperty("lockGrid").GetBoolean());
        }

        var copy = new LauncherSettings();
        copy.CopyFrom(JsonSerializer.Deserialize<LauncherSettings>(json, LayoutStorage.JsonOptions)!);
        Assert.True(copy.LockGrid);
        Assert.True(copy.Clone().LockGrid);

        copy.CopyFrom(new LauncherSettings()); // another PC unlocked it
        Assert.False(copy.LockGrid);
    }
}
