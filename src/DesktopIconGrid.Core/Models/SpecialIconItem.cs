namespace DesktopIconGrid.Core.Models;

/// <summary>
/// An active icon with behaviour of its own (a CPU gauge, a disk gauge, a Google Drive bucket…).
/// <see cref="Kind"/> selects the handler in the app; <see cref="Settings"/> is a flat string map so
/// the layout file stays independent of individual kinds. Unknown kinds are kept and shown as placeholders.
/// </summary>
public sealed class SpecialIconItem : LauncherItem
{
    public string Kind { get; set; } = string.Empty;

    public Dictionary<string, string> Settings { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public string? GetSetting(string key) =>
        Settings.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    public void SetSetting(string key, string? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (string.IsNullOrWhiteSpace(value))
        {
            Settings.Remove(key);
        }
        else
        {
            Settings[key] = value.Trim();
        }
    }

    protected override LauncherItem CloneCore()
    {
        var copy = (SpecialIconItem)MemberwiseClone();
        copy.Settings = new Dictionary<string, string>(Settings, StringComparer.OrdinalIgnoreCase);
        return copy;
    }

    public override void CopyContentFrom(LauncherItem source)
    {
        base.CopyContentFrom(source);
        if (source is SpecialIconItem special)
        {
            Kind = special.Kind.Trim();
            Settings = new Dictionary<string, string>(special.Settings, StringComparer.OrdinalIgnoreCase);
        }
    }
}
