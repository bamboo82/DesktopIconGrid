using DesktopIconGrid.Core.Models;

namespace DesktopIconGrid.Core.SpecialIcons;

/// <summary>
/// The pictures of special icons that have no drawing of their own (0.12.0, the review session's design A3): one-colour
/// symbols that fit the tile, like the rest of the bar, in place of the colour emoji of 0.10.0 and 0.11.0. An icon
/// that still has exactly the emoji an older version gave it is drawn with the symbol and is not rewritten in the
/// layout (<see cref="IsOlderDefault"/>); a text icon chosen in any other way is left alone.
/// </summary>
public static class BuiltInTextIcons
{
    /// <summary>Send to PC: the paper plane (Send, E724).</summary>
    public static TextIcon SendToDevice { get; } = SymbolCatalog.Icon(SymbolCatalog.Find("")!);

    /// <summary>Voice: the microphone (E720).</summary>
    public static TextIcon Voice { get; } = SymbolCatalog.Icon(SymbolCatalog.Find("")!);

    /// <summary>What 0.10.0 and 0.11.0 stored for a new Send to PC icon: the outbox emoji at 22.</summary>
    private static readonly TextIcon OlderSendToDevice = new("\U0001F4E4", "Segoe UI Emoji", 22);

    /// <summary>What 0.11.0 stored for a new Voice icon: the microphone emoji at 22.</summary>
    private static readonly TextIcon OlderVoice = new("\U0001F3A4", "Segoe UI Emoji", 22);

    /// <summary>The picture special icons of <paramref name="kind"/> have when they have no text icon, if any.</summary>
    public static TextIcon? For(string? kind) => kind switch
    {
        SpecialIconKinds.SendToDevice => SendToDevice,
        SpecialIconKinds.Voice => Voice,
        _ => null,
    };

    /// <summary>
    /// Whether <paramref name="icon"/> is exactly what an older version stored for a new icon of <paramref name="kind"/>
    /// (that text, that font, size 22, not bold, no colour): it is drawn as <see cref="For"/> instead.
    /// </summary>
    public static bool IsOlderDefault(string? kind, TextIcon? icon) => icon is { ExtensionData: null } && kind switch
    {
        SpecialIconKinds.SendToDevice => icon == OlderSendToDevice,
        SpecialIconKinds.Voice => icon == OlderVoice,
        _ => false,
    };
}
