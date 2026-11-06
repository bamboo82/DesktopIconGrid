using System.Text.Json;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Core.Resources;
using DesktopIconGrid.Core.Storage;

namespace DesktopIconGrid.Core;

/// <summary>
/// The in-memory state of the bar: settings, window placement and the ordered item tree
/// (root list of shortcuts and groups; each group holds its own list of shortcuts).
/// All structural edits go through this class so ordering rules stay in one place.
/// </summary>
public sealed class LauncherModel
{
    private readonly List<LauncherItem> _items = [];
    private Dictionary<string, JsonElement>? _unknownProperties;

    public LauncherModel()
        : this(new LauncherSettings(), null)
    {
    }

    private LauncherModel(LauncherSettings settings, WindowPlacement? window)
    {
        Settings = settings;
        Settings.Normalize();
        Window = window;
    }

    public LauncherSettings Settings { get; }

    public WindowPlacement? Window { get; set; }

    /// <summary>Top-level items in bar order.</summary>
    public IReadOnlyList<LauncherItem> Items => _items;

    /// <summary>Number of items that were moved, renamed or re-identified while loading a layout.</summary>
    public int RepairCount { get; private set; }

    /// <summary>
    /// The layout format this model is saved as: this app's version, or the higher version of the file it was
    /// loaded from, so a newer app that reads the file again does not migrate its own items a second time.
    /// </summary>
    public int FormatVersion { get; private set; } = LauncherLayout.CurrentVersion;

    /// <summary>Shortcuts at the root plus the shortcuts inside every group.</summary>
    public int TotalShortcutCount => AllShortcuts().Count();

    // ----- loading / saving -------------------------------------------------------------

    /// <summary>Builds a model from a layout file, repairing duplicate ids and nested groups.</summary>
    public static LauncherModel FromLayout(LauncherLayout layout) => FromLayout(layout, exists: null, appDrive: null, systemDrive: null);

    /// <summary>
    /// Builds a model from a layout file, repairing duplicate ids and nested groups, and giving this PC its own location
    /// for a path that 0.8.0 wrote with <c>%APPDRIVE%</c> in Windows' own folders when that is where it is here (see
    /// <see cref="RepairForThisPc"/>). By default the files are looked for here, and the drives and the PC are this one's
    /// (<c>%APPDRIVE%</c>, <c>%SystemDrive%</c>, <see cref="ThisDevice.Id"/>).
    /// </summary>
    public static LauncherModel FromLayout(
        LauncherLayout layout, Func<string, bool>? exists, string? appDrive, string? systemDrive, string? deviceId = null, Func<string, bool>? isSlowDrive = null)
    {
        ArgumentNullException.ThrowIfNull(layout);
        appDrive ??= Environment.GetEnvironmentVariable(DataLocation.AppDriveVariable);
        systemDrive ??= Environment.GetEnvironmentVariable("SystemDrive");
        deviceId ??= ThisDevice.Id;
        var model = new LauncherModel(layout.Settings?.Clone() ?? new LauncherSettings(), layout.Window)
        {
            FormatVersion = Math.Max(layout.Version, LauncherLayout.CurrentVersion),
            _unknownProperties = UnknownProperties.Copy(layout.ExtensionData),
            _driveRepair = deviceId is not null && appDrive is { Length: 2 } && systemDrive is { Length: 2 }
                           && !string.Equals(appDrive, systemDrive, StringComparison.OrdinalIgnoreCase)
                ? new DriveRepair(exists ?? (path => File.Exists(path) || Directory.Exists(path)), appDrive, systemDrive, deviceId, isSlowDrive ?? IsSlowDrive)
                : null,
        };
        if (layout.Window is { IsUsable: false })
        {
            model.Window = null;
        }

        var seen = new HashSet<Guid>();
        foreach (var item in layout.Items ?? [])
        {
            if (item is null)
            {
                model.RepairCount++;
                continue;
            }

            var copy = item.Clone();
            model.Adopt(copy, seen);
            if (copy is GroupItem group)
            {
                var flattened = new List<LauncherItem>();
                model.Flatten(group.Items, flattened, seen);
                group.Items = flattened;
            }

            model._items.Add(copy);
        }

        model._driveRepair = null; // items added later are this version's
        return model;
    }

    public LauncherLayout ToLayout() => new()
    {
        Version = FormatVersion,
        Settings = Settings.Clone(),
        Window = Window,
        Items = _items.Select(i => i.Clone()).ToList(),
        ExtensionData = UnknownProperties.Copy(_unknownProperties),
    };

    /// <summary>A path of 0.8.0's under Windows' own folders, with its drive back (see <see cref="DataLocation.WithWindowsDrive"/>); counted as a repair.</summary>
    /// <summary>What <see cref="RepairForThisPc"/> needs: whether a path is there, the two drives, this PC, and whether a drive is slow to look at.</summary>
    private sealed record DriveRepair(Func<string, bool> Exists, string AppDrive, string SystemDrive, string DeviceId, Func<string, bool> IsSlowDrive);

