using System.Text.Json.Serialization;

namespace DesktopIconGrid.Core.Models;

/// <summary>A launchable entry: a program, document, folder or URL.</summary>
public sealed class ShortcutItem : LauncherItem
{
    /// <summary><see cref="WhenRunning"/>: a click brings the running program's window forward.</summary>
    public const string WhenRunningSwitch = "switch";

    /// <summary><see cref="WhenRunning"/>: a click always starts another instance.</summary>
    public const string WhenRunningStart = "start";

    /// <summary><see cref="WindowState"/>: start the program's window minimised.</summary>
    public const string WindowMinimized = "minimized";

    /// <summary><see cref="WindowState"/>: start the program's window maximised.</summary>
    public const string WindowMaximized = "maximized";

    /// <summary>
    /// What to open. A file, a folder or a URL; <c>shell:</c> locations open in File Explorer and
    /// <c>special:</c> locations are handled by the app. Environment variables are expanded at launch.
    /// </summary>
    public string Location { get; set; } = string.Empty;

    /// <summary>
    /// Where the shortcut points on particular PCs, by device id (<see cref="ThisDevice"/>), where that differs from
    /// <see cref="Location"/>: set by Repair… or by Properties with "only on this PC". It travels with a sync copy, so
    /// every PC finds its own path; <see langword="null"/> when there are none.
    /// </summary>
    public Dictionary<string, string>? Locations { get; set; }

    /// <summary>The location on this PC: its own (<see cref="Locations"/>) when it has one, otherwise <see cref="Location"/>.</summary>
    [JsonIgnore]
    public string EffectiveLocation => LocationFor(ThisDevice.Id);

    /// <summary>True when this PC has a location of its own.</summary>
    [JsonIgnore]
    public bool HasOwnLocation => ThisDevice.Id is { } id && Locations?.ContainsKey(id) == true;

    /// <summary>The location on the PC with <paramref name="deviceId"/>.</summary>
    public string LocationFor(string? deviceId) =>
        deviceId is not null && Locations is { } own && own.TryGetValue(deviceId, out var here) && !string.IsNullOrWhiteSpace(here)
            ? here
            : Location;

    /// <summary>Gives the PC <paramref name="deviceId"/> a location of its own, or takes it away with a blank one.</summary>
    public void SetLocationFor(string deviceId, string? location)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        if (string.IsNullOrWhiteSpace(location))
        {
            Locations?.Remove(deviceId);
        }
        else
        {
            (Locations ??= new Dictionary<string, string>(StringComparer.Ordinal))[deviceId] = location.Trim();
        }

        if (Locations is { Count: 0 })
        {
            Locations = null;
        }
    }

    /// <summary>
    /// A health check shown as a light on the icon (0.9.0): <c>ping host</c>, <c>tcp host:port</c> or an
    /// <c>http(s)://</c> address (see <see cref="Health.HealthCheck"/>); <see langword="null"/> for none.
    /// </summary>
    public string? HealthCheck { get; set; }

    /// <summary>Seconds between health checks (30–600); <see langword="null"/> is a minute.</summary>
    public int? HealthInterval { get; set; }

    /// <summary>A notification when the health light turns red or green again.</summary>
    public bool HealthNotify { get; set; }

    /// <summary>
    /// A folder shortcut opens as a stack (0.10.0): its contents in a small list by the pointer, not an Explorer window.
    /// Ignored when the location is not a folder.
    /// </summary>
    public bool OpenAsStack { get; set; }

    /// <summary>The stack lists the newest entries first, not by name.</summary>
    public bool StackNewestFirst { get; set; }

    /// <summary>Command-line arguments passed to the program.</summary>
    public string? Parameters { get; set; }

    /// <summary>Member of the Multi Launch group (see <see cref="LauncherModel.MultiLaunchMembers"/>).</summary>
    public bool MultiLaunch { get; set; }

    /// <summary>
    /// Start the program with its output captured to a log file (console programs and batch files only;
    /// the program then runs without a console window of its own).
    /// </summary>
    public bool CaptureLog { get; set; }

    /// <summary>
    /// What a click does while the program runs: <see cref="WhenRunningSwitch"/> or <see cref="WhenRunningStart"/>;
    /// <see langword="null"/> follows the location (programs switch; documents, folders and links start again).
    /// </summary>
    public string? WhenRunning { get; set; }

    /// <summary>The working folder; <see langword="null"/> uses the folder of the program (environment variables allowed).</summary>
    public string? StartIn { get; set; }

    /// <summary>Start it elevated (an ordinary UAC prompt).</summary>
    public bool RunAsAdministrator { get; set; }

    /// <summary><see cref="WindowMinimized"/>, <see cref="WindowMaximized"/>, or <see langword="null"/> for a normal window.</summary>
    public string? WindowState { get; set; }

    /// <summary>For web links: the browser to open them in (<see cref="Launching.BrowserProfiles"/>); <see langword="null"/> uses the default browser.</summary>
    public string? Browser { get; set; }

    /// <summary>With <see cref="Browser"/>: the profile folder (<c>Default</c>, <c>Profile 1</c>…); <see langword="null"/> uses the browser's last one.</summary>
    public string? BrowserProfile { get; set; }

    /// <summary>With <see cref="Browser"/>: open the link as an app window, without tabs and address bar.</summary>
    public bool AppWindow { get; set; }

    protected override LauncherItem CloneCore()
    {
        var copy = (ShortcutItem)MemberwiseClone();
        copy.Locations = CopyLocations(Locations);
        return copy;
    }

    private static Dictionary<string, string>? CopyLocations(Dictionary<string, string>? locations)
    {
        var kept = locations?
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
            .ToDictionary(pair => pair.Key, pair => pair.Value.Trim(), StringComparer.Ordinal);
        return kept is { Count: > 0 } ? kept : null;
    }

    public override void CopyContentFrom(LauncherItem source)
    {
        base.CopyContentFrom(source);
        if (source is ShortcutItem shortcut)
        {
            Location = shortcut.Location.Trim();
            Locations = CopyLocations(shortcut.Locations);
            Parameters = string.IsNullOrWhiteSpace(shortcut.Parameters) ? null : shortcut.Parameters.Trim();
            MultiLaunch = shortcut.MultiLaunch;
            CaptureLog = shortcut.CaptureLog;
            WhenRunning = shortcut.WhenRunning;
            StartIn = string.IsNullOrWhiteSpace(shortcut.StartIn) ? null : shortcut.StartIn.Trim();
            RunAsAdministrator = shortcut.RunAsAdministrator;
            WindowState = shortcut.WindowState;
            Browser = string.IsNullOrWhiteSpace(shortcut.Browser) ? null : shortcut.Browser.Trim();
            BrowserProfile = string.IsNullOrWhiteSpace(shortcut.BrowserProfile) ? null : shortcut.BrowserProfile.Trim();
            AppWindow = shortcut.AppWindow;
            HealthCheck = string.IsNullOrWhiteSpace(shortcut.HealthCheck) ? null : shortcut.HealthCheck.Trim();
            HealthInterval = shortcut.HealthInterval;
            HealthNotify = shortcut.HealthNotify;
            OpenAsStack = shortcut.OpenAsStack;
            StackNewestFirst = shortcut.OpenAsStack && shortcut.StackNewestFirst;
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
        return new ShortcutItem { Name = SuggestName(location), Location = Storage.DataLocation.ForStorage(location) };
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
