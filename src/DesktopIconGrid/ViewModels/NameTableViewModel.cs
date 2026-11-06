using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using DesktopIconGrid.Core.Launching;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Infrastructure;
using DesktopIconGrid.Resources;
using DesktopIconGrid.Services;

namespace DesktopIconGrid.ViewModels;

/// <summary>One row of the name table: an item with its original (derived) name and the name the user gave it.</summary>
public sealed class NameRowViewModel : ObservableObject
{
    private string _givenName;
    private bool _isMissing;

    public NameRowViewModel(LauncherItem item, string originalName, string kind, string? groupName, string location)
    {
        Id = item.Id;
        OriginalName = originalName;
        Kind = kind;
        GroupName = groupName ?? string.Empty;
        Location = location;
        ModifiedAt = item.ModifiedAt?.LocalDateTime;
        InitialName = item.Name;
        _givenName = item.Name;
        ColorTag = ColorTags.Find(item.ColorTag)?.Name;
        ColorTagName = ColorTagNames.Of(item.ColorTag);
        ColorTagBrush = ColorTags.Find(item.ColorTag) is { } tag ? ThemeManager.ToBrush(tag.Color) : null;
    }

    /// <summary>
    /// A shortcut whose target is not on this PC (see <see cref="LocationProbe"/>): the bar's own answer, and then its
    /// check on the icon worker, never on the window's thread.
    /// </summary>
    public bool IsMissing
    {
        get => _isMissing;
        internal set => SetProperty(ref _isMissing, value);
    }

    /// <summary>The item's known colour tag (<see cref="ColorTags"/>), or <see langword="null"/>.</summary>
    public string? ColorTag { get; }

    public string ColorTagName { get; }

    public Brush? ColorTagBrush { get; }

    public Guid Id { get; }

    /// <summary>The name the item would have had automatically (file name, catalog entry, kind).</summary>
    public string OriginalName { get; }

    public string Kind { get; }

    public string GroupName { get; }

    public string Location { get; }

    public DateTime? ModifiedAt { get; }

    public string InitialName { get; }

    /// <summary>The caption shown on the bar; also the phrase a future voice command will use.</summary>
    public string GivenName
    {
        get => _givenName;
        set
        {
            if (SetProperty(ref _givenName, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(IsChanged));
                OnPropertyChanged(nameof(IsOriginal));
            }
        }
    }

    public bool IsChanged => !string.Equals(_givenName.Trim(), InitialName, StringComparison.Ordinal);

    public bool IsOriginal => string.Equals(_givenName.Trim(), OriginalName, StringComparison.Ordinal);
}

/// <summary>One choice of the table's colour-tag filter.</summary>
/// <param name="Tag">A tag name, <see cref="NameTableViewModel.AllTags"/> or <see cref="NameTableViewModel.NoTag"/>.</param>
public sealed record TagFilterOption(string Tag, string Name, Brush? Brush);

/// <summary>
/// Backs the "Edit names" table: every item on the bar (separators aside) in one sortable grid with editable names,
/// which can be narrowed to the items with one colour tag.
/// </summary>
public sealed class NameTableViewModel : ObservableObject
{
    /// <summary>The filter that shows every row.</summary>
    public const string AllTags = "*";

    /// <summary>The filter that shows the rows without a colour tag.</summary>
    public const string NoTag = "";

    /// <summary>The filter that shows the shortcuts whose target is not on this PC.</summary>
    public const string MissingHere = "?";

    private readonly LauncherViewModel _root;
    private readonly TaskScheduler _uiScheduler = SynchronizationContext.Current is null ? TaskScheduler.Default : TaskScheduler.FromCurrentSynchronizationContext();
    private TagFilterOption _selectedTagFilter;

    public NameTableViewModel(LauncherViewModel root)
    {
        _root = root;
        foreach (var item in root.Model.Items)
        {
            if (item is not SeparatorItem)
            {
                Rows.Add(CreateRow(item, groupName: null));
            }

            if (item is GroupItem group)
            {
                foreach (var child in group.Items.Where(c => c is not SeparatorItem))
                {
                    Rows.Add(CreateRow(child, group.Name));
                }
            }
        }

        TagFilters =
        [
            new TagFilterOption(AllTags, Strings.Names_AllItems, null),
            .. ColorTags.All.Select(t => new TagFilterOption(t.Name, ColorTagNames.Of(t.Name), ThemeManager.ToBrush(t.Color))),
            new TagFilterOption(NoTag, Strings.Names_Untagged, null),
            new TagFilterOption(MissingHere, Strings.Names_MissingHere, null),
        ];
        _selectedTagFilter = TagFilters[0];
        RowsView = CollectionViewSource.GetDefaultView(Rows);
        RowsView.Filter = row => row is NameRowViewModel r && Matches(r, _selectedTagFilter.Tag);

        MissingChecked = CheckMissingAsync();

        UseOriginalCommand = new RelayCommand<NameRowViewModel>(row => row.GivenName = row.OriginalName, row => row is not null && !row.IsOriginal);
        UseOriginalForAllCommand = new RelayCommand(() =>
        {
            foreach (var row in Rows)
            {
                row.GivenName = row.OriginalName;
            }
        }, () => Rows.Any(r => !r.IsOriginal));
    }