    private DriveRepair? _driveRepair;

    /// <summary>A drive whose look may hang or wake hardware (removable, optical, network) or that is not there.</summary>
    private static bool IsSlowDrive(string drive)
    {
        try
        {
            return new DriveInfo(drive).DriveType is not (DriveType.Fixed or DriveType.Ram);
        }
        catch (ArgumentException)
        {
            return true;
        }
    }

    /// <summary>
    /// A path 0.8.0 wrote with <c>%APPDRIVE%</c> in Windows' own folders, from a portable copy on Windows' drive, is
    /// Windows' drive on the PC where it is there and missing where <c>%APPDRIVE%</c> points. That decides for this PC
    /// alone: the shortcut gets this PC's own location (or its own one is rewritten), and the shared location, the start
    /// folder and the icon stay as they are, since a copy on another PC's second disk may mean its own D: by them. Windows'
    /// drive is looked at first; a slow app drive (a stick, a share) counts as missing without a look. It is safe to repeat:
    /// a path already on Windows' drive is no candidate.
    /// </summary>
    private void RepairForThisPc(ShortcutItem shortcut)
    {
        if (_driveRepair is not { } repair)
        {
            return;
        }

        var own = shortcut.Locations is { } locations && locations.TryGetValue(repair.DeviceId, out var mine) ? mine : null;
        var path = (own ?? shortcut.Location)?.Trim();
        var healed = DataLocation.WithWindowsDrive(path, repair.SystemDrive);
        if (path is null || healed is null || string.Equals(healed, path, StringComparison.Ordinal) || !repair.Exists(healed))
        {
            return;
        }

        var prefix = $"%{DataLocation.AppDriveVariable}%";
        if (!repair.IsSlowDrive(repair.AppDrive) && repair.Exists(repair.AppDrive + path[prefix.Length..]))
        {
            return; // there where %APPDRIVE% points: this copy's own (a second disk's Program Files)
        }

        shortcut.SetLocationFor(repair.DeviceId, healed);
        RepairCount++;
    }

    private void Adopt(LauncherItem item, HashSet<Guid> seen)
    {
        if (item.Id == Guid.Empty || !seen.Add(item.Id))
        {
            item.Id = Guid.NewGuid();
            seen.Add(item.Id);
            RepairCount++;
        }

        item.Name = item.Name?.Trim() ?? string.Empty;
        item.TextIcon = item.TextIcon?.Normalized();
        item.ColorTag = ColorTags.Normalize(item.ColorTag);
        if (item is SeparatorItem separator)
        {
            separator.Kind = separator.Kind?.Trim() is { Length: > 0 } kind ? kind : SeparatorItem.Line; // a caption may be empty
            if (SeparatorItem.SavedName(separator.Kind) is not null)
            {
                separator.Name = string.Empty; // only a caption has a name; a line's or gap's is there for 0.6.0
            }
        }
        else if (item is ShortcutItem shortcut)
        {
            shortcut.Location = shortcut.Location?.Trim() ?? string.Empty;
            RepairForThisPc(shortcut);

            if (shortcut.Name.Length == 0)
            {
                shortcut.Name = ShortcutItem.SuggestName(shortcut.Location);
            }
        }
        else if (item is SpecialIconItem special)
        {
            special.Kind = special.Kind?.Trim() ?? string.Empty;
            special.Settings ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (item.Name.Length == 0)
            {
                item.Name = special.Kind.Length == 0 ? CoreStrings.Item_SpecialIconPlaceholder : special.Kind;
            }
        }
        else if (item is GroupItem && item.Name.Length == 0)
        {
            item.Name = "Group"; // not an item from a newer version: it is written back as it was read, name and all
        }
    }

    private void Flatten(IEnumerable<LauncherItem> source, List<LauncherItem> target, HashSet<Guid> seen)
    {
        foreach (var child in source)
        {
            switch (child)
            {
                case ShortcutItem or SpecialIconItem or SeparatorItem or UnknownItem:
                    Adopt(child, seen);
                    target.Add(child);
                    break;
                case GroupItem nested:
                    RepairCount++;
                    Flatten(nested.Items, target, seen);
                    break;
                default:
                    RepairCount++;
                    break;
            }
        }
    }

    // ----- lookups --------------------------------------------------------------------------

    public LauncherItem? Find(Guid id) => Find(id, out _);

    /// <summary>Finds an item anywhere in the tree and reports the group that contains it (null at the root).</summary>
    public LauncherItem? Find(Guid id, out GroupItem? parent)
    {
        foreach (var item in _items)
        {
            if (item.Id == id)
            {
                parent = null;
                return item;
            }

            if (item is GroupItem group)
            {
                var child = group.Items.Find(c => c.Id == id);
                if (child is not null)
                {
                    parent = group;
                    return child;
                }
            }
        }

        parent = null;
        return null;
    }

