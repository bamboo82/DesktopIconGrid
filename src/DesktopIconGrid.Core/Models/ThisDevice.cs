namespace DesktopIconGrid.Core.Models;

/// <summary>
/// The installation the app runs on, by the id that sync records for it (<see cref="Sync.SyncSettings.DeviceId"/>).
/// Set once at startup; shortcuts use it to find their own path on this PC (<see cref="ShortcutItem.Locations"/>).
/// </summary>
public static class ThisDevice
{
    public static string? Id { get; set; }
}
