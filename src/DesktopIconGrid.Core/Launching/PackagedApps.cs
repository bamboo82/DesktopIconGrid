namespace DesktopIconGrid.Core.Launching;

/// <summary>
/// Store (packaged) apps as the running badge and the window switcher see them: their processes run from a
/// package folder (<c>…\WindowsApps\Name_Version_Arch_Resource_PublisherId\…</c>), so they are matched by package
/// family (<c>Name_PublisherId</c>) instead of by one executable path.
/// </summary>
public static class PackagedApps
{
    /// <summary>How a Store app added from the Apps folder is stored: <c>shell:AppsFolder\Family!AppId</c>.</summary>
    public const string AppsFolderPrefix = @"shell:AppsFolder\";

    private const int PublisherIdLength = 13;

    /// <summary>
    /// Inbox programs that Windows 11 replaced with Store apps: starting the old file name starts the app, whose
    /// processes run from its package. On Windows 10 the old program runs and is matched by its own path.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> InboxReplacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["calc.exe"] = "Microsoft.WindowsCalculator_8wekyb3d8bbwe",
        ["notepad.exe"] = "Microsoft.WindowsNotepad_8wekyb3d8bbwe",
        ["mspaint.exe"] = "Microsoft.Paint_8wekyb3d8bbwe",
        ["snippingtool.exe"] = "Microsoft.ScreenSketch_8wekyb3d8bbwe",
    };

    /// <summary>The package family of a <c>shell:AppsFolder\Family!AppId</c> location; <see langword="null"/> for anything else.</summary>
    public static string? FamilyOfAppsFolderItem(string? location)
    {
        var trimmed = location?.Trim();
        if (trimmed is null || !trimmed.StartsWith(AppsFolderPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var appUserModelId = trimmed[AppsFolderPrefix.Length..];
        var bang = appUserModelId.IndexOf('!');
        if (bang <= 0)
        {
            return null; // a desktop program listed in the Apps folder by path or by its own id
        }

        var family = appUserModelId[..bang];
        return IsFamilyName(family) ? family : null;
    }

    /// <summary><c>Name_PublisherId</c>: no underscore in the name, a 13-character publisher id.</summary>
    public static bool IsFamilyName(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        var separator = value.LastIndexOf('_');
        return separator > 0
               && value.IndexOf('_') == separator
               && value.Length - separator - 1 == PublisherIdLength
               && value[(separator + 1)..].All(char.IsAsciiLetterOrDigit);
    }

    /// <summary>True when <paramref name="imagePath"/> lies in a package folder of <paramref name="family"/>.</summary>
    public static bool IsInFamily(string imagePath, string family)
    {
        ArgumentNullException.ThrowIfNull(imagePath);
        if (!IsFamilyName(family))
        {
            return false;
        }

        var separator = family.LastIndexOf('_');
        var name = family[..separator];
        var publisher = family[(separator + 1)..];
        var segments = imagePath.Split('\\', '/');
        for (var i = 0; i < segments.Length - 1; i++)
        {
            if (segments[i].Equals("WindowsApps", StringComparison.OrdinalIgnoreCase)
                && segments[i + 1].StartsWith(name + "_", StringComparison.OrdinalIgnoreCase)
                && segments[i + 1].EndsWith("_" + publisher, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The Store app behind an inbox program (see <see cref="InboxReplacements"/>) when <paramref name="executablePath"/>
    /// lies directly in one of <paramref name="windowsFolders"/> (the Windows folder, System32, SysWOW64).
    /// </summary>
    public static string? ReplacementFamily(string executablePath, IEnumerable<string> windowsFolders)
    {
        ArgumentNullException.ThrowIfNull(executablePath);
        ArgumentNullException.ThrowIfNull(windowsFolders);
        var cut = executablePath.LastIndexOfAny(['\\', '/']);
        if (cut <= 0 || !InboxReplacements.TryGetValue(executablePath[(cut + 1)..], out var family))
        {
            return null;
        }

        var folder = executablePath[..cut];
        return windowsFolders.Any(f => !string.IsNullOrEmpty(f) && string.Equals(f.TrimEnd('\\', '/'), folder, StringComparison.OrdinalIgnoreCase))
            ? family
            : null;
    }
}
