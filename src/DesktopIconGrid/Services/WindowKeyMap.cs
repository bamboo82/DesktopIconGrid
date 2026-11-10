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
            if (HotkeyGesture.TryParseWindowKey(WindowKeys.For(settings, action), out var gesture))
            {
                map._actions.TryAdd((gesture.Modifiers, gesture.Key), action);
                map._texts[action] = gesture.ToString();
            }
        }

        return map;
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
