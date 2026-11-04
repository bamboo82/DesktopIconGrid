namespace DesktopIconGrid.Core.Models;

/// <summary>A launchable entry: a program, document, folder or URL.</summary>
public sealed class ShortcutItem : LauncherItem
{
    /// <summary>
    /// What to open. A file, a folder or a URL; <c>shell:</c> locations open in File Explorer and
    /// <c>special:</c> locations are handled by the app. Environment variables are expanded at launch.
    /// </summary>
    public string Location { get; set; } = string.Empty;

    /// <summary>Command-line arguments passed to the program.</summary>
    public string? Parameters { get; set; }

    /// <summary>Member of the Multi Launch group (see <see cref="LauncherModel.MultiLaunchMembers"/>).</summary>
    public bool MultiLaunch { get; set; }

    /// <summary>
    /// Start the program with its output captured to a log file (console programs and batch files only;
    /// the program then runs without a console window of its own).
    /// </summary>
    public bool CaptureLog { get; set; }

    public override LauncherItem Clone() => (ShortcutItem)MemberwiseClone();

    public override void CopyContentFrom(LauncherItem source)
    {
        base.CopyContentFrom(source);
        if (source is ShortcutItem shortcut)
        {
            Location = shortcut.Location.Trim();
            Parameters = string.IsNullOrWhiteSpace(shortcut.Parameters) ? null : shortcut.Parameters.Trim();
            MultiLaunch = shortcut.MultiLaunch;
            CaptureLog = shortcut.CaptureLog;
        }
    }

    /// <summary>
    /// Creates a shortcut for a dropped or picked path, deriving a sensible caption from it
    /// ("C:\Tools\app.exe" → "app", "C:\Users\me\Projects" → "Projects", "https://example.com/" → "example.com").
    /// </summary>
    public static ShortcutItem FromPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var location = path.Trim();
        return new ShortcutItem { Name = SuggestName(location), Location = location };
    }

    /// <summary>Derives a caption from a file path, folder path or URL.</summary>
    public static string SuggestName(string location)
    {
        if (string.IsNullOrWhiteSpace(location))
        {
            return string.Empty;
        }

        var trimmed = location.Trim();
        if (Launching.LaunchPlanner.IsUri(trimmed) && Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            var lastSegment = uri.Segments.Length > 0 ? uri.Segments[^1].Trim('/') : string.Empty;
            if (lastSegment.Length > 0)
            {
                return StripExtension(Uri.UnescapeDataString(lastSegment));
            }

            return uri.Host.Length > 0 ? uri.Host : trimmed;
        }

        var withoutTrailingSeparators = trimmed.TrimEnd('/', '\\');
        if (withoutTrailingSeparators.Length == 0)
        {
            return trimmed;
        }

        var separator = withoutTrailingSeparators.LastIndexOfAny(['/', '\\']);
        var fileName = separator >= 0 ? withoutTrailingSeparators[(separator + 1)..] : withoutTrailingSeparators;
        return fileName.Length == 0 ? withoutTrailingSeparators : StripExtension(fileName);
    }

    private static string StripExtension(string fileName)
    {
        var dot = fileName.LastIndexOf('.');
        return dot > 0 ? fileName[..dot] : fileName;
    }
}
