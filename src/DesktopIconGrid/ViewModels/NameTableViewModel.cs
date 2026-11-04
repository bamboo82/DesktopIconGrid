using System.Collections.ObjectModel;
using System.Windows.Input;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Infrastructure;

namespace DesktopIconGrid.ViewModels;

/// <summary>One row of the name table: an item with its original (derived) name and the name the user gave it.</summary>
public sealed class NameRowViewModel : ObservableObject
{
    private string _givenName;

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
    }

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

/// <summary>Backs the "Edit names" table: every item on the bar in one sortable grid with editable names.</summary>
public sealed class NameTableViewModel : ObservableObject
{
    private readonly LauncherViewModel _root;

    public NameTableViewModel(LauncherViewModel root)
    {
        _root = root;
        foreach (var item in root.Model.Items)
        {
            Rows.Add(CreateRow(item, groupName: null));
            if (item is GroupItem group)
            {
                foreach (var child in group.Items)
                {
                    Rows.Add(CreateRow(child, group.Name));
                }
            }
        }

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

    public string Hint =>
        "Names identify your shortcuts on the bar and will be the phrases for voice commands. " +
        "Click a header to sort; edit the Given name column; Save applies every change.";

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
            ShortcutItem shortcut => new NameRowViewModel(item, ShortcutItem.SuggestName(shortcut.Location), "Shortcut", groupName, shortcut.Location),
            GroupItem group => new NameRowViewModel(item, "Group", "Group", groupName, $"{group.Items.Count} item(s)"),
            SpecialIconItem special => new NameRowViewModel(item, _root.SpecialIcons.Resolve(special.Kind).DisplayName, _root.SpecialIcons.Resolve(special.Kind).DisplayName, groupName, special.GetSetting("folderName") ?? special.GetSetting("drive") ?? string.Empty),
            _ => new NameRowViewModel(item, item.Name, "Item", groupName, string.Empty),
        };
    }
}
