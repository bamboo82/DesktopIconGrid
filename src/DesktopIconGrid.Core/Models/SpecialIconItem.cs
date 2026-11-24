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

    /// <summary>
    /// How the widget's own text is drawn (0.15.0 WP7b): the clock's time, a gauge's number, the command tile's output, set
    /// in the text icon window's Widget mode (文字外觀…). <see langword="null"/> is the text as before. Only the handlers
    /// that draw text of their own read it; a version before 0.15.0 keeps it unread with the item. It is no part of what a
    /// command tile's approval covers: it changes how its output looks, not what runs.
    /// </summary>
    public TextLook? TextLook { get; set; }

    /// <summary>
    /// How the widget's name is drawn (0.16.0 WP4, the owner's request): the name under the tile, with its font, size, bold,
    /// colour, plate and up or down as a row line's caption has them; and the caption inside the tile, which takes its font,
    /// colour and bold in place of <see cref="TextLook"/>'s. <see langword="null"/> is the name as before, the value's font and
    /// colour reaching that caption included. Set in the same window as <see cref="TextLook"/>, its 名稱 part; synced with
    /// the item, kept unread by a version before 0.16.0, and, like it, no part of a command tile's approval.
    /// </summary>
    public TextLook? NameLook { get; set; }

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
            TextLook = special.TextLook?.Normalized();
            NameLook = special.NameLook?.Normalized();
        }
    }
}