    public GroupItem? FindGroup(Guid id) => Find(id) as GroupItem;

    public bool Contains(Guid id) => Find(id) is not null;

    /// <summary>The list a parent owns: the root list for <see langword="null"/>, otherwise the group's items.</summary>
    public IReadOnlyList<LauncherItem> ItemsOf(GroupItem? parent) => ListOf(parent);

    public IEnumerable<ShortcutItem> AllShortcuts()
    {
        foreach (var item in _items)
        {
            switch (item)
            {
                case ShortcutItem shortcut:
                    yield return shortcut;
                    break;
                case GroupItem group:
                    foreach (var child in group.Items.OfType<ShortcutItem>())
                    {
                        yield return child;
                    }

                    break;
            }
        }
    }

    /// <summary>Shortcuts flagged for Multi Launch, in bar order (root first, then each group in order).</summary>
    public IEnumerable<ShortcutItem> MultiLaunchMembers() => AllShortcuts().Where(s => s.MultiLaunch);

    // ----- edits ------------------------------------------------------------------------------

    /// <summary>
    /// Inserts a new item into the root list or into a group. <paramref name="index"/> is clamped to the
    /// list; groups can only be inserted at the root.
    /// </summary>
    public void Insert(LauncherItem item, GroupItem? parent, int index)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (Contains(item.Id))
        {
            throw new InvalidOperationException("The item is already on the bar.");
        }

        if (item is GroupItem && parent is not null)
        {
            throw new InvalidOperationException("Groups cannot be placed inside other groups.");
        }

        item.ModifiedAt ??= DateTimeOffset.UtcNow;
        var list = ListOf(parent);
        list.Insert(Math.Clamp(index, 0, list.Count), item);
    }

    public void Add(LauncherItem item, GroupItem? parent = null) => Insert(item, parent, int.MaxValue);

    public bool Remove(Guid id)
    {
        var item = Find(id, out var parent);
        if (item is null)
        {
            return false;
        }

        return ListOf(parent).Remove(item);
    }

    /// <summary>
    /// Moves an item so that it ends up at <paramref name="insertBeforeIndex"/> of the target list, where the
    /// index refers to the target list as it is <em>before</em> the move (the way a drop position is computed).
    /// Reordering within one list and moving between the root and a group are both supported.
    /// </summary>
    public void Move(Guid id, GroupItem? targetParent, int insertBeforeIndex)
    {
        var item = Find(id, out var sourceParent) ?? throw new KeyNotFoundException($"No item with id {id}.");
        if (item is GroupItem && targetParent is not null)
        {
            throw new InvalidOperationException("Groups cannot be placed inside other groups.");
        }

        if (targetParent is not null && !ReferenceEquals(Find(targetParent.Id), targetParent))
        {
            throw new InvalidOperationException("The target group is not on the bar.");
        }

        var source = ListOf(sourceParent);
        var target = ListOf(targetParent);
        var sourceIndex = source.IndexOf(item);
        var targetIndex = Math.Clamp(insertBeforeIndex, 0, target.Count);

        if (ReferenceEquals(source, target) && sourceIndex < targetIndex)
        {
            targetIndex--;
        }

        source.RemoveAt(sourceIndex);
        target.Insert(Math.Clamp(targetIndex, 0, target.Count), item);
    }

    /// <summary>Appends an item to the end of a group.</summary>
    public void MoveIntoGroup(Guid id, GroupItem group)
    {
        ArgumentNullException.ThrowIfNull(group);
        Move(id, group, int.MaxValue);
    }

    /// <summary>Replaces the editable content of an existing item with the values from <paramref name="edited"/> (matched by id).</summary>
    public void Update(LauncherItem edited)
    {
        ArgumentNullException.ThrowIfNull(edited);
        var existing = Find(edited.Id) ?? throw new KeyNotFoundException($"No item with id {edited.Id}.");
        if (existing.GetType() != edited.GetType())
        {
            throw new InvalidOperationException("An item cannot change kind.");
        }

        existing.CopyContentFrom(edited);
        existing.ModifiedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Changes an item's caption. Returns false when the item is unknown, the name is blank, or nothing changed.</summary>
    public bool Rename(Guid id, string name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        var item = Find(id);
        if (item is null || trimmed.Length == 0 || string.Equals(item.Name, trimmed, StringComparison.Ordinal))
        {
            return false;
        }

        item.Name = trimmed;
        item.ModifiedAt = DateTimeOffset.UtcNow;
        return true;
    }

    public void ApplySettings(LauncherSettings settings) => Settings.CopyFrom(settings);

    private List<LauncherItem> ListOf(GroupItem? parent) => parent?.Items ?? _items;
}
