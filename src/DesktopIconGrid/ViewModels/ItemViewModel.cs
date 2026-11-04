using System.Windows.Input;
using System.Windows.Media;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Infrastructure;

namespace DesktopIconGrid.ViewModels;

/// <summary>One icon on the bar or inside a group popup.</summary>
public sealed class ItemViewModel : ObservableObject
{
    private ImageSource? _image;
    private bool _isDropTarget;
    private ItemListViewModel? _children;

    public ItemViewModel(LauncherViewModel root, LauncherItem model)
    {
        Root = root;
        Model = model;

        ActivateCommand = new RelayCommand(() => Root.Activate(this, anchor: null));
        PropertiesCommand = new RelayCommand(() => Root.EditItem(this));
        RemoveCommand = new RelayCommand(() => Root.RemoveItem(this));
        ToggleMultiLaunchCommand = new RelayCommand(() => Root.ToggleMultiLaunch(this), () => IsShortcut);
        OpenLocationCommand = new RelayCommand(() => Root.RevealItem(this), () => IsShortcut);
        NewShortcutAfterCommand = new RelayCommand(() => Root.NewShortcutAfter(this));
        NewGroupAfterCommand = new RelayCommand(() => Root.NewGroupAfter(this), () => IsAtRoot);
    }

    public LauncherViewModel Root { get; }

    /// <summary>The live core object; edits go through <see cref="LauncherViewModel"/>.</summary>
    public LauncherItem Model { get; }

    public Guid Id => Model.Id;

    public bool IsGroup => Model is GroupItem;

    public bool IsShortcut => Model is ShortcutItem;

    public string Name => Model.Name;

    public string? Location => (Model as ShortcutItem)?.Location;

    public string? Parameters => (Model as ShortcutItem)?.Parameters;

    public bool IsMultiLaunch => Model is ShortcutItem { MultiLaunch: true };

    public string MultiLaunchMenuHeader => IsMultiLaunch ? "Remove from Multi Launch group" : "Add to Multi Launch group";

    public int ChildCount => (Model as GroupItem)?.Items.Count ?? 0;

    /// <summary>True when the item sits in the root list (so a group may be created next to it).</summary>
    public bool IsAtRoot
    {
        get
        {
            Root.Model.Find(Id, out var parent);
            return parent is null;
        }
    }

    public string ToolTipText
    {
        get
        {
            if (IsGroup)
            {
                return ChildCount == 1 ? $"{Name}\n1 item" : $"{Name}\n{ChildCount} items";
            }

            var location = string.IsNullOrEmpty(Parameters) ? Location : $"{Location} {Parameters}";
            var multi = IsMultiLaunch ? "\nMulti Launch group member" : string.Empty;
            return $"{Name}\n{location}{multi}";
        }
    }

    public ImageSource? Image
    {
        get => _image;
        private set => SetProperty(ref _image, value);
    }

    /// <summary>Set while something is being dragged over this group.</summary>
    public bool IsDropTarget
    {
        get => _isDropTarget;
        set => SetProperty(ref _isDropTarget, value);
    }

    /// <summary>The group's contents; <see langword="null"/> for shortcuts.</summary>
    public ItemListViewModel? Children =>
        Model is GroupItem group ? _children ??= new ItemListViewModel(Root, group) : null;

    public ICommand ActivateCommand { get; }

    public ICommand PropertiesCommand { get; }

    public ICommand RemoveCommand { get; }

    public ICommand ToggleMultiLaunchCommand { get; }

    public ICommand OpenLocationCommand { get; }

    public ICommand NewShortcutAfterCommand { get; }

    public ICommand NewGroupAfterCommand { get; }

    public void LoadImage() => Image = Root.Icons.GetItemIcon(Model, Root.IconSize);

    /// <summary>Re-reads every derived property after the model was edited.</summary>
    public void RefreshFromModel() => OnAllPropertiesChanged();
}
