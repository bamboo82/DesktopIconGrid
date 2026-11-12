using System.Text.Json;
using DesktopIconGrid.Core.Diagnostics;
using DesktopIconGrid.Core.Models;

namespace DesktopIconGrid.Core.Launching;

/// <summary>Where a workspace member's window goes: its rectangle in device pixels (the restored one when maximised).</summary>
public sealed record SavedWindow(int Left, int Top, int Width, int Height, bool Maximized)
{
    /// <summary>The least of a window that has to be on a monitor for it to count as visible there (enough to grab its title bar).</summary>
    public const int MinVisibleWidth = 120;

    public const int MinVisibleHeight = 40;

    /// <summary>
    /// This position if enough of it is on one of <paramref name="monitors"/>; otherwise (the monitor it was remembered on
    /// is gone: undocked, another screen setup) moved onto the first monitor, and made to fit it. Unchanged when no
    /// monitor is known.
    /// </summary>
    public SavedWindow OnScreen(IReadOnlyList<Models.PixelRect> monitors)
    {
        ArgumentNullException.ThrowIfNull(monitors);
        if (monitors.Count == 0 || monitors.Any(IsVisibleOn))
        {
            return this;
        }

        var first = monitors[0];
        var width = Math.Min(Width, first.Width);
        var height = Math.Min(Height, first.Height);
        return this with
        {
            Left = first.Left + Math.Min(40, first.Width - width),
            Top = first.Top + Math.Min(40, first.Height - height),
            Width = width,
            Height = height,
        };
    }

    private bool IsVisibleOn(Models.PixelRect monitor)
    {
        var width = Math.Min(Left + Width, monitor.Right) - Math.Max(Left, monitor.Left);
        var height = Math.Min(Top + Height, monitor.Bottom) - Math.Max(Top, monitor.Top);
        var topOnScreen = Top >= monitor.Top - 8 && Top < monitor.Bottom - MinVisibleHeight; // the title bar, not only a bottom strip
        return width >= Math.Min(MinVisibleWidth, Width) && height >= Math.Min(MinVisibleHeight, Height) && topOnScreen;
    }
}

/// <summary>
/// Workspaces (0.10.0): named sets of shortcuts started together, each remembering where its windows go on this PC.
/// The Multi Launch group of earlier versions is the workspace <see cref="MultiLaunch"/> (its members keep their
/// <see cref="ShortcutItem.MultiLaunch"/> flag, so older versions still see the group); other workspaces are names in
/// <see cref="ShortcutItem.Workspaces"/>.
/// </summary>
public static class Workspaces
{
    /// <summary>The name of the built-in workspace, in every language: it is a key, the menus show their own wording.</summary>
    public const string MultiLaunch = "Multi Launch";

    public const int MaxNameLength = 40;

    /// <summary>A name as it is stored, or <see langword="null"/> when it cannot be one (empty, too long, a line break, the built-in name).</summary>
    public static string? Normalize(string? name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        return trimmed.Length is 0 or > MaxNameLength || !SpecialIcons.CommandTileSettings.IsShowable(trimmed) || Same(trimmed, MultiLaunch) ? null : trimmed;
    }

    /// <summary>The same workspace: any case, and spaces around a name (a hand-edited or synced one) do not make another.</summary>
    public static bool Same(string a, string b) => string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);

    public static bool IsMember(ShortcutItem shortcut, string workspace)
    {
        ArgumentNullException.ThrowIfNull(shortcut);
        return Same(workspace, MultiLaunch) ? shortcut.MultiLaunch : shortcut.Workspaces?.Any(name => Same(name, workspace)) == true;
    }

    /// <summary>Puts the shortcut into the workspace or takes it out.</summary>
    public static void SetMember(ShortcutItem shortcut, string workspace, bool member)
    {
        ArgumentNullException.ThrowIfNull(shortcut);
        if (Same(workspace, MultiLaunch))
        {
            shortcut.MultiLaunch = member;
            return;
        }

        if (Normalize(workspace) is not { } name)
        {
            return;
        }

        var names = shortcut.Workspaces?.Where(existing => !Same(existing, name)).ToList() ?? [];
        if (member)
        {
            names.Add(name);
        }

        shortcut.Workspaces = names.Count == 0 ? null : names;
    }

    /// <summary>The workspaces of the bar: the built-in one when it has members, then the others by name.</summary>
    public static IReadOnlyList<string> Names(IEnumerable<ShortcutItem> shortcuts)
    {
        var all = shortcuts.ToList();
        var names = all.SelectMany(s => s.Workspaces ?? [])
            .Select(Normalize)
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        if (all.Any(s => s.MultiLaunch))
        {
            names.Insert(0, MultiLaunch);
        }

        return names;
    }

    public static IReadOnlyList<ShortcutItem> Members(IEnumerable<ShortcutItem> shortcuts, string workspace) =>
        shortcuts.Where(shortcut => IsMember(shortcut, workspace)).ToList();
}

