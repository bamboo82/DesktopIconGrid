using System.Windows.Media;
using DesktopIconGrid.Core.Models;

namespace DesktopIconGrid.Services;

/// <summary>Resolves icons for files, folders, links, shell locations and icon resources.</summary>
public interface IIconProvider
{
    /// <summary>
    /// Icon for a location or icon source: a file/folder path, URL, <c>shell:</c> name, CLSID parsing name,
    /// image file, or <c>path,index</c> resource. Returns <see langword="null"/> when nothing could be resolved.
    /// </summary>
    ImageSource? GetIcon(string source, int pixelSize);

    /// <summary>Icon for an item, honouring its custom icon and falling back to the kind's default.</summary>
    ImageSource? GetItemIcon(LauncherItem item, int pixelSize);

    /// <summary>The system folder icon, used by groups.</summary>
    ImageSource? GetFolderIcon(int pixelSize);

    /// <summary>Checks whether a source string looks resolvable, without doing expensive work.</summary>
    bool CanResolve(string? source);

    void ClearCache();
}
