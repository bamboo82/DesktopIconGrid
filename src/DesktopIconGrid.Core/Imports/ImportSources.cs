using System.Text.Json;
using System.Text.RegularExpressions;

namespace DesktopIconGrid.Core.Imports;

/// <summary>Where an imported shortcut comes from; also the name of the group it goes into when each source gets one.</summary>
public enum ImportSource
{
    TaskbarPins,
    Desktop,
    WindowsTerminal,
    WinScp,
    Putty,
    SshHosts,
}

/// <summary>Something <c>Import ▸</c> can turn into a shortcut.</summary>
public sealed record ImportCandidate(ImportSource Source, string Name, string Location, string? Parameters = null, string? StartIn = null)
{
    /// <summary>What decides whether it is already on the bar: the location with its parameters, in any case.</summary>
    public string Key => string.IsNullOrWhiteSpace(Parameters) ? Location.Trim() : $"{Location.Trim()} {Parameters.Trim()}";
}

/// <summary>
/// Reads the lists that <c>Import ▸</c> offers from the files other programs keep: Windows Terminal profiles, the hosts
/// of an SSH config file, and sessions saved by WinSCP and PuTTY (whose names are stored URL-encoded).
/// </summary>
public static partial class ImportSources
{
    /// <summary>The profiles of a Windows Terminal <c>settings.json</c> (comments and trailing commas allowed), hidden ones left out.</summary>
    public static IReadOnlyList<string> TerminalProfiles(string? settingsJson)
    {
        if (string.IsNullOrWhiteSpace(settingsJson))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(settingsJson, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            if (!document.RootElement.TryGetProperty("profiles", out var profiles))
            {
                return [];
            }

            // Either { "profiles": [ … ] } (old) or { "profiles": { "defaults": …, "list": [ … ] } }.
            var list = profiles.ValueKind == JsonValueKind.Array
                ? profiles
                : profiles.ValueKind == JsonValueKind.Object && profiles.TryGetProperty("list", out var inner) && inner.ValueKind == JsonValueKind.Array
                    ? inner
                    : default;
            if (list.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var names = new List<string>();
            foreach (var profile in list.EnumerateArray())
            {
                if (profile.ValueKind != JsonValueKind.Object
                    || (profile.TryGetProperty("hidden", out var hidden) && hidden.ValueKind == JsonValueKind.True)
                    || !profile.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String
                    || string.IsNullOrWhiteSpace(name.GetString()))
                {
                    continue;
                }

                names.Add(name.GetString()!.Trim());
            }

            return names;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>The host aliases of an SSH <c>config</c> file; patterns (<c>*</c>, <c>?</c>, <c>!</c>) are left out.</summary>
    public static IReadOnlyList<string> SshHosts(string? configText)
    {
        if (string.IsNullOrWhiteSpace(configText))
        {
            return [];
        }

        var hosts = new List<string>();
        foreach (var rawLine in configText.Split('\n'))
        {
            var line = rawLine.Split('#')[0].Trim(); // comments run to the end of the line
            var match = HostLine().Match(line);
            if (!match.Success)
            {
                continue;
            }

            foreach (var host in match.Groups["hosts"].Value.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
            {
                var name = host.Trim('"');
                if (name.Length > 0 && name.IndexOfAny(['*', '?', '!']) < 0 && !hosts.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    hosts.Add(name);
                }
            }
        }

        return hosts;
    }

    /// <summary>The sessions of a portable WinSCP's <c>WinSCP.ini</c> (<c>[Sessions\name]</c> sections), decoded, defaults left out.</summary>
    public static IReadOnlyList<string> WinScpIniSessions(string? iniText)
    {
        if (string.IsNullOrWhiteSpace(iniText))
        {
            return [];
        }

        return SessionNames(iniText.Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.StartsWith(@"[Sessions\", StringComparison.OrdinalIgnoreCase) && line.EndsWith(']'))
            .Select(line => line[@"[Sessions\".Length..^1]));
    }

    /// <summary>Decoded session names (WinSCP and PuTTY store <c>My%20Server</c>), without the program's own defaults.</summary>
    public static IReadOnlyList<string> SessionNames(IEnumerable<string> storedNames)
    {
        ArgumentNullException.ThrowIfNull(storedNames);
        return storedNames
            .Select(Decode)
            .Where(name => name.Length > 0 && !string.Equals(name, "Default Settings", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>A stored session name decoded (<c>%20</c> → space); a name that is not valid escaping is kept as it is.</summary>
    public static string Decode(string storedName)
    {
        ArgumentNullException.ThrowIfNull(storedName);
        try
        {
            return Uri.UnescapeDataString(storedName.Trim());
        }
        catch (UriFormatException)
        {
            return storedName.Trim();
        }
    }

    [GeneratedRegex(@"^Host\s+(?<hosts>.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HostLine();
}
