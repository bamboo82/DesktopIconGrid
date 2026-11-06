using DesktopIconGrid.Core.Launching;

namespace DesktopIconGrid.Core.Imports;

/// <summary>An installed app as the Start menu lists it (<c>shell:AppsFolder</c>), and the location the bar stores for it.</summary>
/// <param name="Name">The name the Start menu shows.</param>
/// <param name="Location">A program path, a link, or <c>shell:AppsFolder\…</c> for an app only the shell can start.</param>
/// <param name="IsStoreApp">A packaged (Store) app, started through its app user model id.</param>
public sealed record InstalledApp(string Name, string Location, bool IsStoreApp);

/// <summary>
/// Turns an entry of <c>shell:AppsFolder</c> into a location. The entries' parsing names come in four shapes: a Store
/// app's id (<c>Microsoft.WindowsCalculator_8wekyb3d8bbwe!App</c>), a path below a known folder
/// (<c>{6D809377-6AF0-444B-8957-A3773F02200E}\Tool\tool.exe</c>), a link, or an id a desktop program registered
/// (<c>Microsoft.Windows.Explorer</c>). A path becomes the real path, so the running badge, switching and the missing
/// check work as for any program; the ids are started through the shell.
/// </summary>
public static class AppsFolderEntry
{
    /// <summary>The location for an entry; <paramref name="knownFolderPath"/> resolves a known folder's id to its path.</summary>
    public static InstalledApp? ToApp(string? name, string? parsingName, Func<Guid, string?> knownFolderPath)
    {
        ArgumentNullException.ThrowIfNull(knownFolderPath);
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(parsingName))
        {
            return null;
        }

        var parsing = parsingName.Trim();
        var display = name.Trim();
        if (parsing.StartsWith('{') && parsing.IndexOf('}') is var close and > 1
            && Guid.TryParse(parsing[1..close], out var folderId))
        {
            var rest = parsing[(close + 1)..].TrimStart('\\');
            return knownFolderPath(folderId) is { Length: > 0 } folder && rest.Length > 0
                ? new InstalledApp(display, Path.Combine(folder, rest), IsStoreApp: false)
                : new InstalledApp(display, PackagedApps.AppsFolderPrefix + parsing, IsStoreApp: false);
        }

        if (LaunchPlanner.IsWebLink(parsing))
        {
            return new InstalledApp(display, parsing, IsStoreApp: false);
        }

        return new InstalledApp(display, PackagedApps.AppsFolderPrefix + parsing, IsStoreApp: parsing.Contains('!', StringComparison.Ordinal));
    }
}
