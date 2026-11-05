using System.Text.Json.Serialization;

namespace DesktopIconGrid.Core.Models;

/// <summary>
/// A divider between icons: a thin line, an empty cell, or a small caption (the <see cref="LauncherItem.Name"/>).
/// Stored as <c>"$type": "separator"</c>; versions before 0.7.0 keep it as an item they do not know.
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
