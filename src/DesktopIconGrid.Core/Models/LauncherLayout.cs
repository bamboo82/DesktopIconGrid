using System.Text.Json;
using System.Text.Json.Serialization;

namespace DesktopIconGrid.Core.Models;

/// <summary>The on-disk document: settings, window placement and the ordered items of the bar.</summary>
public sealed class LauncherLayout
{
    /// <summary>
    /// Raised only when existing data changes meaning. New properties need no bump: every level of the document
    /// keeps the properties it does not know (<see cref="ExtensionData"/>), so older versions pass them on.
    /// 0.8.0 shows a box at every start for a layout of a higher version, so a number is raised only for a real change.
    /// </summary>
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;

    /// <summary>
    /// The format a file's version stands for. Version 2 was written only by test builds of 0.8.1 (CI runs 93–100,
    /// never a release) and holds nothing version 1 does not: it is read, and saved, as 1, so neither 0.8.0 nor 0.8.1
    /// takes it for a newer version. Anything above is.
    /// </summary>
    public static int FormatOf(int version) => version == 2 ? CurrentVersion : version;

    public LauncherSettings Settings { get; set; } = new();

    public WindowPlacement? Window { get; set; }

    public List<LauncherItem> Items { get; set; } = [];

    /// <summary>Properties written by a newer version that this one does not know; kept so saving never drops them.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
