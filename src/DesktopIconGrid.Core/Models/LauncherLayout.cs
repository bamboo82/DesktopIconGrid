using System.Text.Json;
using System.Text.Json.Serialization;

namespace DesktopIconGrid.Core.Models;

/// <summary>The on-disk document: settings, window placement and the ordered items of the bar.</summary>
public sealed class LauncherLayout
{
    /// <summary>
    /// Raised only when existing data changes meaning. New properties need no bump: every level of the document
    /// keeps the properties it does not know (<see cref="ExtensionData"/>), so older versions pass them on.
    /// </summary>
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;

    public LauncherSettings Settings { get; set; } = new();

    public WindowPlacement? Window { get; set; }

    public List<LauncherItem> Items { get; set; } = [];

    /// <summary>Properties written by a newer version that this one does not know; kept so saving never drops them.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
