using System.IO;
using DesktopIconGrid.Core.Launching;
using DesktopIconGrid.Resources;

namespace DesktopIconGrid.Services;

/// <summary>One entry of "Open web links in": the default browser, or a browser with one of its profiles.</summary>
public sealed record BrowserChoice(string? Browser, string? Profile, string Name)
{
    public bool IsDefault => Browser is null;
}

/// <summary>The browsers and profiles installed for this Windows user (Edge and Chrome, from their <c>Local State</c>).</summary>
public static class BrowserCatalog
{
    public static readonly BrowserChoice DefaultBrowser = new(null, null, Strings.Props_BrowserDefault);

    /// <summary>The default browser first, then every profile of every installed browser.</summary>
    public static IReadOnlyList<BrowserChoice> Choices()
    {
        var choices = new List<BrowserChoice> { DefaultBrowser };
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify);
        foreach (var browser in BrowserProfiles.Browsers)
        {
            foreach (var profile in BrowserProfiles.Parse(ReadShared(Path.Combine(localAppData, browser.UserDataFolder, "Local State"))))
            {
                choices.Add(new BrowserChoice(browser.Id, profile.Directory, string.Format(Strings.Props_BrowserProfile, DisplayName(browser.Id), profile.Name)));
            }
        }

        return choices;
    }

    /// <summary>The name of a browser, or its id when it is not one this version knows.</summary>
    public static string DisplayName(string browserId) => browserId switch
    {
        BrowserProfiles.Edge => Strings.Props_BrowserEdge,
        BrowserProfiles.Chrome => Strings.Props_BrowserChrome,
        _ => browserId,
    };

    /// <summary>Reads a file the browser may have open; <see langword="null"/> when it is missing or locked.</summary>
    private static string? ReadShared(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
