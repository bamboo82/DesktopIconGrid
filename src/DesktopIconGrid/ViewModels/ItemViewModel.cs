using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using DesktopIconGrid.Core.Launching;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Infrastructure;
using DesktopIconGrid.Resources;
using DesktopIconGrid.Services;
using DesktopIconGrid.SpecialIcons;

namespace DesktopIconGrid.ViewModels;

/// <summary>One icon on the bar or inside a group popup.</summary>
public sealed class ItemViewModel : ObservableObject
{
    private ImageSource? _image;
    private int _imageRequest;
    private bool _isDropTarget;
    private ItemListViewModel? _children;
    private GaugeState? _gauge;
    private bool _isSearchDimmed;
    private bool _isSearchSelected;
    private bool _isLaunching;

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

    // ----- separators and colour tags ---------------------------------------------------------

    public bool IsSeparator => Model is SeparatorItem;

    /// <summary>The kind of separator this version draws (<see cref="SeparatorItem.EffectiveKind"/>); empty for other items.</summary>
    public string SeparatorKind => (Model as SeparatorItem)?.EffectiveKind ?? string.Empty;

    public bool IsLineSeparator => SeparatorKind == SeparatorItem.Line;

    public bool IsGapSeparator => SeparatorKind == SeparatorItem.Gap;

    public bool IsLabelSeparator => SeparatorKind == SeparatorItem.Label;

    /// <summary>How much room the item takes in the grid: a divider is thin and a caption as wide as its text.</summary>
    public FlowSlot Slot => SeparatorKind switch
    {
        SeparatorItem.Line => FlowSlot.Narrow,
        SeparatorItem.Label => FlowSlot.Auto,
        _ => FlowSlot.Cell,
    };

    /// <summary>What a screen reader says for a separator: its caption, or what kind it is.</summary>
    public string SeparatorName => SeparatorKind switch
    {
        SeparatorItem.Line => Strings.Separator_LineName,
        SeparatorItem.Gap => Strings.Separator_GapName,
        _ => Name,
    };

    /// <summary>A known colour tag is drawn as a corner on the icon.</summary>
    public bool HasColorTag => ColorTags.Find(Model.ColorTag) is not null;

    public Brush? ColorTagBrush => ColorTags.Find(Model.ColorTag) is { } tag ? ThemeManager.ToBrush(tag.Color) : null;

    /// <summary>The tag's name in the UI language; empty without a (known) tag.</summary>
    public string ColorTagName => ColorTagNames.Of(Model.ColorTag);

    /// <summary>Set for a few seconds after the item was started from the bar, while the program comes up: the icon pulses.</summary>
    public bool IsLaunching
    {
        get => _isLaunching;
        internal set => SetProperty(ref _isLaunching, value);
    }

    /// <summary>An item from a newer version: kept, shown as a placeholder, not openable or editable.</summary>
    public bool IsUnknown => Model is UnknownItem;

    /// <summary>The handler for a special icon; the placeholder handler for other kinds of item.</summary>
    public ISpecialIconHandler SpecialHandler => Root.SpecialIcons.Resolve((Model as SpecialIconItem)?.Kind);

    /// <summary>Live gauge of a monitor icon; <see langword="null"/> for other items.</summary>
    public GaugeState? Gauge => _gauge;

    public bool ShowsGauge => _gauge is { IsVisible: true } && !Model.HasCustomIcon && Model.TextIcon is null;

    public string Name => Model.Name;

    /// <summary>A search is under way and this item neither matches nor holds a match: drawn faded.</summary>
    public bool IsSearchDimmed
    {
        get => _isSearchDimmed;
        internal set => SetProperty(ref _isSearchDimmed, value);
    }

    /// <summary>The match Enter would open: drawn with a frame.</summary>
    public bool IsSearchSelected
    {
        get => _isSearchSelected;
        internal set => SetProperty(ref _isSearchSelected, value);
    }

    public string? Location => (Model as ShortcutItem)?.Location;

    public string? Parameters => (Model as ShortcutItem)?.Parameters;

    public bool IsMultiLaunch => Model is ShortcutItem { MultiLaunch: true };

    public string MultiLaunchMenuHeader => IsMultiLaunch ? Strings.Menu_RemoveFromMultiLaunch : Strings.Menu_AddToMultiLaunch;

    /// <summary>True while a program started from this shortcut (or its executable) is running.</summary>
    public bool IsRunning => IsShortcut && Root.Tracker.IsRunning(Id);

    public bool CapturesLog => Model is ShortcutItem { CaptureLog: true };

    /// <summary>The group's shortcuts and special icons (its separators are not counted).</summary>
    public int ChildCount => (Model as GroupItem)?.Items.Count(i => i is not SeparatorItem) ?? 0;

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

    /// <summary>True when files dropped on this icon are handled by it: opened with a shortcut's program, or taken by a special icon.</summary>
    public bool AcceptsDroppedFiles => Model switch
    {
        ShortcutItem shortcut => DropArguments.SupportsOpenWith(shortcut),
        SpecialIconItem special => SpecialHandler.AcceptsFileDrops(special),
        _ => false,
    };

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
                return ChildCount == 1 ? string.Format(Strings.Item_OneItemToolTip, Name) : string.Format(Strings.Item_ItemsToolTip, Name, ChildCount);
            }

            if (Model is SpecialIconItem special)
            {
                var detail = _gauge is { IsVisible: true } && _gauge.Detail.Length > 0
                    ? _gauge.Detail
                    : SpecialHandler.GetToolTip(special) ?? SpecialHandler.Description;
                return $"{Name}\n{detail}";
            }

            if (IsUnknown)
            {
                return string.Format(Strings.Item_NewerVersionToolTip, Name);
            }

            var location = string.IsNullOrEmpty(Parameters) ? Location : $"{Location} {Parameters}";
            var multi = IsMultiLaunch ? "\n" + Strings.Item_MultiLaunchMember : string.Empty;
            var running = IsRunning ? "\n" + Strings.Grid_Running : string.Empty;
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

    /// <summary>False when the icon is drawn as text or a gauge and no image is needed.</summary>
    public bool NeedsImage => !HasTextIcon && !ShowsGauge;

    public void LoadImage() => Root.LoadImageAsync(this);

    internal void SetImage(ImageSource? image) => Image = image;

    /// <summary>Starts a new icon lookup; the results of earlier lookups for this item are then ignored.</summary>
    internal int BeginImageRequest() => ++_imageRequest;

    internal bool IsCurrentImageRequest(int request) => request == _imageRequest;

    /// <summary>Creates (or recreates after editing) the live state of a special icon.</summary>
    public void RebuildGauge()
    {
        _gauge?.Detach();
        _gauge = Model is SpecialIconItem special ? SpecialHandler.CreateState(special) : null;
        if (_gauge is not null)
        {
            _gauge.PropertyChanged += (_, e) =>
            {
                OnPropertyChanged(nameof(ToolTipText));
                if (e.PropertyName is null or "" or nameof(GaugeState.IsVisible))
                {
                    OnPropertyChanged(nameof(ShowsGauge));
                    OnPropertyChanged(nameof(ShowsImage));
                }
            };
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
