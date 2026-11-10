using DesktopIconGrid.Core.Models;

namespace DesktopIconGrid.Core.Imports;

/// <summary>What <c>Import…</c> may offer, and the items the chosen candidates become.</summary>
public static class ImportPlan
{
    /// <summary>
    /// Whether a candidate can go on the bar as written. Its name goes into the parameters (<c>wt -p "Ubuntu"</c>,
    /// <c>putty -load "web"</c>, <c>wt ssh web</c>), so a name that would end the quotes it is given, start an option,
    /// hold a line break, or start another tab in Windows Terminal (<c>;</c>) is not offered. Names come from other
    /// programs' files, and a synced or copied file may hold anything.
    /// </summary>
    public static bool IsUsable(ImportCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        var name = candidate.Name;
        if (string.IsNullOrWhiteSpace(name) || name.StartsWith('-') || name.Any(c => char.IsControl(c) || c is '"' or '＂'))
        {
            return false;
        }

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
