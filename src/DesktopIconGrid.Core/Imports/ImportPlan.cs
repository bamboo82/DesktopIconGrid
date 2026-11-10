using DesktopIconGrid.Core.Models;

namespace DesktopIconGrid.Core.Imports;

/// <summary>What <c>Import…</c> may offer, and the items the chosen candidates become.</summary>
public static class ImportPlan
{
    /// <summary>
    /// Whether a candidate can go on the bar as written. Its name goes into the parameters (<c>wt -p "Ubuntu"</c>,
    /// <c>putty -load "web"</c>, <c>WinSCP.exe "web"</c>, <c>wt ssh web</c>), and a click fills in no placeholder there,
    /// so nothing checks it again. A name is not offered when it would:
    /// <list type="bullet">
    /// <item>end the quotes it is given (<c>"</c>, <c>＂</c>), or escape the closing one (a backslash at its end);</item>
    /// <item>start an option or a switch (<c>-</c>, or <c>/</c>, which WinSCP reads as <c>/script=…</c> once the
    /// quotes are gone);</item>
    /// <item>hold a line break, or a <c>%</c>, which is expanded again at every click;</item>
    /// <item>start another tab in Windows Terminal (<c>;</c>), or, as an SSH host, hold what a shell acts on.</item>
    /// </list>
    /// Names come from other programs' files and registry keys, which a synced or copied file may fill with anything.
    /// </summary>
    public static bool IsUsable(ImportCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        var name = candidate.Name;
        if (string.IsNullOrWhiteSpace(name) || name.StartsWith('-') || name.StartsWith('/') || name.EndsWith('\\')
            || name.Any(c => char.IsControl(c) || c is '"' or '＂' or '%'))
        {
            return false;
        }

        // Both Windows Terminal templates (profiles, SSH hosts) name it "wt.exe"; another spelling needs adding here.
        var program = Path.GetFileName(candidate.Location.Trim().Trim('"'));
        var terminal = program.Equals("wt.exe", StringComparison.OrdinalIgnoreCase) || program.Equals("wt", StringComparison.OrdinalIgnoreCase);
        return !(terminal && name.Contains(';', StringComparison.Ordinal))
               && !(candidate.Source == ImportSource.SshHosts && name.Any(c => char.IsWhiteSpace(c) || c is ';' or '&' or '|' or '<' or '>' or '^' or '%' or '!' or '`' or '$'));
    }

    /// <summary>
    /// The items the chosen candidates become, in the order given: one shortcut each, or, with
    /// <paramref name="groupPerSource"/>, one group per source (named by <paramref name="groupName"/>) holding them.
    /// <paramref name="forStorage"/> writes a location as the layout keeps it (<c>%APPDRIVE%</c> in portable mode).
    /// </summary>
    public static IReadOnlyList<LauncherItem> Build(
        IEnumerable<ImportCandidate> chosen, bool groupPerSource, Func<ImportSource, string> groupName, Func<string, string> forStorage)
    {
        ArgumentNullException.ThrowIfNull(chosen);
        ArgumentNullException.ThrowIfNull(groupName);
        ArgumentNullException.ThrowIfNull(forStorage);
        var shortcuts = chosen.Select(c => (c.Source, Shortcut: new ShortcutItem
        {
            Name = c.Name.Trim(),
            Location = forStorage(c.Location.Trim()),
            Parameters = string.IsNullOrWhiteSpace(c.Parameters) ? null : c.Parameters.Trim(),
            StartIn = string.IsNullOrWhiteSpace(c.StartIn) ? null : forStorage(c.StartIn.Trim()),
        })).ToList();
        if (!groupPerSource)
        {
            return shortcuts.Select(s => (LauncherItem)s.Shortcut).ToList();
        }

        return shortcuts
            .GroupBy(s => s.Source)
            .Select(g => (LauncherItem)new GroupItem { Name = groupName(g.Key), Items = g.Select(s => (LauncherItem)s.Shortcut).ToList() })
            .ToList();
    }

    /// <summary>What decides whether a shortcut is already on the bar, as <see cref="ImportCandidate.Key"/> does: its location, expanded, with its parameters.</summary>
    public static string KeyOf(ShortcutItem shortcut)
    {
        ArgumentNullException.ThrowIfNull(shortcut);
        var location = Environment.ExpandEnvironmentVariables(shortcut.EffectiveLocation.Trim());
        return string.IsNullOrWhiteSpace(shortcut.Parameters) ? location : $"{location} {shortcut.Parameters.Trim()}";
    }
}
