using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using DesktopIconGrid.Core.Launching;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Core.Storage;
using DesktopIconGrid.Infrastructure;
using DesktopIconGrid.Resources;
using DesktopIconGrid.Services;

namespace DesktopIconGrid.ViewModels;

/// <summary>A shortcut's uses on this PC as the names table shows them (0.12.0 WP4).</summary>
/// <param name="Count">How many.</param>
/// <param name="Last">The last one, worded.</param>
/// <param name="Unused">Whether it is "not used in 90 days".</param>
public sealed record RowUsage(int Count, LastUse Last, bool Unused);

/// <summary>One row of the name table: an item with its original (derived) name and the name the user gave it.</summary>
public sealed class NameRowViewModel : ObservableObject
{
    private string _givenName;
    private bool _isMissing;

    public NameRowViewModel(LauncherItem item, string originalName, string kind, string? groupName, string location, RowUsage? usage = null)
    {
        Id = item.Id;
        OriginalName = originalName;
        Kind = kind;
        GroupName = groupName ?? string.Empty;
        Location = location;
        ModifiedAt = item.ModifiedAt?.LocalDateTime;
        ModifiedText = ModifiedAt?.ToString("g", CultureInfo.CurrentCulture) ?? string.Empty;
        InitialName = item.Name;
        _givenName = item.Name;
        InitialAlsoSay = item.AlsoSay ?? string.Empty;
        _alsoSay = InitialAlsoSay;
        CanBeSaid = item is ShortcutItem or SpecialIconItem;
        ColorTag = ColorTags.Find(item.ColorTag)?.Name;
        ColorTagName = ColorTagNames.Of(item.ColorTag);
        ColorTagBrush = ColorTags.Find(item.ColorTag) is { } tag ? ThemeManager.ToBrush(tag.Color) : null;
        if (usage is not null)
        {
            Uses = usage.Count;
            var used = usage.Last.Kind != LastUseKind.Never;
            LastUsed = used ? usage.Last.Local : DateTime.MinValue; // never used: older than any date
            LastUsedText = usage.Last.Kind switch
            {
                LastUseKind.Today => Strings.Names_Today,
                LastUseKind.Yesterday => Strings.Names_Yesterday,
                LastUseKind.DaysAgo => string.Format(CultureInfo.CurrentCulture, Strings.Names_DaysAgo, usage.Last.Days),
                LastUseKind.Date => usage.Last.Local.ToString("d", CultureInfo.CurrentCulture),
                _ => "—",
            };
            LastUsedToolTip = used ? usage.Last.Local.ToString("F", CultureInfo.CurrentCulture) : null;
            IsUnused = usage.Unused;
        }
    }

    /// <summary>How often the shortcut was used on this PC; <see langword="null"/> (an empty cell) for what is not a shortcut.</summary>
    public int? Uses { get; }

    /// <summary>The last use in local time, to sort by: a shortcut never used has the earliest date; other items none.</summary>
    public DateTime? LastUsed { get; }

    /// <summary>Today, yesterday, so many days ago up to 30, then the date; "—" when never used; empty for other items.</summary>
    public string LastUsedText { get; } = string.Empty;

    /// <summary>The last use's full local date and time.</summary>
    public string? LastUsedToolTip { get; }

    /// <summary>A shortcut "not used in 90 days" (<see cref="UsageRules.IsUnused"/>).</summary>
    public bool IsUnused { get; }

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

    /// <summary>
    /// <see cref="ModifiedAt"/> in Windows' regional format, as every date of the window. A binding's StringFormat
    /// followed the interface language, so an English bar on a zh-TW Windows showed 11/19/2026 12:33 AM beside
    /// 2026/11/18 (0.12.1, review 28 A3).
    /// </summary>
    public string ModifiedText { get; }

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

    public bool IsChanged => !string.Equals(_givenName.Trim(), InitialName, StringComparison.Ordinal) || IsAlsoSayChanged;

    public bool IsNameChanged => !string.Equals(_givenName.Trim(), InitialName, StringComparison.Ordinal);

    private string _alsoSay;

    public string InitialAlsoSay { get; }

    /// <summary>Whether voice launch starts this row's item at all (not a group), so the phrases are worth editing.</summary>
    public bool CanBeSaid { get; }

    /// <summary>Other phrases voice launch takes for the item, separated by <c>;</c> (<see cref="LauncherItem.AlsoSay"/>).</summary>
    public string AlsoSay
    {
        get => _alsoSay;
        set
        {
            if (CanBeSaid && SetProperty(ref _alsoSay, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(IsChanged));
            }
        }
    }

