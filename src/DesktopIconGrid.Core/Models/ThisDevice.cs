namespace DesktopIconGrid.Core.Models;

/// <summary>
/// The PC and user the app runs on, by <see cref="MachineKey"/> (the sync device id only when that cannot be worked out).
/// Set once at startup; shortcuts use it to find their own path on this PC (<see cref="ShortcutItem.Locations"/>).
/// </summary>
public static class ThisDevice
{
    public static string? Id { get; set; }
}
