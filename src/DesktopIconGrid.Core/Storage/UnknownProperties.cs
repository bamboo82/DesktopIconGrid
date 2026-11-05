using System.Text.Json;

namespace DesktopIconGrid.Core.Storage;

/// <summary>
/// The JSON properties a newer version wrote and this one does not know, held by
/// <see cref="System.Text.Json.Serialization.JsonExtensionDataAttribute"/> properties so that loading and saving
/// (here, or on another PC through a sync copy) never drops them.
/// </summary>
public static class UnknownProperties
{
    /// <summary>
    /// A copy of <paramref name="properties"/> for a cloned object, so two objects never share one dictionary;
    /// <see langword="null"/> when there is nothing to keep. The values are immutable and are shared.
    /// </summary>
    public static Dictionary<string, JsonElement>? Copy(Dictionary<string, JsonElement>? properties) =>
        properties is null || properties.Count == 0 ? null : new Dictionary<string, JsonElement>(properties, properties.Comparer);
}
