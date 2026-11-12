namespace DesktopIconGrid.Core.Models;

/// <summary>
/// The PC and user the app runs on, by <see cref="MachineKey"/> (the sync device id only when that cannot be worked out).
/// Set once at startup; shortcuts use it to find their own path on this PC (<see cref="ShortcutItem.Locations"/>).
/// </summary>
public static class ThisDevice
{
    public static string? Id { get; set; }

    /// <summary>
    /// Whether <see cref="Id"/> comes from this PC itself (Windows' machine key). False when that could not be read and
    /// the id is the data folder's, which a portable copy carries from PC to PC: what is approved for "this PC" then
    /// holds only until the program ends (<see cref="SpecialIcons.CommandTrust"/>).
    /// </summary>
    public static bool IsOfThisMachine { get; set; } = true;
}
