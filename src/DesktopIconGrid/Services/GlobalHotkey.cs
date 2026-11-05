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

    public static bool IsModifierKey(Key key) => key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
        or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System or Key.None;

    /// <summary>Parses "Ctrl+Alt+Space", "Win+Shift+F12", "Control+Alt+D1" and similar.</summary>
    public static bool TryParse(string? text, out HotkeyGesture gesture)
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
        return gesture.HasUsableModifier;
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

/// <summary>
/// Registers one system-wide hotkey on a window and raises <see cref="Pressed"/> when it fires. Each hotkey of a
/// window takes two ids of its own (<see cref="ShowHideIds"/>, <see cref="PointerIds"/>): a new combination is
/// registered under the spare id before the old one is let go.
/// </summary>
public sealed class GlobalHotkey : IDisposable
{
    /// <summary>The first of the two ids of the show/hide hotkey.</summary>
    public const int ShowHideIds = 0x4A11;

    /// <summary>The first of the two ids of the Show-at-pointer hotkey.</summary>
    public const int PointerIds = 0x4A21;

    private const int WmHotkey = 0x0312;
    private const uint ModNoRepeat = 0x4000;
    private const int ErrorHotkeyAlreadyRegistered = 1409;

    private readonly HwndSource _source;
    private readonly int _firstId;
    private readonly int _secondId;
    private int? _registeredId;
    private bool _disposed;

    public GlobalHotkey(HwndSource source, int firstId = ShowHideIds)
    {
        _source = source;
        _firstId = firstId;
        _secondId = firstId + 1;
        _source.AddHook(WndProc);
    }

    public event EventHandler? Pressed;

    /// <summary>Registers <paramref name="gesture"/> (or clears the hotkey when <see langword="null"/>). Returns false with a message when Windows refuses it.</summary>
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
        if (!NativeMethods.RegisterHotKey(_source.Handle, newId, gesture.NativeModifiers | ModNoRepeat, gesture.VirtualKey))
        {
            var code = Marshal.GetLastWin32Error();
            error = code == ErrorHotkeyAlreadyRegistered
                ? string.Format(Strings.Hotkey_InUse, gesture)
                : string.Format(Strings.Hotkey_Failed, gesture, code);
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
            NativeMethods.UnregisterHotKey(_source.Handle, id);
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
        _source.RemoveHook(WndProc);
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
