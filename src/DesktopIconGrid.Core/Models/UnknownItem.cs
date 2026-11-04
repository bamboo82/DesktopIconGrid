using System.Text.Json;

namespace DesktopIconGrid.Core.Models;

/// <summary>
/// An item whose <c>$type</c> this version does not know (the layout was written by a newer build).
/// It keeps its original JSON so saving the layout again writes it back unchanged; the bar shows it
/// as a placeholder that can be moved or removed but not opened or edited.
/// </summary>
public sealed class UnknownItem : LauncherItem
{
    /// <summary>The unrecognised <c>$type</c> value (empty when the discriminator was missing).</summary>
    public string TypeDiscriminator { get; set; } = string.Empty;

    /// <summary>The complete original JSON object.</summary>
    public JsonElement Raw { get; set; }

    public override LauncherItem Clone()
    {
        var copy = (UnknownItem)MemberwiseClone();
        copy.Raw = Raw.ValueKind == JsonValueKind.Undefined ? Raw : Raw.Clone();
        return copy;
    }
}
