using System.Globalization;

namespace DesktopIconGrid.Core.Models;

/// <summary>
/// The colours of one bar theme, as <c>#RRGGBB</c> or <c>#AARRGGBB</c> strings so the core library
/// stays independent of the UI framework.
/// </summary>
public sealed record ThemePalette(
    string Id,
    string Name,
    string Background,
    string Border,
    string Foreground,
    string Hover,
    string HoverBorder,
    string Pressed,
    string Grip,
    string Accent,
    string DropTarget)
{
    private readonly string? _pointerOver;
    private readonly string? _pointerOverBorder;
    private readonly string? _focus;

    public bool IsCustom => string.Equals(Id, ThemeCatalog.CustomId, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The overlay behind an item or one of the bar's buttons under the pointer (0.15.0 WP4b): the custom theme's hover
    /// colour when it has one, else <see cref="Hover"/>. <see cref="Hover"/> stays the fill of the search box, the notice
    /// row and a text tile's frame.
    /// </summary>
    public string PointerOver
    {
        get => _pointerOver ?? Hover;
        init => _pointerOver = value;
    }

    /// <summary>The outline of an item or a separator under the pointer: from the hover colour, else <see cref="HoverBorder"/>.</summary>
    public string PointerOverBorder
    {
        get => _pointerOverBorder ?? HoverBorder;
        init => _pointerOverBorder = value;
    }

    /// <summary>
    /// The keyboard focus border, the selected match's border and the drop marker (0.15.0 WP4b): the custom theme's
    /// focus colour when it has one, else <see cref="Accent"/>, which keeps its other uses.
    /// </summary>
    public string Focus
    {
        get => _focus ?? Accent;
        init => _focus = value;
    }
}

/// <summary>Built-in themes plus the "Custom" theme derived from a user-chosen background colour.</summary>
public static class ThemeCatalog
{
    public const string ClassicId = "classic";
    public const string CustomId = "custom";

    /// <summary>The olive-grey look of the reference launcher.</summary>
    public static ThemePalette Classic { get; } = new(
        ClassicId, "Classic",
        Background: "#73806E", Border: "#333333", Foreground: "#FFFFFF",
        Hover: "#2EFFFFFF", HoverBorder: "#50FFFFFF", Pressed: "#50FFFFFF",
        Grip: "#70FFFFFF", Accent: "#4C8BF5", DropTarget: "#4A4C8BF5");

    /// <summary>Neutral dark grey.</summary>
    public static ThemePalette Graphite { get; } = new(
        "graphite", "Graphite",
        Background: "#2B2B2B", Border: "#141414", Foreground: "#EAEAEA",
        Hover: "#22FFFFFF", HoverBorder: "#40FFFFFF", Pressed: "#40FFFFFF",
        Grip: "#60FFFFFF", Accent: "#60A5FA", DropTarget: "#4A60A5FA");

    /// <summary>Warm off-white for light desktops.</summary>
    public static ThemePalette Paper { get; } = new(
        "paper", "Paper",
        Background: "#F3F2EE", Border: "#B8B6B0", Foreground: "#202020",
        Hover: "#14000000", HoverBorder: "#30000000", Pressed: "#24000000",
        Grip: "#55000000", Accent: "#2563EB", DropTarget: "#402563EB");

    /// <summary>Deep blue with a cyan accent.</summary>
    public static ThemePalette Midnight { get; } = new(
        "midnight", "Midnight",
        Background: "#1C2733", Border: "#0D141B", Foreground: "#E3ECF5",
        Hover: "#2A7DD3FC", HoverBorder: "#507DD3FC", Pressed: "#507DD3FC",
        Grip: "#6A9FB7CC", Accent: "#7DD3FC", DropTarget: "#4A7DD3FC");

    /// <summary>Beige with an amber accent.</summary>
    public static ThemePalette Sand { get; } = new(
        "sand", "Sand",
        Background: "#D8C8A4", Border: "#8A7A55", Foreground: "#2B2415",
        Hover: "#18000000", HoverBorder: "#34000000", Pressed: "#28000000",
        Grip: "#58000000", Accent: "#B45309", DropTarget: "#40B45309");

    public static IReadOnlyList<ThemePalette> Builtin { get; } = [Classic, Graphite, Paper, Midnight, Sand];

    public static ThemePalette? Find(string? id) =>
        Builtin.FirstOrDefault(t => string.Equals(t.Id, id?.Trim(), StringComparison.OrdinalIgnoreCase));

    public static bool IsKnown(string? id) =>
        Find(id) is not null || string.Equals(id?.Trim(), CustomId, StringComparison.OrdinalIgnoreCase);

    /// <summary>The palette for the settings: a built-in theme by id, or a custom one derived from the background colour.</summary>
    public static ThemePalette Resolve(LauncherSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (string.Equals(settings.Theme, CustomId, StringComparison.OrdinalIgnoreCase))
        {
            return WithHoverAndFocus(CreateCustom(settings.BackgroundColor), settings.HoverColor, settings.HoverStrength, settings.FocusColor);
        }

        return Find(settings.Theme) ?? Classic;
    }

    /// <summary>
    /// Builds a palette around a background colour: light backgrounds get dark text and dark highlights,
    /// dark backgrounds get white text and white highlights.
    /// </summary>
    public static ThemePalette CreateCustom(string? backgroundHex)
    {
        var background = LauncherSettings.IsValidColor(backgroundHex)
            ? backgroundHex!.ToUpperInvariant()
            : LauncherSettings.DefaultBackgroundColor;
        var light = IsLight(background);

        return new ThemePalette(
            CustomId, "Custom",
            Background: background,
            Border: Darken(background, 0.5),
            Foreground: ForegroundOn(background),
            Hover: light ? "#14000000" : "#2EFFFFFF",
            HoverBorder: light ? "#30000000" : "#50FFFFFF",
            Pressed: light ? "#24000000" : "#50FFFFFF",
            Grip: light ? "#55000000" : "#70FFFFFF",
            Accent: light ? "#2563EB" : "#4C8BF5",
            DropTarget: light ? "#402563EB" : "#4A4C8BF5");
    }

    /// <summary>
    /// The custom theme's own hover and focus colours over the ones derived from its background (0.15.0 WP4b). The hover
    /// colour lies under the pointer at its strength (else at the derived overlay's), and the item's outline and the
    /// pressed state are stronger by the derived ratios. The focus colour borders keyboard focus, the selected match and
    /// the drop marker, and fills a drop target at the derived alpha. Without them, the palette is the derived one.
    /// </summary>
    public static ThemePalette WithHoverAndFocus(ThemePalette custom, string? hoverHex, int? hoverStrength, string? focusHex)
    {
        ArgumentNullException.ThrowIfNull(custom);
        var palette = custom;
        if (LauncherSettings.IsValidColor(hoverHex))
        {
            var derived = AlphaOf(custom.Hover);
            double alpha = hoverStrength is { } percent
                ? Math.Clamp(percent, LauncherSettings.MinHoverStrength, LauncherSettings.MaxHoverStrength) * 255 / 100.0
                : derived;
            palette = palette with
            {
                PointerOver = WithAlpha(hoverHex!, alpha),
                PointerOverBorder = WithAlpha(hoverHex!, alpha * AlphaOf(custom.HoverBorder) / derived),
                Pressed = WithAlpha(hoverHex!, alpha * AlphaOf(custom.Pressed) / derived),
            };
        }

        if (LauncherSettings.IsValidColor(focusHex))
        {
            palette = palette with { Focus = focusHex!.ToUpperInvariant(), DropTarget = WithAlpha(focusHex!, AlphaOf(custom.DropTarget)) };
        }

        return palette;
    }

    /// <summary>The hover colour a custom theme derives from its background, <c>#RRGGBB</c>: black on a light one, white on a dark one.</summary>
    public static string DerivedHoverColor(ThemePalette custom) => "#" + Opaque(custom.Hover);

    /// <summary>The strength of the derived hover overlay in percent, held to the slider's 10–100 (black's 8 % starts at 10).</summary>
    public static int DerivedHoverStrength(ThemePalette custom) =>
        Math.Clamp((int)Math.Round(AlphaOf(custom.Hover) * 100 / 255.0), LauncherSettings.MinHoverStrength, LauncherSettings.MaxHoverStrength);

    /// <summary>
    /// Whether a focus colour is too close to the background for keyboard focus to be seen: a WCAG contrast ratio under
    /// 3 : 1 (0.15.0 WP4b; Settings warns, Save is not stopped).
    /// </summary>
    public static bool IsFocusHardToSee(string focusHex, string backgroundHex) =>
        ContrastRatio(RelativeLuminance(focusHex), RelativeLuminance(backgroundHex)) < 3;

    private static int AlphaOf(string argb) => argb.Length == 9 ? int.Parse(argb.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) : 255;

    /// <summary>The colour of <c>#AARRGGBB</c> or <c>#RRGGBB</c> without its alpha.</summary>
    private static string Opaque(string hex) => hex.Length == 9 ? hex[3..] : hex[1..];

    private static string WithAlpha(string hex, double alpha) =>
        string.Create(CultureInfo.InvariantCulture, $"#{(int)Math.Clamp(Math.Round(alpha, MidpointRounding.AwayFromZero), 0, 255):X2}{Opaque(hex).ToUpperInvariant()}");

    /// <summary>Whether text on <paramref name="hex"/> should be dark: the rule of a custom background.</summary>
    public static bool IsLight(string hex) => Luminance(hex) > 0.6;

    /// <summary>
    /// Near-black or white, whichever shows better on <paramref name="hex"/>: the text of a custom background, and of a
    /// text icon that has a tile colour and no colour of its own.
    /// </summary>
    public static string ForegroundOn(string hex) => IsLight(hex) ? "#202020" : "#FFFFFF";

    /// <summary>
    /// White or near-black (#202020), whichever has the higher WCAG contrast ratio against <paramref name="hex"/>: the
    /// text of a text icon on a tile colour with no colour of its own (0.12.0 release check S2). The custom
    /// background keeps its own rule (<see cref="ForegroundOn"/>), which gave #00FF00 a white symbol at about 1.4:1.
    /// </summary>
    public static string TextOnTile(string hex)
    {
        var tile = RelativeLuminance(hex);
        return ContrastRatio(1.0, tile) >= ContrastRatio(RelativeLuminance("#202020"), tile) ? "#FFFFFF" : "#202020";
    }

    /// <summary>The WCAG relative luminance of a <c>#RRGGBB</c> colour: its channels through the sRGB curve, 0 to 1.</summary>
    public static double RelativeLuminance(string hex)
    {
        var (r, g, b) = ParseRgb(hex);
        return (0.2126 * Linear(r)) + (0.7152 * Linear(g)) + (0.0722 * Linear(b));

        static double Linear(int channel)
        {
            var value = channel / 255.0;
            return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }
    }

    /// <summary>The WCAG contrast ratio of two relative luminances, 1 to 21.</summary>
    public static double ContrastRatio(double first, double second) => (Math.Max(first, second) + 0.05) / (Math.Min(first, second) + 0.05);

    /// <summary>Relative brightness of a <c>#RRGGBB</c> colour, 0 (black) to 1 (white).</summary>
    public static double Luminance(string hex)
    {
        var (r, g, b) = ParseRgb(hex);
        return (0.299 * r + 0.587 * g + 0.114 * b) / 255.0;
    }

    internal static (int R, int G, int B) ParseRgb(string hex)
    {
        if (!LauncherSettings.IsValidColor(hex))
        {
            throw new FormatException($"'{hex}' is not a #RRGGBB colour.");
        }

        var value = int.Parse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return ((value >> 16) & 0xFF, (value >> 8) & 0xFF, value & 0xFF);
    }

    private static string Darken(string hex, double amount)
    {
        var (r, g, b) = ParseRgb(hex);
        var factor = 1 - amount;
        return string.Format(CultureInfo.InvariantCulture, "#{0:X2}{1:X2}{2:X2}", (int)(r * factor), (int)(g * factor), (int)(b * factor));
    }
}
