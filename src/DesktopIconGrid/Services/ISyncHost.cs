using DesktopIconGrid.Core.Models;

namespace DesktopIconGrid.Services;

/// <summary>What the sync coordinator needs from the bar: the current layout, a change signal and a way to apply a downloaded copy.</summary>
public interface ISyncHost
{
    /// <summary>Raised after settings or items changed and were written to disk (window moves do not count).</summary>
    event EventHandler? LayoutChanged;

    LauncherLayout ExportLayout();

    void ReplaceLayout(LauncherLayout layout);
}
