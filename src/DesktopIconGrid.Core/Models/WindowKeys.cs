namespace DesktopIconGrid.Core.Models;

/// <summary>What a key in the bar's window does.</summary>
public enum WindowKeyAction
{
    NewShortcut,
    NewGroup,
    OpenSettings,
    Find,
    UndoRemove,
    Remove,
    Properties,
}

/// <summary>
/// The keys of the bar's window (not the system-wide hotkeys): each action's default, and the combination the settings
/// give it instead (<see cref="LauncherSettings.WindowKeys"/>, by action name; an empty text turns it off). Keys are
/// written as the settings write hotkeys (<c>Ctrl+N</c>, <c>F2</c>, <c>Ctrl+,</c>).
/// </summary>
public static class WindowKeys
{
    public static readonly IReadOnlyDictionary<WindowKeyAction, string> Defaults = new Dictionary<WindowKeyAction, string>
    {
        [WindowKeyAction.NewShortcut] = "Ctrl+N",
        [WindowKeyAction.NewGroup] = "Ctrl+G",
        [WindowKeyAction.OpenSettings] = "Ctrl+,",
        [WindowKeyAction.Find] = "Ctrl+F",
        [WindowKeyAction.UndoRemove] = "Ctrl+Z",
        [WindowKeyAction.Remove] = "Delete",
        [WindowKeyAction.Properties] = "F2",
    };

    /// <summary>The combination <paramref name="action"/> has with <paramref name="settings"/>; empty when it is off.</summary>
    public static string For(LauncherSettings settings, WindowKeyAction action)
    {
        ArgumentNullException.ThrowIfNull(settings);
        // A null value (a damaged or hand-edited file: JSON reads it without complaint) is no override.
        return settings.WindowKeys is { } keys && keys.TryGetValue(action.ToString(), out var text) && text is not null ? text.Trim() : Defaults[action];
    }

    /// <summary>
    /// The overrides to store for the chosen keys: only what differs from the default (an action set back to its default
    /// stores nothing), or <see langword="null"/> when nothing does.
    /// </summary>
    public static Dictionary<string, string>? Overrides(IReadOnlyDictionary<WindowKeyAction, string> chosen)
    {
        ArgumentNullException.ThrowIfNull(chosen);
        var overrides = chosen
            .Where(pair => !string.Equals(pair.Value.Trim(), Defaults[pair.Key], StringComparison.OrdinalIgnoreCase))
            .ToDictionary(pair => pair.Key.ToString(), pair => pair.Value.Trim());
        return overrides.Count == 0 ? null : overrides;
    }
}
