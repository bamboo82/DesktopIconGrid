using DesktopIconGrid.Core.Diagnostics;
using DesktopIconGrid.Resources;

namespace DesktopIconGrid.Services;

/// <summary>
/// The app's system-wide hotkeys (show/hide, then Show at pointer), applied together from the settings. Windows refuses
/// a combination that any hotkey holds, the app's own other one included, so a combination that one of them holds and
/// another one wants (the two swapped in one save) is let go of before anything is registered. What each one holds is
/// tracked as it really is, also after a refusal: setting a hotkey back to the combination it still holds changes
/// nothing. A refusal is shown once, not again on every later save while the setting stays the same; it is still
/// tried again then, and works once the other program has let the combination go.
/// </summary>
internal sealed class HotkeySet
{
    private readonly GlobalHotkey[] _hotkeys;
    private readonly string[] _names;
    private readonly HotkeyGesture?[] _held;
    private readonly string?[] _shown;

    /// <param name="hotkeys">In the order they get their combinations; the names are for the log.</param>
    public HotkeySet(params (GlobalHotkey Hotkey, string Name)[] hotkeys)
    {
        ArgumentNullException.ThrowIfNull(hotkeys);
        _hotkeys = [.. hotkeys.Select(h => h.Hotkey)];
        _names = [.. hotkeys.Select(h => h.Name)];
        _held = new HotkeyGesture?[hotkeys.Length];
        _shown = new string?[hotkeys.Length];
    }

    /// <summary>The combination each hotkey holds now.</summary>
    public IReadOnlyList<HotkeyGesture?> Held => _held;

    /// <summary>
    /// Gives each hotkey its combination from <paramref name="wanted"/> (an empty or unreadable text clears it). Returns,
    /// for each, the refusal to show, or <see langword="null"/>: there is none, or it was shown before for this setting.
    /// </summary>
    public IReadOnlyList<string?> Apply(IReadOnlyList<string?> wanted)
    {
        ArgumentNullException.ThrowIfNull(wanted);
        if (wanted.Count != _hotkeys.Length)
        {
            throw new ArgumentException("One combination per hotkey.", nameof(wanted));
        }

        var count = _hotkeys.Length;
        var gestures = new HotkeyGesture?[count];
        var refusals = new string?[count];
        for (var i = 0; i < count; i++)
        {
            gestures[i] = HotkeyGesture.TryParse(wanted[i], out var gesture) ? gesture : null;
            if (gestures[i] is { } same && Array.IndexOf(gestures, same, 0, i) >= 0)
            {
                // The same combination twice (only a hand-edited settings file; the dialog refuses it): the first keeps it.
                gestures[i] = null;
                refusals[i] = i >= 2 ? Strings.Settings_VoiceHotkeySame : Strings.Settings_PointerHotkeySame;
            }
        }

        // Let go of every combination another hotkey wants first, so that the order of registering cannot matter.
        for (var i = 0; i < count; i++)
        {
            if (_held[i] is { } held && held != gestures[i] && Array.IndexOf(gestures, held) >= 0)
            {
                _hotkeys[i].Unregister();
                _held[i] = null;
                AppLog.Default.Debug("Hotkey", $"{_names[i]} lets go of {held}: the other hotkey wants it");
            }
        }

        for (var i = 0; i < count; i++)
        {
            if (_held[i] == gestures[i])
            {
                continue;
            }

            if (_hotkeys[i].Register(gestures[i], out var error))
            {
                _held[i] = gestures[i];
                AppLog.Default.Info("Hotkey", gestures[i] is { } registered ? $"{_names[i]} registered: {registered}" : $"{_names[i]} cleared");
            }
            else
            {
                refusals[i] = error ?? string.Empty;
                AppLog.Default.Warning("Hotkey", $"{_names[i]} not changed: {error}");
            }
        }

        for (var i = 0; i < count; i++)
        {
            var setting = wanted[i]?.Trim() ?? string.Empty;
            if (refusals[i] is null)
            {
                _shown[i] = null;
            }
            else if (string.Equals(_shown[i], setting, StringComparison.Ordinal))
            {
                refusals[i] = null; // shown before for this very setting: tried again and logged, not shown again
            }
            else
            {
                _shown[i] = setting;
            }
        }

        return refusals;
    }
}
