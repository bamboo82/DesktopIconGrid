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
    public static LauncherModel FromLayout(LauncherLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        var model = new LauncherModel(layout.Settings?.Clone() ?? new LauncherSettings(), layout.Window)
        {
            FormatVersion = Math.Max(layout.Version, LauncherLayout.CurrentVersion),
            _unknownProperties = UnknownProperties.Copy(layout.ExtensionData),
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
        }
        else if (item is ShortcutItem shortcut)
        {
            shortcut.Location = shortcut.Location?.Trim() ?? string.Empty;
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
        else if (item.Name.Length == 0)
        {
            item.Name = "Group";
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
