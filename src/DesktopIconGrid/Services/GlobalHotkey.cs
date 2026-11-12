using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Input;
using System.Windows.Interop;
using DesktopIconGrid.Resources;

namespace DesktopIconGrid.Services;

/// <summary>A key combination such as Ctrl+Alt+Space, stored as text in the settings.</summary>
public sealed record HotkeyGesture(ModifierKeys Modifiers, Key Key)
{
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;

    /// <summary>At least one of Ctrl, Alt or Win is required so ordinary typing cannot trigger the hotkey.</summary>
    public bool HasUsableModifier => (Modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) != 0;

    public uint NativeModifiers =>
        (Modifiers.HasFlag(ModifierKeys.Alt) ? ModAlt : 0)
        | (Modifiers.HasFlag(ModifierKeys.Control) ? ModControl : 0)
        | (Modifiers.HasFlag(ModifierKeys.Shift) ? ModShift : 0)
        | (Modifiers.HasFlag(ModifierKeys.Windows) ? ModWin : 0);

    public uint VirtualKey => (uint)KeyInterop.VirtualKeyFromKey(Key);

    /// <summary>
    /// The modifier keys held now, the Windows keys included: WPF's <see cref="Keyboard.Modifiers"/> only ever reports
    /// Alt, Ctrl and Shift.
    /// </summary>
    public static ModifierKeys HeldModifiers() =>
        Keyboard.Modifiers | (Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin) ? ModifierKeys.Windows : ModifierKeys.None);

    /// <summary>Whether two written combinations are the same keys, however they are spelled ("ctrl+f", "Control+F").</summary>
    public static bool SameCombination(string? a, string? b) =>
        TryParseAny(a, out var first) && TryParseAny(b, out var second) && first == second;

    public static bool IsModifierKey(Key key) => key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
        or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System or Key.None;

    /// <summary>Parses "Ctrl+Alt+Space", "Win+Shift+F12", "Control+Alt+D1" and similar; a system-wide hotkey needs Ctrl, Alt or Win.</summary>
    public static bool TryParse(string? text, out HotkeyGesture gesture) => TryParseAny(text, out gesture) && gesture.HasUsableModifier;

    /// <summary>A key of the bar's window (<see cref="Core.Models.WindowKeys"/>): as <see cref="TryParse"/>, but see <see cref="IsAllowedInWindow"/>.</summary>
    public static bool TryParseWindowKey(string? text, out HotkeyGesture gesture) => TryParseAny(text, out gesture) && gesture.IsAllowedInWindow;

    /// <summary>
    /// Whether the combination may be a key of the bar's window: with Ctrl, Alt or Win, or an F-key, Delete or Insert
    /// alone (a letter alone types into the bar's search). Never Escape, Tab, Enter, Space, Backspace or an arrow, which
    /// the window and its dialogs need.
    /// </summary>
    public bool IsAllowedInWindow =>
        Key is not (Key.Escape or Key.Tab or Key.Enter or Key.Space or Key.Back or Key.Left or Key.Right or Key.Up or Key.Down or Key.None)
        && !IsModifierKey(Key)
        && (HasUsableModifier || Key is (>= Key.F1 and <= Key.F24) or Key.Delete or Key.Insert);

    private static bool TryParseAny(string? text, out HotkeyGesture gesture)
    {
        gesture = new HotkeyGesture(ModifierKeys.None, Key.None);
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var modifiers = ModifierKeys.None;
        var key = Key.None;
        foreach (var rawToken in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (rawToken.ToLowerInvariant())
            {
                case "ctrl" or "control":
                    modifiers |= ModifierKeys.Control;
                    continue;
                case "alt":
                    modifiers |= ModifierKeys.Alt;
                    continue;
                case "shift":
                    modifiers |= ModifierKeys.Shift;
                    continue;
                case "win" or "windows":
                    modifiers |= ModifierKeys.Windows;
                    continue;
            }

            if (key != Key.None || !TryParseKey(rawToken, out key))
            {
                return false;
            }
        }

        if (key == Key.None || IsModifierKey(key))
        {
            return false;
        }

        gesture = new HotkeyGesture(modifiers, key);
        return true;
    }

    public override string ToString()
    {
        var builder = new StringBuilder();
        if (Modifiers.HasFlag(ModifierKeys.Control))
        {
            builder.Append("Ctrl+");
        }

        if (Modifiers.HasFlag(ModifierKeys.Alt))
        {
            builder.Append("Alt+");
        }

        if (Modifiers.HasFlag(ModifierKeys.Shift))
        {
            builder.Append("Shift+");
        }

        if (Modifiers.HasFlag(ModifierKeys.Windows))
        {
            builder.Append("Win+");
        }

        builder.Append(KeyName(Key));
        return builder.ToString();
    }

    private static string KeyName(Key key) => key switch
    {
        >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
        Key.OemComma => ",",
        Key.OemPeriod => ".",
        Key.OemMinus => "-",
        Key.OemPlus => "=",
        Key.OemQuestion => "/",
        Key.OemSemicolon => ";",
        Key.OemQuotes => "'",
        Key.OemTilde => "`",
        Key.OemOpenBrackets => "[",
        Key.OemCloseBrackets => "]",
        Key.OemPipe => "\\",
        _ => key.ToString(),
    };

    private static bool TryParseKey(string token, out Key key)
    {
        key = token switch
        {
            "," => Key.OemComma,
            "." => Key.OemPeriod,
            "-" => Key.OemMinus,
            "=" => Key.OemPlus,
            "/" => Key.OemQuestion,
            ";" => Key.OemSemicolon,
            "'" => Key.OemQuotes,
            "`" => Key.OemTilde,
            "[" => Key.OemOpenBrackets,
            "]" => Key.OemCloseBrackets,
            "\\" => Key.OemPipe,
            _ => Key.None,
        };
        if (key != Key.None)
        {
            return true;
        }

        if (token.Length == 1 && char.IsDigit(token[0]))
        {
            key = Key.D0 + (token[0] - '0');
            return true;
        }

        return Enum.TryParse(token, ignoreCase: true, out key) && key != Key.None;
    }
}

