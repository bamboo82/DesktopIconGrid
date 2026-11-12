using System.Windows.Input;
using DesktopIconGrid.Core.Models;

namespace DesktopIconGrid.Services;

/// <summary>
/// The bar window's keys as the settings give them (<see cref="WindowKeys"/>): which action a pressed combination
/// starts, and the text a menu shows for an action. A key that cannot be read, or that two actions share, starts only
/// the first action that has it.
/// </summary>
public sealed class WindowKeyMap
{
    private readonly Dictionary<(ModifierKeys, Key), WindowKeyAction> _actions = [];
    private readonly Dictionary<WindowKeyAction, string> _texts = [];

    private WindowKeyMap()
    {
    }

    public static WindowKeyMap From(LauncherSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var map = new WindowKeyMap();
        foreach (var action in Enum.GetValues<WindowKeyAction>())
        {
            // An override that cannot be read (hand-edited, or a key name of a newer version) is no override: the
            // action keeps its default. Only an empty one turns it off.
            var text = WindowKeys.For(settings, action);
            if (!HotkeyGesture.TryParseWindowKey(text, out var gesture)
                && (text.Length == 0 || !HotkeyGesture.TryParseWindowKey(WindowKeys.Defaults[action], out gesture)))
            {
                continue;
            }

            // A key two actions share starts the first; the other has none, and its menu shows none.
            if (map._actions.TryAdd((gesture.Modifiers, gesture.Key), action))
            {
                map._texts[action] = gesture.ToString();
            }
        }

        // Shift+Delete removes as Delete does, as it did before the keys could be changed, while Delete is the key.
        if (map._actions.TryGetValue((ModifierKeys.None, Key.Delete), out var plain) && plain == WindowKeyAction.Remove)
        {
            map._actions.TryAdd((ModifierKeys.Shift, Key.Delete), WindowKeyAction.Remove);
        }

        return map;
    }

    /// <summary>
    /// Whether a combination can be the bar's while a text box has the focus: Ctrl or Alt is held (AltGr, which is
    /// both, types a character), and the key is none a text box edits with.
    /// </summary>
    public static bool LeavesTextBoxAlone(Key key, ModifierKeys modifiers)
    {
        var control = modifiers.HasFlag(ModifierKeys.Control);
        var alt = modifiers.HasFlag(ModifierKeys.Alt);
        if (control == alt)
        {
            return false; // neither, or AltGr
        }

        return key is not (Key.A or Key.C or Key.V or Key.X or Key.Y or Key.Z or Key.Left or Key.Right or Key.Up or Key.Down
            or Key.Home or Key.End or Key.Back or Key.Delete or Key.Insert or Key.Space or Key.Tab or Key.Enter or Key.Escape);
    }

    /// <summary>The action <paramref name="key"/> with <paramref name="modifiers"/> starts, or <see langword="null"/>.</summary>
    public WindowKeyAction? ActionFor(Key key, ModifierKeys modifiers) =>
        _actions.TryGetValue((modifiers, key), out var action) ? action : null;

    /// <summary>What a menu shows next to the action (<c>Ctrl+N</c>); empty when it has no key.</summary>
    public string TextFor(WindowKeyAction action) => _texts.TryGetValue(action, out var text) ? text : string.Empty;

    /// <summary>The texts by action name, for menus to bind to.</summary>
    public IReadOnlyDictionary<string, string> Texts =>
        Enum.GetValues<WindowKeyAction>().ToDictionary(action => action.ToString(), TextFor);
}
