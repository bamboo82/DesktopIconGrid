using System.Text.Json;

namespace DesktopIconGrid.Core.Launching;

/// <summary>A profile of a Chromium browser: its folder under <c>User Data</c> (what <c>--profile-directory</c> takes) and its name.</summary>
public sealed record BrowserProfile(string Directory, string Name);

/// <summary>A browser a web link can be opened in with a chosen profile.</summary>
/// <param name="Id">Stored in <see cref="Models.ShortcutItem.Browser"/>.</param>
/// <param name="Executable">Started through the shell, which finds it under <c>App Paths</c>.</param>
/// <param name="UserDataFolder">Where its <c>Local State</c> lists the profiles, below <c>%LocalAppData%</c>.</param>
public sealed record KnownBrowser(string Id, string Executable, string UserDataFolder);

/// <summary>
/// Edge and Chrome keep their profiles in <c>User Data\Local State</c> (<c>profile.info_cache</c>: folder → name) and
/// take <c>--profile-directory=&lt;folder&gt;</c>, plus <c>--app=&lt;url&gt;</c> for a window without browser chrome.
/// </summary>
public static class BrowserProfiles
{
    public const string Edge = "edge";
    public const string Chrome = "chrome";

    public static readonly IReadOnlyList<KnownBrowser> Browsers =
    [
        new(Edge, "msedge.exe", @"Microsoft\Edge\User Data"),
        new(Chrome, "chrome.exe", @"Google\Chrome\User Data"),
    ];

    public static KnownBrowser? Find(string? id) =>
        string.IsNullOrWhiteSpace(id) ? null : Browsers.FirstOrDefault(b => string.Equals(b.Id, id.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>The profiles listed in a <c>Local State</c> file, by name; nothing when it cannot be read.</summary>
    public static IReadOnlyList<BrowserProfile> Parse(string? localStateJson)
    {
        if (string.IsNullOrWhiteSpace(localStateJson))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(localStateJson);
            if (!document.RootElement.TryGetProperty("profile", out var profile)
                || profile.ValueKind != JsonValueKind.Object
                || !profile.TryGetProperty("info_cache", out var cache)
                || cache.ValueKind != JsonValueKind.Object)
            {
                return [];
            }

            var profiles = new List<BrowserProfile>();
            foreach (var entry in cache.EnumerateObject())
            {
                var name = entry.Value.ValueKind == JsonValueKind.Object
                           && entry.Value.TryGetProperty("name", out var nameElement)
                           && nameElement.ValueKind == JsonValueKind.String
                    ? nameElement.GetString()
                    : null;
                profiles.Add(new BrowserProfile(entry.Name, string.IsNullOrWhiteSpace(name) ? entry.Name : name.Trim()));
            }

            return profiles.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>The browser's command line for <paramref name="url"/>: the profile, an app window or a tab, then the shortcut's own parameters.</summary>
    public static string Arguments(string url, string? profileDirectory, bool appWindow, string? parameters)
    {
        ArgumentNullException.ThrowIfNull(url);
        var arguments = new List<string>();
        if (!string.IsNullOrWhiteSpace(profileDirectory))
        {
            arguments.Add("--profile-directory=" + DropArguments.Quote(profileDirectory.Trim()));
        }

        arguments.Add(appWindow ? "--app=" + DropArguments.Quote(url) : DropArguments.Quote(url));
        if (!string.IsNullOrWhiteSpace(parameters))
        {
            arguments.Add(parameters.Trim());
        }

        return string.Join(' ', arguments);
    }
}