    public ObservableCollection<NameRowViewModel> Rows { get; } = [];

    /// <summary>Done when every shortcut's target has been checked (tests wait for it).</summary>
    internal Task MissingChecked { get; }

    /// <summary>
    /// Marks the shortcuts whose target is missing: at once from the bar's icons where it has them, and from the same
    /// check on the icon worker for every shortcut, group members the bar has not shown yet included.
    /// </summary>
    private Task CheckMissingAsync()
    {
        var checks = new List<Task>();
        foreach (var row in Rows)
        {
            if (_root.Model.Find(row.Id) is not ShortcutItem shortcut)
            {
                continue;
            }

            row.IsMissing = _root.KnownMissing(row.Id) ?? false;
            checks.Add(_root.IsMissingAsync(shortcut).ContinueWith(
                task =>
                {
                    row.IsMissing = task.Result;
                    if (_selectedTagFilter.Tag == MissingHere)
                    {
                        RowsView.Refresh();
                    }
                },
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnRanToCompletion,
                _uiScheduler));
        }

        return Task.WhenAll(checks);
    }

    /// <summary>The rows the table shows: <see cref="Rows"/> through the colour-tag filter.</summary>
    public ICollectionView RowsView { get; }

    public IReadOnlyList<TagFilterOption> TagFilters { get; }

    public TagFilterOption SelectedTagFilter
    {
        get => _selectedTagFilter;
        set
        {
            if (value is not null && SetProperty(ref _selectedTagFilter, value))
            {
                RowsView.Refresh();
            }
        }
    }

    /// <summary>True when the row passes the filter <paramref name="tag"/> (<see cref="AllTags"/>, <see cref="NoTag"/> or a tag name).</summary>
    public static bool Matches(NameRowViewModel row, string tag)
    {
        ArgumentNullException.ThrowIfNull(row);
        return tag switch
        {
            AllTags => true,
            NoTag => row.ColorTag is null,
            MissingHere => row.IsMissing,
            _ => string.Equals(row.ColorTag, tag, StringComparison.Ordinal),
        };
    }

    public string Hint => Strings.Names_Hint;

    public int ChangedCount => Rows.Count(r => r.IsChanged && r.GivenName.Trim().Length > 0);

    public ICommand UseOriginalCommand { get; }

    public ICommand UseOriginalForAllCommand { get; }

    /// <summary>Applies the edited names to the bar. Empty names are ignored.</summary>
    public int Apply()
    {
        var renamed = 0;
        foreach (var row in Rows.Where(r => r.IsChanged && r.GivenName.Trim().Length > 0))
        {
            if (_root.RenameItem(row.Id, row.GivenName.Trim(), save: false))
            {
                renamed++;
            }
        }

        if (renamed > 0)
        {
            _root.SaveNow(contentChanged: true);
        }

        return renamed;
    }

    private NameRowViewModel CreateRow(LauncherItem item, string? groupName)
    {
        return item switch
        {
            ShortcutItem shortcut => new NameRowViewModel(item, ShortcutItem.SuggestName(shortcut.EffectiveLocation), Strings.Names_KindShortcut, groupName, shortcut.EffectiveLocation),
            GroupItem group => new NameRowViewModel(item, Strings.Common_Group, Strings.Common_Group, groupName, string.Format(Strings.Names_GroupItems, group.Items.Count(i => i is not SeparatorItem))),
            SpecialIconItem special => new NameRowViewModel(item, _root.SpecialIcons.Resolve(special.Kind).DisplayName, _root.SpecialIcons.Resolve(special.Kind).DisplayName, groupName, special.GetSetting("folderName") ?? special.GetSetting("drive") ?? string.Empty),
            _ => new NameRowViewModel(item, item.Name, Strings.Names_KindItem, groupName, string.Empty),
        };
    }
}
