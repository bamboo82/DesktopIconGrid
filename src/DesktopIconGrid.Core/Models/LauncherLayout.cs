namespace DesktopIconGrid.Core.Models;

/// <summary>The on-disk document: settings, window placement and the ordered items of the bar.</summary>
public sealed class LauncherLayout
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;

    public LauncherSettings Settings { get; set; } = new();

    public WindowPlacement? Window { get; set; }

    public List<LauncherItem> Items { get; set; } = [];
}
