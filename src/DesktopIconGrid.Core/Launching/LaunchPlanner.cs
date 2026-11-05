using System.Diagnostics;
using DesktopIconGrid.Core.Models;

namespace DesktopIconGrid.Core.Launching;

public enum LaunchAction
{
    /// <summary>Start <see cref="LaunchPlan.StartInfo"/> through the shell.</summary>
    StartProcess,

    /// <summary>Toggle "show desktop" (handled by the app with the Shell COM object).</summary>
    ShowDesktop,

    /// <summary>Open the Windows Run dialog (handled by the app with the Shell COM object).</summary>
    RunDialog,
}

public sealed record LaunchPlan(LaunchAction Action, ProcessStartInfo? StartInfo = null);

/// <summary>Turns a shortcut into something the app can execute.</summary>
public static class LaunchPlanner
{
    public const string SpecialScheme = "special:";
    public const string ShellScheme = "shell:";
    public const string ShowDesktopLocation = SpecialScheme + "show-desktop";
    public const string RunDialogLocation = SpecialScheme + "run";

    /// <summary>The shell verb that starts a program elevated.</summary>
    public const string RunAsVerb = "runas";

    public static LaunchPlan Plan(ShortcutItem shortcut) => Plan(shortcut, expandParameters: true);

    /// <summary>Plans the launch; pass <paramref name="expandParameters"/> = false when the parameters were already expanded (dropped paths may contain <c>%</c>).</summary>
    public static LaunchPlan Plan(ShortcutItem shortcut, bool expandParameters) => Plan(shortcut, expandParameters, asAdministrator: false);

    /// <summary>
    /// Plans the launch. <paramref name="asAdministrator"/> asks for elevation (the <c>runas</c> verb, an ordinary UAC
    /// prompt) for files and programs; links and shell locations ignore it.
    /// </summary>
    public static LaunchPlan Plan(ShortcutItem shortcut, bool expandParameters, bool asAdministrator)
    {
        ArgumentNullException.ThrowIfNull(shortcut);

        var location = Environment.ExpandEnvironmentVariables(shortcut.Location?.Trim() ?? string.Empty);
        if (location.Length == 0)
        {
            throw new ArgumentException("The shortcut has no location to open.", nameof(shortcut));
        }

        if (location.StartsWith(SpecialScheme, StringComparison.OrdinalIgnoreCase))
        {
            return location.ToLowerInvariant() switch
            {
                ShowDesktopLocation => new LaunchPlan(LaunchAction.ShowDesktop),
                RunDialogLocation => new LaunchPlan(LaunchAction.RunDialog),
                _ => throw new ArgumentException($"Unknown special location \"{location}\".", nameof(shortcut)),
            };
        }

        var parameters = string.IsNullOrWhiteSpace(shortcut.Parameters)
            ? null
            : expandParameters ? Environment.ExpandEnvironmentVariables(shortcut.Parameters.Trim()) : shortcut.Parameters.Trim();

        ProcessStartInfo startInfo;
        if (location.StartsWith(ShellScheme, StringComparison.OrdinalIgnoreCase))
        {
            // shell: namespace paths are most reliable when handed to Explorer explicitly.
            startInfo = new ProcessStartInfo("explorer.exe")
            {
                Arguments = parameters is null ? Quote(location) : $"{Quote(location)} {parameters}",
            };
        }
        else
        {
            startInfo = new ProcessStartInfo(location);
            if (parameters is not null)
            {
                startInfo.Arguments = parameters;
            }

            var workingDirectory = DefaultWorkingDirectory(location);
            if (workingDirectory is not null)
            {
                startInfo.WorkingDirectory = workingDirectory;
            }

            if (asAdministrator && !IsUri(location))
            {
                startInfo.Verb = RunAsVerb;
            }
        }

        startInfo.UseShellExecute = true;
        return new LaunchPlan(LaunchAction.StartProcess, startInfo);
    }

    /// <summary>The folder of a rooted file location; nothing for URLs, bare commands or folders.</summary>
    public static string? DefaultWorkingDirectory(string location)
    {
        if (string.IsNullOrWhiteSpace(location) || IsUri(location) || !Path.IsPathRooted(location))
        {
            return null;
        }

        if (Directory.Exists(location))
        {
            return null;
        }

        var directory = Path.GetDirectoryName(location);
        return string.IsNullOrEmpty(directory) ? null : directory;
    }

    /// <summary>True for <c>http://</c>, <c>https://</c>, <c>mailto:</c>, <c>ms-settings:</c> and similar; false for file paths.</summary>
    public static bool IsUri(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme.Length > 1
        && !string.Equals(uri.Scheme, Uri.UriSchemeFile, StringComparison.OrdinalIgnoreCase);

    public static bool IsSpecial(string? location) =>
        location is not null && location.TrimStart().StartsWith(SpecialScheme, StringComparison.OrdinalIgnoreCase);

    public static bool IsShellNamespace(string? location) =>
        location is not null && location.TrimStart().StartsWith(ShellScheme, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether a click on the shortcut brings its running program forward instead of starting another instance:
    /// its <see cref="ShortcutItem.WhenRunning"/> choice, else yes for programs and no for documents, folders and links.
    /// </summary>
    public static bool SwitchesWhenRunning(ShortcutItem shortcut)
    {
        ArgumentNullException.ThrowIfNull(shortcut);
        return shortcut.WhenRunning?.Trim().ToLowerInvariant() switch
        {
            ShortcutItem.WhenRunningSwitch => true,
            ShortcutItem.WhenRunningStart => false,
            _ => IsProgramLocation(shortcut.Location),
        };
    }

    /// <summary>A program: an <c>.exe</c> or <c>.com</c> file, a bare command (<c>notepad</c>), or a Store app from the Apps folder.</summary>
    public static bool IsProgramLocation(string? location)
    {
        var value = Environment.ExpandEnvironmentVariables(location?.Trim() ?? string.Empty);
        if (value.Length == 0 || IsSpecial(value))
        {
            return false;
        }

        if (IsShellNamespace(value)) // before IsUri: "shell:" parses as a URI scheme too
        {
            return PackagedApps.FamilyOfAppsFolderItem(value) is not null;
        }

        if (IsUri(value))
        {
            return false;
        }

        var path = value.TrimEnd('\\', '/');
        var cut = path.LastIndexOfAny(['\\', '/']);
        var fileName = cut >= 0 ? path[(cut + 1)..] : path;
        var extension = Path.GetExtension(fileName);
        return extension.Equals(".exe", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".com", StringComparison.OrdinalIgnoreCase)
               || (extension.Length == 0 && !Directory.Exists(value));
    }

    private static string Quote(string value) => value.Contains(' ') ? $"\"{value}\"" : value;
}