/// <summary><c>RegisterHotKey</c> and <c>UnregisterHotKey</c> for one window; tests stand in for Windows with a fake.</summary>
internal interface IHotkeyRegistrar
{
    /// <summary>Registers the combination under <paramref name="id"/>; false with the Win32 error code when Windows refuses.</summary>
    bool Register(int id, uint modifiers, uint virtualKey, out int errorCode);

    void Unregister(int id);
}

/// <summary>
/// Registers one system-wide hotkey on a window and raises <see cref="Pressed"/> when it fires. Each hotkey of a
/// window takes two ids of its own (<see cref="ShowHideIds"/>, <see cref="PointerIds"/>): a new combination is
/// registered under the spare id before the old one is let go. Windows refuses a combination that any hotkey holds,
/// the app's own other one included; <see cref="HotkeySet"/> applies the app's hotkeys together for that reason.
/// </summary>
public sealed class GlobalHotkey : IDisposable
{
    /// <summary>The first of the two ids of the show/hide hotkey.</summary>
    public const int ShowHideIds = 0x4A11;

    /// <summary>The first of the two ids of the Show-at-pointer hotkey.</summary>
    public const int PointerIds = 0x4A21;

    /// <summary>The first of the two ids of the voice hotkey.</summary>
    public const int VoiceIds = 0x4A31;

    private const int WmHotkey = 0x0312;
    private const uint ModNoRepeat = 0x4000;
    private const int ErrorHotkeyAlreadyRegistered = 1409;

    private readonly IHotkeyRegistrar _registrar;
    private readonly HwndSource? _source;
    private readonly int _firstId;
    private readonly int _secondId;
    private int? _registeredId;
    private bool _disposed;

    public GlobalHotkey(HwndSource source, int firstId = ShowHideIds)
        : this(new WindowRegistrar(source), firstId)
    {
        _source = source;
        _source.AddHook(WndProc);
    }

    /// <summary>A hotkey on <paramref name="registrar"/> and no window: it never fires (for tests).</summary>
    internal GlobalHotkey(IHotkeyRegistrar registrar, int firstId = ShowHideIds)
    {
        _registrar = registrar;
        _firstId = firstId;
        _secondId = firstId + 1;
    }

    public event EventHandler? Pressed;

    /// <summary>
    /// Registers <paramref name="gesture"/> (or clears the hotkey when <see langword="null"/>). Returns false with a
    /// message when Windows refuses it; the combination held before stays, and the message says whether there is one.
    /// </summary>
    public bool Register(HotkeyGesture? gesture, out string? error)
    {
        error = null;
        if (gesture is null)
        {
            Unregister();
            return true;
        }

        // Register the new combination under the other id first, so a rejected gesture keeps the working one.
        var newId = _registeredId == _firstId ? _secondId : _firstId;
        if (!_registrar.Register(newId, gesture.NativeModifiers | ModNoRepeat, gesture.VirtualKey, out var code))
        {
            var state = _registeredId is null ? Strings.Hotkey_NoneActive : Strings.Hotkey_PreviousStays;
            error = code == ErrorHotkeyAlreadyRegistered
                ? string.Format(Strings.Hotkey_InUse, gesture, state)
                : string.Format(Strings.Hotkey_Failed, gesture, code, state);
            return false;
        }

        Unregister();
        _registeredId = newId;
        return true;
    }

    public void Unregister()
    {
        if (_registeredId is { } id)
        {
            _registrar.Unregister(id);
            _registeredId = null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Unregister();
        _source?.RemoveHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && _registeredId is { } id && wParam.ToInt32() == id)
        {
            Pressed?.Invoke(this, EventArgs.Empty);
            handled = true;
        }

        return IntPtr.Zero;
    }

    private sealed class WindowRegistrar(HwndSource source) : IHotkeyRegistrar
    {
        public bool Register(int id, uint modifiers, uint virtualKey, out int errorCode)
        {
            if (NativeMethods.RegisterHotKey(source.Handle, id, modifiers, virtualKey))
            {
                errorCode = 0;
                return true;
            }

            errorCode = Marshal.GetLastWin32Error();
            return false;
        }

        public void Unregister(int id) => NativeMethods.UnregisterHotKey(source.Handle, id);
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    }
}