/// <summary>
/// Where each workspace's windows go on this PC, by workspace name and shortcut id. Kept in a local file that is never
/// synced: pixel positions belong to this PC's monitors.
/// </summary>
public sealed class WorkspacePositions
{
    public const string FileName = "workspaces.json";

    /// <summary>Far more than any bar's positions take; a larger file is not this app's.</summary>
    public const long MaxFileBytes = 1024 * 1024;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly string? _filePath;
    private Dictionary<string, Dictionary<Guid, SavedWindow>> _positions;

    /// <param name="filePath">The file; <see langword="null"/> keeps the positions in memory only (tests).</param>
    public WorkspacePositions(string? filePath)
    {
        _filePath = filePath;
        _positions = Load(filePath);
    }

    public SavedWindow? Get(string workspace, Guid shortcut) =>
        _positions.TryGetValue(workspace, out var windows) && windows.TryGetValue(shortcut, out var saved) ? saved : null;

    /// <summary>
    /// Keeps <paramref name="windows"/> for the workspace and saves; false when the file could not be written. What was
    /// kept before for a shortcut in <paramref name="keep"/> stays when it has no window now (a member that is closed
    /// does not lose its place); everything else of the workspace is replaced.
    /// </summary>
    public bool Set(string workspace, IReadOnlyDictionary<Guid, SavedWindow> windows, IReadOnlyCollection<Guid>? keep = null)
    {
        ArgumentNullException.ThrowIfNull(windows);
        var merged = new Dictionary<Guid, SavedWindow>(windows);
        if (keep is not null && _positions.TryGetValue(workspace, out var before))
        {
            foreach (var id in keep)
            {
                if (!merged.ContainsKey(id) && before.TryGetValue(id, out var earlier))
                {
                    merged[id] = earlier;
                }
            }
        }

        if (merged.Count == 0)
        {
            _positions.Remove(workspace);
        }
        else
        {
            _positions[workspace] = merged;
        }

        return Save();
    }

    private static Dictionary<string, Dictionary<Guid, SavedWindow>> Load(string? filePath)
    {
        var empty = new Dictionary<string, Dictionary<Guid, SavedWindow>>(StringComparer.OrdinalIgnoreCase);
        if (filePath is null || !File.Exists(filePath))
        {
            return empty;
        }

        try
        {
            if (new FileInfo(filePath).Length > MaxFileBytes)
            {
                AppLog.Default.Warning("Workspaces", "The window positions file is too large to be one; it is not read");
                return empty;
            }

            var read = JsonSerializer.Deserialize<Dictionary<string, Dictionary<Guid, SavedWindow>>>(File.ReadAllBytes(filePath), Json);
            foreach (var (name, windows) in read ?? [])
            {
                if (windows is not null)
                {
                    empty[name] = windows.Where(pair => pair.Value is { Width: > 0, Height: > 0 }).ToDictionary(pair => pair.Key, pair => pair.Value);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            AppLog.Default.Warning("Workspaces", $"The window positions could not be read: {ex.Message}");
        }

        return empty;
    }

    private bool Save()
    {
        if (_filePath is null)
        {
            return true;
        }

        var temporary = _filePath + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(_positions, Json));
            File.Move(temporary, _filePath, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Default.Warning("Workspaces", $"The window positions could not be saved: {ex.Message}");
            return false;
        }
    }
}
