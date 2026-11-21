using DesktopIconGrid.Core.Models;

namespace DesktopIconGrid.Services;

/// <summary>What the sync coordinator needs from the bar: the current layout, a change signal and a way to apply a downloaded copy.</summary>
public interface ISyncHost
{
    /// <summary>Raised after settings or items changed and were written to disk (window moves do not count).</summary>
    event EventHandler? LayoutChanged;

    LauncherLayout ExportLayout();

    void ReplaceLayout(LauncherLayout layout);

    /// <summary>The background picture <paramref name="storedName"/> came from the sync place (0.15.0 WP5b): draw it if it is still the one named.</summary>
    void PictureArrived(string storedName);
}
