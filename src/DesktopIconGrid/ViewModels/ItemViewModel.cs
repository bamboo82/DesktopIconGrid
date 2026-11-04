using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using DesktopIconGrid.Core.Launching;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Infrastructure;
using DesktopIconGrid.Services;
using DesktopIconGrid.SpecialIcons;

namespace DesktopIconGrid.ViewModels;

/// <summary>One icon on the bar or inside a group popup.</summary>
public sealed class ItemViewModel : ObservableObject
{
    private ImageSource? _image;
    private bool _isDropTarget;
    private ItemListViewModel? _children;
    private GaugeState? _gauge;

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
        ShowLogCommand = new RelayCommand(() => Root.ShowLog(this), () => CapturesLog);
        RebuildGauge();
    }

    public LauncherViewModel Root { get; }

    /// <summary>The live core object; edits go through <see cref="LauncherViewModel"/>.</summary>
    public LauncherItem Model { get; }

    public Guid Id => Model.Id;

    public bool IsGroup => Model is GroupItem;

    public bool IsShortcut => Model is ShortcutItem;

    public bool IsSpecial => Model is SpecialIconItem;

    /// <summary>The handler for a special icon; the placeholder handler for other kinds of item.</summary>
    public ISpecialIconHandler SpecialHandler => Root.SpecialIcons.Resolve((Model as SpecialIconItem)?.Kind);

    /// <summary>Live gauge of a monitor icon; <see langword="null"/> for other items.</summary>
    public GaugeState? Gauge => _gauge;

    public bool ShowsGauge => _gauge is not null && !Model.HasCustomIcon && Model.TextIcon is null;

    public string Name => Model.Name;

    public string? Location => (Model as ShortcutItem)?.Location;

    public string? Parameters => (Model as ShortcutItem)?.Parameters;

    public bool IsMultiLaunch => Model is ShortcutItem { MultiLaunch: true };

    public string MultiLaunchMenuHeader => IsMultiLaunch ? "Remove from Multi Launch group" : "Add to Multi Launch group";

    /// <summary>True while a program started from this shortcut (or its executable) is running.</summary>
    public bool IsRunning => IsShortcut && Root.Tracker.IsRunning(Id);

    public bool CapturesLog => Model is ShortcutItem { CaptureLog: true };

    public int ChildCount => (Model as GroupItem)?.Items.Count ?? 0;

    // ----- text icons -----------------------------------------------------------------------------

    /// <summary>True when the icon is drawn as text; an explicit icon image takes precedence.</summary>
    public bool HasTextIcon => !Model.HasCustomIcon && Model.TextIcon is not null;

    public bool ShowsImage => !HasTextIcon && !ShowsGauge;

    public string TextIconText => Model.TextIcon?.Text ?? string.Empty;

    public FontFamily TextIconFontFamily => TextIconFonts.Resolve(Model.TextIcon?.FontFamily);

    public double TextIconFontSize => Model.TextIcon?.FontSize ?? TextIcon.DefaultFontSize;

    public FontWeight TextIconFontWeight => Model.TextIcon?.Bold == true ? FontWeights.Bold : FontWeights.Normal;

    public bool HasTextIconColor => Model.TextIcon?.Color is not null;

    public Brush? TextIconBrush => Model.TextIcon?.Color is { } color ? ThemeManager.ToBrush(color) : null;

    /// <summary>True when files dropped on this icon should be opened with its program.</summary>
    public bool AcceptsDroppedFiles => Model is ShortcutItem shortcut && DropArguments.SupportsOpenWith(shortcut);

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

            if (IsSpecial)
            {
                return $"{Name}\n{_gauge?.Detail ?? SpecialHandler.Description}";
            }

            var location = string.IsNullOrEmpty(Parameters) ? Location : $"{Location} {Parameters}";
            var multi = IsMultiLaunch ? "\nMulti Launch group member" : string.Empty;
            var running = IsRunning ? "\nRunning" : string.Empty;
            return $"{Name}\n{location}{multi}{running}";
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

    public ICommand ShowLogCommand { get; }

    /// <summary>Called by the root when the tracker reports a change for this item.</summary>
    public void NotifyRunningChanged()
    {
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(ToolTipText));
    }

    public void LoadImage() => Image = HasTextIcon || ShowsGauge ? null : Root.Icons.GetItemIcon(Model, Root.IconSize);

    /// <summary>Creates (or recreates after editing) the live state of a special icon.</summary>
    public void RebuildGauge()
    {
        _gauge?.Detach();
        _gauge = Model is SpecialIconItem special ? SpecialHandler.CreateState(special) : null;
        if (_gauge is not null)
        {
            _gauge.PropertyChanged += (_, _) => OnPropertyChanged(nameof(ToolTipText));
        }

        OnPropertyChanged(nameof(Gauge));
        OnPropertyChanged(nameof(ShowsGauge));
        OnPropertyChanged(nameof(ShowsImage));
    }

    /// <summary>Stops live updates; called when the item leaves the bar.</summary>
    public void Detach() => _gauge?.Detach();

    /// <summary>Re-reads every derived property after the model was edited.</summary>
    public void RefreshFromModel() => OnAllPropertiesChanged();
}
