using System.Text.Json.Serialization;

namespace DesktopIconGrid.Core.Models;

/// <summary>
/// A divider between icons: a thin line, a line that starts a new row and may carry a caption, a line that is the first
/// thing of a new row (both 0.14.0), an empty cell, or a small caption (the <see cref="LauncherItem.Name"/>). Stored as
/// <c>"$type": "separator"</c>; versions before 0.7.0 keep it as an item they do not know, and a separator without a
/// text is stored with a name for their sake (<see cref="SavedName"/>).
/// </summary>
public sealed class SeparatorItem : LauncherItem
{
    public const string Line = "line";

    /// <summary>
    /// A line that always starts a new row, even when the row it ends has room (0.14.0, the owner's request): drawn
    /// across the bar, with its <see cref="LauncherItem.Name"/> as a caption at the left when it has one. A version before
    /// 0.14.0 keeps the kind and the name and draws it as a line, which is the fallback wanted.
    /// </summary>
    public const string RowLine = "rowline";

    /// <summary>
    /// An upright line that is always the first thing of a new row (0.14.0): the row before it ends where it is, and
    /// the items after it follow the line. No text. A version before 0.14.0 draws it as a line.
    /// </summary>
    public const string RowHead = "rowhead";

    public const string Gap = "gap";
    public const string Label = "label";

    public static readonly IReadOnlyList<string> Kinds = [Line, RowHead, RowLine, Gap, Label];

    /// <summary>One of <see cref="Kinds"/>. Another value (from a newer version) is kept and drawn as a line.</summary>
    public string Kind { get; set; } = Line;

    /// <summary>Whether a separator of <paramref name="kind"/> has a text of its own: a caption, and a row line's caption.</summary>
    public static bool HasText(string? kind) =>
        string.Equals(kind?.Trim(), Label, StringComparison.OrdinalIgnoreCase) || string.Equals(kind?.Trim(), RowLine, StringComparison.OrdinalIgnoreCase);

    /// <summary>The kind this version draws.</summary>
    [JsonIgnore]
    public string EffectiveKind => Kinds.FirstOrDefault(k => string.Equals(k, Kind?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? Line;

    /// <summary>
    /// The name a separator without a text of <paramref name="kind"/> is saved with: "Divider" for a line, a row-start
    /// line and a row line without a caption (<paramref name="name"/> empty, or that saved name itself), "Gap" for a
    /// gap. This version ignores it when loading. It is there for 0.6.0, which reads a separator as an item from a
    /// newer version, names every such item without a name "Group" and saves that name, so a bar synced through a
    /// 0.6.0 PC came back with its lines called "Group". <see langword="null"/> for a caption and a row line with a
    /// caption, whose name is their text, and for a kind this version does not know, whose name belongs to the
    /// version that does.
    /// </summary>
    public static string? SavedName(string? kind, string? name = null)
    {
        var trimmed = kind?.Trim() ?? string.Empty;
        if (trimmed.Length == 0 || Is(trimmed, Line) || Is(trimmed, RowHead))
        {
            return "Divider";
        }

        if (Is(trimmed, RowLine))
        {
            return string.IsNullOrWhiteSpace(name) || name.Trim() == "Divider" ? "Divider" : null;
        }

        return Is(trimmed, Gap) ? "Gap" : null;
    }

    private static bool Is(string kind, string known) => string.Equals(kind, known, StringComparison.OrdinalIgnoreCase);

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
