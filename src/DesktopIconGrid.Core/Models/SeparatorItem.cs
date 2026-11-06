using System.Text.Json.Serialization;

namespace DesktopIconGrid.Core.Models;

/// <summary>
/// A divider between icons: a thin line, an empty cell, or a small caption (the <see cref="LauncherItem.Name"/>).
/// Stored as <c>"$type": "separator"</c>; versions before 0.7.0 keep it as an item they do not know, and a line or a
/// gap is stored with a name for their sake (<see cref="SavedName"/>).
/// </summary>
public sealed class SeparatorItem : LauncherItem
{
    public const string Line = "line";
    public const string Gap = "gap";
    public const string Label = "label";

    public static readonly IReadOnlyList<string> Kinds = [Line, Gap, Label];

    /// <summary><see cref="Line"/>, <see cref="Gap"/> or <see cref="Label"/>. Another value (from a newer version) is kept and drawn as a line.</summary>
    public string Kind { get; set; } = Line;

    /// <summary>The kind this version draws.</summary>
    [JsonIgnore]
    public string EffectiveKind => Kinds.FirstOrDefault(k => string.Equals(k, Kind?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? Line;

    /// <summary>
    /// The name a line or a gap of <paramref name="kind"/> is saved with: "Divider" or "Gap". This version ignores it
    /// when loading, since only a caption has a name. It is there for 0.6.0, which reads a separator as an item from a
    /// newer version, names every such item without a name "Group" and saves that name, so a bar synced through a
    /// 0.6.0 PC came back with its lines called "Group". <see langword="null"/> for a caption, whose name is its text,
    /// and for a kind this version does not know, whose name belongs to the version that does.
    /// </summary>
    public static string? SavedName(string? kind)
    {
        var trimmed = kind?.Trim() ?? string.Empty;
        return trimmed.Length == 0 || string.Equals(trimmed, Line, StringComparison.OrdinalIgnoreCase) ? "Divider"
            : string.Equals(trimmed, Gap, StringComparison.OrdinalIgnoreCase) ? "Gap"
            : null;
    }

    protected override LauncherItem CloneCore() => (SeparatorItem)MemberwiseClone();

    public override void CopyContentFrom(LauncherItem source)
    {
        base.CopyContentFrom(source);
        if (source is SeparatorItem separator)
        {
            Kind = separator.Kind?.Trim() ?? Line;
        }
    }
}
