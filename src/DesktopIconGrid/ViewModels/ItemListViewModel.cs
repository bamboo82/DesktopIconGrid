using System.Collections.ObjectModel;
using System.Windows.Input;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Infrastructure;
using DesktopIconGrid.Resources;

namespace DesktopIconGrid.ViewModels;

/// <summary>An ordered list of icons: the root of the bar, or the contents of one group.</summary>
public sealed class ItemListViewModel : ObservableObject
{
    public ItemListViewModel(LauncherViewModel root, GroupItem? group)
    {
        Root = root;
        Group = group;
        NewShortcutCommand = new RelayCommand(() => Root.NewShortcut(this, int.MaxValue));
        NewGroupCommand = new RelayCommand(() => Root.NewGroup(this, int.MaxValue), () => IsRoot);
        AddAppCommand = new RelayCommand(() => Root.AddApp(this, int.MaxValue));
        Sync(notifyRoot: false); // the owning group view model may still be constructing this list
    }

    public LauncherViewModel Root { get; }

    /// <summary><see langword="null"/> for the root list.</summary>
    public GroupItem? Group { get; }

    public bool IsRoot => Group is null;

    public ObservableCollection<ItemViewModel> Items { get; } = [];

    public bool IsEmpty => Items.Count == 0;

    public string EmptyHint => IsRoot ? Strings.Grid_EmptyRoot : Strings.Grid_EmptyGroup;

    public ICommand NewShortcutCommand { get; }

    public ICommand NewGroupCommand { get; }

    /// <summary>Add apps… at the end of this list.</summary>
    public ICommand AddAppCommand { get; }

    /// <summary>Reconciles the observable list with the model, keeping existing view models and moving containers instead of recreating them.</summary>
    public void Sync() => Sync(notifyRoot: true);

    private void Sync(bool notifyRoot)
    {
        var desired = Root.Model.ItemsOf(Group).Select(Root.GetOrCreateViewModel).ToList();

        for (var i = 0; i < desired.Count; i++)
        {
            var viewModel = desired[i];
            var currentIndex = Items.IndexOf(viewModel);
            if (currentIndex == i)
            {
                continue;
            }

            if (currentIndex >= 0)
            {
                Items.Move(currentIndex, i);
            }
            else
            {
                Items.Insert(i, viewModel);
            }
        }

        while (Items.Count > desired.Count)
        {
            Items.RemoveAt(Items.Count - 1);
        }

        OnPropertyChanged(nameof(IsEmpty));
        if (notifyRoot)
        {
            Root.NotifyListChanged(this);
        }
    }
}
