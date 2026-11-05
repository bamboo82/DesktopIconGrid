namespace DesktopIconGrid.Core.Models;

/// <summary>A colour tag: the name stored with the item and the colour it is drawn in (<c>#RRGGBB</c>).</summary>
public sealed record ColorTag(string Name, string Color);

/// <summary>
/// The colour tags an item can carry (<see cref="LauncherItem.ColorTag"/>), drawn as a corner on its icon. A name
/// this version does not know (from a newer one) is kept but not drawn.
/// </summary>
public static class ColorTags
{
    public const string Red = "red";
    public const string Orange = "orange";
    public const string Yellow = "yellow";
    public const string Green = "green";
    public const string Blue = "blue";
    public const string Purple = "purple";
    public const string Gray = "gray";

    public static readonly IReadOnlyList<ColorTag> All =
    [
        new(Red, "#E5484D"),
        new(Orange, "#F2994A"),
        new(Yellow, "#E8C547"),
        new(Green, "#3FB950"),
        new(Blue, "#3B82F6"),
        new(Purple, "#A371F7"),
        new(Gray, "#8B949E"),
    ];

    /// <summary>The known tag of that name (any case, spaces around ignored), or <see langword="null"/>.</summary>
    public static ColorTag? Find(string? name) =>
        string.IsNullOrWhiteSpace(name) ? null : All.FirstOrDefault(t => string.Equals(t.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>A known name in its stored spelling, another name trimmed, blank as <see langword="null"/>.</summary>
    public static string? Normalize(string? name) =>
        string.IsNullOrWhiteSpace(name) ? null : Find(name)?.Name ?? name.Trim();
}