    public bool IsAlsoSayChanged =>
        !string.Equals(Core.Voice.VoiceVocabulary.NormalizeAlsoSay(_alsoSay) ?? string.Empty, InitialAlsoSay, StringComparison.Ordinal);

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

    /// <summary>The filter that shows the shortcuts not used in 90 days on this PC (0.12.0 WP4).</summary>
    public const string NotUsed90 = "-90";

    private readonly LauncherViewModel _root;
    private readonly DateTimeOffset _now;
    private readonly IReadOnlyDictionary<Guid, ItemUse> _uses;
    private readonly DateTimeOffset _since;
    private readonly TaskScheduler _uiScheduler = SynchronizationContext.Current is null ? TaskScheduler.Default : TaskScheduler.FromCurrentSynchronizationContext();
    private TagFilterOption _selectedTagFilter;

    /// <param name="root">The bar.</param>
    /// <param name="now">The time the counts are read at (tests); now by default. The counts do not tick while the window is open.</param>
    public NameTableViewModel(LauncherViewModel root, DateTimeOffset? now = null)
    {
        _root = root;
        _now = now ?? DateTimeOffset.UtcNow;
        _uses = root.Usage.Snapshot();
        _since = root.Usage.Since;
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
            new TagFilterOption(NotUsed90, Strings.Names_NotUsed90, null),
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
            NotUsed90 => row.IsUnused,
            _ => string.Equals(row.ColorTag, tag, StringComparison.Ordinal),
        };
    }

    public string Hint => Strings.Names_Hint;

    /// <summary>
    /// Since when this PC counts, and while that is less than 90 days, from when "Not used in 90 days" is complete: so
    /// nobody takes the counts for more than they are, or for something that left this PC.
    /// </summary>
    public string UsageLine
    {
        get
        {
            var since = string.Format(CultureInfo.CurrentCulture, Strings.Names_UsageSince, _since.ToLocalTime().ToString("d", CultureInfo.CurrentCulture));
            var complete = UsageRules.CompleteFrom(_since);
            if (_now >= complete)
            {
                return since;
            }

            var partial = string.Format(CultureInfo.CurrentCulture, Strings.Names_UsagePartial, complete.ToLocalTime().ToString("d", CultureInfo.CurrentCulture));
            return since.EndsWith('。') ? since + partial : $"{since} {partial}";
        }
    }

    public int ChangedCount => Rows.Count(r => (r.IsNameChanged && r.GivenName.Trim().Length > 0) || r.IsAlsoSayChanged);

    public ICommand UseOriginalCommand { get; }

    public ICommand UseOriginalForAllCommand { get; }

    /// <summary>Applies the edited names and voice phrases to the bar; returns how many items changed. Empty names are ignored.</summary>
    public int Apply()
    {
        var renamed = 0;
        foreach (var row in Rows)
        {
            var changed = row.IsNameChanged && row.GivenName.Trim().Length > 0 && _root.RenameItem(row.Id, row.GivenName.Trim(), save: false);
            changed |= row.IsAlsoSayChanged && _root.SetAlsoSay(row.Id, row.AlsoSay);
            if (changed)
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

    /// <summary>A shortcut's uses as read when the window opened.</summary>
    private RowUsage UsageOf(ShortcutItem shortcut)
    {
        var use = _uses.GetValueOrDefault(shortcut.Id);
        return new RowUsage(use?.Count ?? 0, LastUse.Of(use?.Last, _now, TimeZoneInfo.Local), UsageRules.IsUnused(use, _since, _now));
    }

    private NameRowViewModel CreateRow(LauncherItem item, string? groupName)
    {
        return item switch
        {
            ShortcutItem shortcut => new NameRowViewModel(item, ShortcutItem.SuggestName(shortcut.EffectiveLocation), Strings.Names_KindShortcut, groupName, shortcut.EffectiveLocation, UsageOf(shortcut)),
            GroupItem group => new NameRowViewModel(item, Strings.Common_Group, Strings.Common_Group, groupName, string.Format(Strings.Names_GroupItems, group.Items.Count(i => i is not SeparatorItem))),
            SpecialIconItem special => new NameRowViewModel(item, _root.SpecialIcons.Resolve(special.Kind).DisplayName, _root.SpecialIcons.Resolve(special.Kind).DisplayName, groupName, special.GetSetting("folderName") ?? special.GetSetting("drive") ?? string.Empty),
            _ => new NameRowViewModel(item, item.Name, Strings.Names_KindItem, groupName, string.Empty),
        };
    }
}
