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
    public bool IsCustom => string.Equals(Id, ThemeCatalog.CustomId, StringComparison.OrdinalIgnoreCase);
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
            return CreateCustom(settings.BackgroundColor);
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
        var light = Luminance(background) > 0.6;

        return new ThemePalette(
            CustomId, "Custom",
            Background: background,
            Border: Darken(background, 0.5),
            Foreground: light ? "#202020" : "#FFFFFF",
            Hover: light ? "#14000000" : "#2EFFFFFF",
            HoverBorder: light ? "#30000000" : "#50FFFFFF",
            Pressed: light ? "#24000000" : "#50FFFFFF",
            Grip: light ? "#55000000" : "#70FFFFFF",
            Accent: light ? "#2563EB" : "#4C8BF5",
            DropTarget: light ? "#402563EB" : "#4A4C8BF5");
    }

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
