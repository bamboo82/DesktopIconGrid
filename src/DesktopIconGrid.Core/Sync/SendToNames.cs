using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Core.SpecialIcons;

namespace DesktopIconGrid.Core.Sync;

/// <summary>
/// The other PCs by their names, the one name each PC is given on its own Settings tab (0.14.0 A3): how a list of them is
/// shown, and how a Send to PC icon follows a PC that was renamed.
/// </summary>
public static class SendToNames
{
    /// <summary>The setting of a Send to PC icon that holds the id of the PC it sends to.</summary>
    public const string DeviceIdKey = "deviceId";

    /// <summary>The setting that holds that PC's name as it was when it was chosen, or last seen.</summary>
    public const string DeviceNameKey = "deviceName";

    /// <summary>
    /// What a list of PCs shows for each: its name, and after a name that two or more of them have (names are allowed to
    /// be the same: the id tells the PCs apart), the first four characters of its id, <c>NOTEBOOK (3fa9)</c>.
    /// </summary>
    public static IReadOnlyList<string> Labels(IReadOnlyList<InboxDevice> devices)
    {
        ArgumentNullException.ThrowIfNull(devices);
        var shared = devices.GroupBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        return [.. devices.Select(device => shared.Contains(device.Name) && device.Id.Length >= 4 ? $"{device.Name} ({device.Id[..4]})" : device.Name)];
    }

    /// <summary>
    /// Follows a renamed PC on a Send to PC <paramref name="icon"/>: when the PC it sends to is in <paramref name="listed"/>
    /// under another name than the one the icon keeps, the kept name becomes that one, and so does the icon's caption
    /// while it is the one made from the old name with <paramref name="captionFormat"/> (<c>Send to {0}</c>); a caption
    /// typed by hand stays. Nothing else is touched, not even the time the icon was changed, so every PC that sees the
    /// same new name writes the same icon, and the next look finds nothing to change. Returns whether anything changed.
    /// </summary>
    public static bool Follow(SpecialIconItem icon, IReadOnlyList<InboxDevice> listed, string captionFormat)
    {
        ArgumentNullException.ThrowIfNull(icon);
        ArgumentNullException.ThrowIfNull(listed);
        ArgumentNullException.ThrowIfNull(captionFormat);
        if (!string.Equals(icon.Kind, SpecialIconKinds.SendToDevice, StringComparison.OrdinalIgnoreCase)
            || icon.GetSetting(DeviceIdKey) is not { } id
            || !FolderInbox.IsDeviceId(id)
            || listed.FirstOrDefault(device => string.Equals(device.Id, id, StringComparison.OrdinalIgnoreCase)) is not { } now)
        {
            return false; // not such an icon, no PC chosen, or that PC is not listed (it does not receive now): nothing known
        }

        var stored = icon.GetSetting(DeviceNameKey);
        var newName = FolderInbox.ShownName(now.Name, id);
        if (string.Equals(stored, newName, StringComparison.Ordinal))
        {
            return false;
        }

        var oldName = FolderInbox.ShownName(stored, id);
        if (string.Equals(icon.Name, string.Format(System.Globalization.CultureInfo.CurrentCulture, captionFormat, oldName), StringComparison.Ordinal))
        {
            icon.Name = string.Format(System.Globalization.CultureInfo.CurrentCulture, captionFormat, newName);
        }

        icon.SetSetting(DeviceNameKey, newName);
        return true;
    }
}
