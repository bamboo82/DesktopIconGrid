using System.IO;
using System.Reflection;
using System.Windows.Input;
using DesktopIconGrid.Core;
using DesktopIconGrid.Core.Diagnostics;
using DesktopIconGrid.Core.Launching;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Core.SpecialItems;
using DesktopIconGrid.Core.Storage;
using DesktopIconGrid.Infrastructure;
using DesktopIconGrid.Services;
using DesktopIconGrid.SpecialIcons;

namespace DesktopIconGrid.ViewModels;

/// <summary>Root view model: owns the model, persists every change and exposes bar-wide commands.</summary>
public sealed class LauncherViewModel : ObservableObject
{
    public const int LabelHeight = 16;
    private const int PopupMaxColumns = 8;
    private const int PopupMaxRows = 6;
    private static readonly TimeSpan LaunchDebounce = TimeSpan.FromMilliseconds(400);

    private readonly Dictionary<Guid, ItemViewModel> _viewModels = [];
    private readonly TaskScheduler _uiScheduler;
    private readonly LayoutStorage _storage;
    private readonly IDialogService _dialogs;
    private readonly ILaunchService _launcher;
    private readonly Action<Action> _defer;

    private ItemViewModel? _openGroup;
    private bool _isGroupPopupOpen;
    private Guid _lastLaunchedId;
    private DateTime _lastLaunchedAt;

    public LauncherViewModel(
        LauncherModel model,
        LayoutStorage storage,
        IIconProvider icons,
        IDialogService dialogs,
        ILaunchService launcher,
        ProcessTracker tracker,
        SpecialIconRegistry specialIcons,
        Action<Action>? defer = null)
    {
        Model = model;
        _storage = storage;
        Icons = icons;
        _dialogs = dialogs;
        _launcher = launcher;
        Tracker = tracker;
        SpecialIcons = specialIcons;
        _defer = defer ?? (action => action());
        _uiScheduler = SynchronizationContext.Current is null ? TaskScheduler.Default : TaskScheduler.FromCurrentSynchronizationContext();
        tracker.RunningChanged += (_, id) =>
        {
            if (_viewModels.TryGetValue(id, out var viewModel))
            {
                viewModel.NotifyRunningChanged();
            }
        };

        SpecialItemSections = SpecialItemCatalog.GetAvailableSections();
        RootItems = new ItemListViewModel(this, null);

        NewShortcutCommand = new RelayCommand(() => NewShortcut(RootItems, int.MaxValue));
        NewGroupCommand = new RelayCommand(() => NewGroup(RootItems, int.MaxValue));
        OpenSettingsCommand = new RelayCommand(OpenSettings);
        LaunchMultiLaunchCommand = new RelayCommand(() => _ = LaunchMultiLaunchGroupAsync());
        OpenLayoutFolderCommand = new RelayCommand(OpenLayoutFolder);
        AboutCommand = new RelayCommand(ShowAbout);
        ExitCommand = new RelayCommand(() => ExitRequested?.Invoke(this, EventArgs.Empty));
        CloseGroupPopupCommand = new RelayCommand(() => IsGroupPopupOpen = false);
        OpenSyncCommand = new RelayCommand(() => _dialogs.ShowSync());
        OpenNameTableCommand = new RelayCommand(() => _dialogs.ShowNameTable(this));
        OpenLogViewerCommand = new RelayCommand(() => _dialogs.ShowAppLog());
    }

    public LauncherModel Model { get; private set; }

    public IIconProvider Icons { get; }

    public ProcessTracker Tracker { get; }

    public SpecialIconRegistry SpecialIcons { get; }

    public ItemListViewModel RootItems { get; }

    public IReadOnlyList<SpecialItemSection> SpecialItemSections { get; }

    public string LayoutFilePath => _storage.FilePath;

    // ----- settings-derived appearance ----------------------------------------------------------

    public int IconSize => Model.Settings.IconSize;

    public int CellPadding => Model.Settings.CellPadding;

    public bool ShowLabels => Model.Settings.ShowLabels;

    public bool DoubleClickToLaunch => Model.Settings.DoubleClickToLaunch;

    /// <summary>Bar opacity in percent.</summary>
    public int Opacity => Model.Settings.Opacity;

    public bool CloseToTray => Model.Settings.CloseToTray;

    public string CloseButtonToolTip => CloseToTray ? "Hide to tray (Exit is in the tray and options menus)" : "Exit";

    /// <summary>Set by the host to flush pending background work (e.g. a debounced sync upload) before exiting.</summary>
    public Func<Task>? FlushBeforeExit { get; set; }

    public double CellWidth => IconSize + 2 * CellPadding + (ShowLabels ? 24 : 0);

    public double CellHeight => IconSize + 2 * CellPadding + (ShowLabels ? LabelHeight + 2 : 0);

    public bool AlwaysOnTop
    {
        get => Model.Settings.AlwaysOnTop;
        set
        {
            if (Model.Settings.AlwaysOnTop == value)
            {
                return;
            }

            Model.Settings.AlwaysOnTop = value;
            OnPropertyChanged();
            Save();
        }
    }

    /// <summary>The colours the bar is currently drawn with.</summary>
    public ThemePalette Palette => ThemeCatalog.Resolve(Model.Settings);

    public bool HasMultiLaunchMembers => Model.MultiLaunchMembers().Any();

    public WindowPlacement? WindowPlacement => Model.Window;

    // ----- group popup --------------------------------------------------------------------------

    public ItemViewModel? OpenGroup
    {
        get => _openGroup;
        private set
        {
            if (SetProperty(ref _openGroup, value))
            {
                OnPropertyChanged(nameof(PopupWidth));
                OnPropertyChanged(nameof(PopupMaxHeight));
            }
        }
    }

    public bool IsGroupPopupOpen
    {
        get => _isGroupPopupOpen;
        set
        {
            if (SetProperty(ref _isGroupPopupOpen, value) && !value)
            {
                OpenGroup = null;
            }
        }
    }

    public double PopupWidth => Math.Clamp(OpenGroup?.ChildCount ?? 1, 1, PopupMaxColumns) * CellWidth + 8;

    public double PopupMaxHeight => PopupMaxRows * CellHeight + 8;

    // ----- commands -----------------------------------------------------------------------------

    public ICommand NewShortcutCommand { get; }

    public ICommand NewGroupCommand { get; }

    public ICommand OpenSettingsCommand { get; }

    public ICommand LaunchMultiLaunchCommand { get; }

    public ICommand OpenLayoutFolderCommand { get; }

    public ICommand AboutCommand { get; }

    public ICommand ExitCommand { get; }

    public ICommand CloseGroupPopupCommand { get; }

    public ICommand OpenSyncCommand { get; }

    public ICommand OpenNameTableCommand { get; }

    public ICommand OpenLogViewerCommand { get; }

    /// <summary>Raised when a group icon is clicked; the view positions the popup at the anchor element.</summary>
    public event EventHandler<GroupActivatedEventArgs>? GroupActivated;

    public event EventHandler? ExitRequested;

    public event EventHandler? SettingsChanged;

    /// <summary>Raised after settings or items changed and were written to disk (window moves do not count).</summary>
    public event EventHandler? LayoutChanged;

    // ----- view-model plumbing ------------------------------------------------------------------

    /// <summary>
    /// Resolves an item's icon on the shared icon worker (shell calls can block on slow or missing drives) and applies
    /// it on the UI thread. A lookup that a newer request for the same item has superseded is dropped, so an edit
    /// followed by an icon-size change cannot end with the stale image.
    /// </summary>
    public void LoadImageAsync(ItemViewModel viewModel)
    {
        var request = viewModel.BeginImageRequest();
        if (!viewModel.NeedsImage)
        {
            viewModel.SetImage(null);
            return;
        }

        if (viewModel.Model is SpecialIconItem special && !viewModel.Model.HasCustomIcon)
        {
            viewModel.SetImage(viewModel.SpecialHandler.GetIcon(special));
            return;
        }

        var model = viewModel.Model;
        var size = IconSize;
        IconWorker.Shared.RunAsync(() => Icons.GetItemIcon(model, size))
            .ContinueWith(
                task =>
                {
                    if (viewModel.IsCurrentImageRequest(request))
                    {
                        viewModel.SetImage(task.IsCompletedSuccessfully ? task.Result : null);
                    }
                },
                CancellationToken.None,
                TaskContinuationOptions.None,
                _uiScheduler);
    }

    public ItemViewModel GetOrCreateViewModel(LauncherItem item)
    {
        if (!_viewModels.TryGetValue(item.Id, out var viewModel))
        {
            viewModel = new ItemViewModel(this, item);
            _viewModels[item.Id] = viewModel;
            _defer(viewModel.LoadImage);
            if (item is ShortcutItem shortcut)
            {
                Tracker.Watch(shortcut);
            }
        }

        return viewModel;
    }

    internal void NotifyListChanged(ItemListViewModel list)
    {
        if (list.Group is not null && _viewModels.TryGetValue(list.Group.Id, out var groupViewModel))
        {
            groupViewModel.RefreshFromModel();
            if (OpenGroup == groupViewModel)
            {
                OnPropertyChanged(nameof(PopupWidth));
            }
        }
    }

    // ----- actions ------------------------------------------------------------------------------

    public void Activate(ItemViewModel item, object? anchor) => Activate(item, anchor, ActivationModifiers.None);

    public void Activate(ItemViewModel item, object? anchor, ActivationModifiers modifiers)
    {
        if (item.IsGroup)
        {
            ToggleGroupPopup(item, anchor);
            return;
        }

        var now = DateTime.UtcNow;
        if (item.Id == _lastLaunchedId && now - _lastLaunchedAt < LaunchDebounce)
        {
            return; // a natural double-click in single-click mode
        }

        _lastLaunchedId = item.Id;
        _lastLaunchedAt = now;

        if (item.Model is UnknownItem)
        {
            _dialogs.ShowInfo(item.Name, "This item was made by a newer version of Desktop Icon Grid and cannot be opened here. It is kept so nothing is lost.");
            return;
        }

        if (item.Model is SpecialIconItem special)
        {
            var specialResult = item.SpecialHandler.Activate(special, modifiers);
            if (specialResult.Outcome == LaunchOutcome.Failed)
            {
                _dialogs.ShowError($"\"{item.Name}\"", specialResult.ErrorMessage ?? "Unknown error");
            }

            IsGroupPopupOpen = false;
            return;
        }

        if (item.Model is not ShortcutItem shortcutToLaunch)
        {
            return;
        }

        var result = _launcher.Launch(shortcutToLaunch);
        if (result.Outcome == LaunchOutcome.Failed)
        {
            _dialogs.ShowError($"Can't open \"{item.Name}\"", $"{result.ErrorMessage}\n\n{item.Location}");
            return;
        }

        IsGroupPopupOpen = false;
    }

    /// <summary>Opens files dropped onto a shortcut with that shortcut's program, or hands them to a special icon.</summary>
    public void OpenWith(ItemViewModel target, IReadOnlyList<string> paths)
    {
        if (target.Model is SpecialIconItem special)
        {
            _ = DropOnSpecialAsync(target, special, paths);
            return;
        }

        if (target.Model is not ShortcutItem shortcut || !DropArguments.SupportsOpenWith(shortcut))
        {
            return;
        }

        var result = _launcher.LaunchWith(shortcut, paths);
        if (result.Outcome == LaunchOutcome.Failed)
        {
            _dialogs.ShowError($"Can't open with \"{target.Name}\"", $"{result.ErrorMessage}\n\n{shortcut.Location}");
            return;
        }

        IsGroupPopupOpen = false;
    }

    private async Task DropOnSpecialAsync(ItemViewModel target, SpecialIconItem special, IReadOnlyList<string> paths)
    {
        var result = await target.SpecialHandler.DropFilesAsync(special, paths);
        if (result.Outcome == LaunchOutcome.Failed)
        {
            _dialogs.ShowError($"\"{target.Name}\"", result.ErrorMessage ?? "Unknown error");
        }
    }

    public void ToggleGroupPopup(ItemViewModel group, object? anchor)
    {
        if (IsGroupPopupOpen && OpenGroup == group)
        {
            IsGroupPopupOpen = false;
            return;
        }

        OpenGroup = group;
        GroupActivated?.Invoke(this, new GroupActivatedEventArgs(group, anchor));
        IsGroupPopupOpen = true;
    }

    public void NewShortcut(ItemListViewModel list, int index)
    {
        var draft = new ShortcutItem();
        if (!_dialogs.ShowItemProperties(draft, isNew: true))
        {
            return;
        }

        Model.Insert(draft, list.Group, index);
        list.Sync();
        Save();
    }

    public void NewShortcutAfter(ItemViewModel item)
    {
        var list = ListContaining(item, out var index);
        NewShortcut(list, index + 1);
    }

    public void NewGroup(ItemListViewModel list, int index)
    {
        if (!list.IsRoot)
        {
            list = RootItems;
            index = int.MaxValue;
        }

        var draft = new GroupItem { Name = "Group" };
        if (!_dialogs.ShowItemProperties(draft, isNew: true))
        {
            return;
        }

        Model.Insert(draft, null, index);
        list.Sync();
        Save();
    }

    public void NewGroupAfter(ItemViewModel item)
    {
        var list = ListContaining(item, out var index);
        NewGroup(list, index + 1);
    }

    public void AddSpecialItem(SpecialItemDefinition definition, ItemListViewModel list, int index)
    {
        Model.Insert(definition.CreateShortcut(), list.Group, index);
        list.Sync();
        Save();
    }

    public void AddSpecialItemAfter(SpecialItemDefinition definition, ItemViewModel item)
    {
        var list = ListContaining(item, out var index);
        AddSpecialItem(definition, list, index + 1);
    }

    /// <summary>Creates a special icon of the handler's kind after its properties dialog was confirmed.</summary>
    public void AddSpecialIcon(ISpecialIconHandler handler, ItemListViewModel list, int index)
    {
        var item = handler.CreateItem();
        if (!handler.ShowProperties(item, isNew: true))
        {
            return;
        }

        Model.Insert(item, list.Group, index);
        list.Sync();
        Save();
    }

    public void AddSpecialIconAfter(ISpecialIconHandler handler, ItemViewModel item)
    {
        var list = ListContaining(item, out var index);
        AddSpecialIcon(handler, list, index + 1);
    }

    /// <summary>Creates one shortcut per dropped path, inserting them in order at <paramref name="index"/>.</summary>
    public void AddPaths(IEnumerable<string> paths, ItemListViewModel list, int index)
    {
        var added = false;
        foreach (var path in paths.Where(p => !string.IsNullOrWhiteSpace(p)))
        {
            Model.Insert(ShortcutItem.FromPath(path), list.Group, index);
            index = index == int.MaxValue ? index : index + 1;
            added = true;
        }

        if (added)
        {
            list.Sync();
            Save();
            AppLog.Default.Info("Layout", $"Added dropped item(s) as shortcuts into \"{list.Group?.Name ?? "the bar"}\"");
        }
    }

    public void AddLink(string url, ItemListViewModel list, int index) => AddPaths([url], list, index);

    /// <summary>Reorders within a list or moves between the root and a group. Returns false when the move is not allowed.</summary>
    public bool MoveItem(Guid id, ItemListViewModel targetList, int insertBeforeIndex)
    {
        var item = Model.Find(id, out var sourceParent);
        if (item is null || (item is GroupItem && !targetList.IsRoot))
        {
            return false;
        }

        Model.Move(id, targetList.Group, insertBeforeIndex);
        ListFor(sourceParent).Sync();
        if (!ReferenceEquals(sourceParent, targetList.Group))
        {
            targetList.Sync();
        }

        Save();
        return true;
    }

    public bool MoveIntoGroup(Guid id, ItemViewModel group)
    {
        var item = Model.Find(id, out var sourceParent);
        if (item is null || item is GroupItem || group.Model is not GroupItem groupModel || id == group.Id)
        {
            return false;
        }

        Model.MoveIntoGroup(id, groupModel);
        ListFor(sourceParent).Sync();
        group.Children?.Sync();
        Save();
        return true;
    }

    public void EditItem(ItemViewModel item)
    {
        if (item.Model is UnknownItem)
        {
            _dialogs.ShowInfo(item.Name, "This item was made by a newer version of Desktop Icon Grid and cannot be edited here.");
            return;
        }

        var draft = item.Model.Clone();
        var saved = draft is SpecialIconItem specialDraft
            ? item.SpecialHandler.ShowProperties(specialDraft, isNew: false)
            : _dialogs.ShowItemProperties(draft, isNew: false);
        if (!saved)
        {
            return;
        }

        Model.Update(draft);
        item.RefreshFromModel();
        if (item.IsSpecial)
        {
            item.RebuildGauge();
        }

        item.LoadImage();
        if (item.Model is ShortcutItem edited)
        {
            Tracker.Watch(edited);
        }

        OnPropertyChanged(nameof(HasMultiLaunchMembers));
        Save();
    }

    /// <summary>Opens the live log of the shortcut's latest captured run.</summary>
    public void ShowLog(ItemViewModel item)
    {
        if (!item.IsShortcut)
        {
            return;
        }

        _dialogs.ShowLog(new LogViewerViewModel(item.Name, Tracker.LatestLog(item.Id), Tracker.LogDirectory));
    }

    public void RemoveItem(ItemViewModel item)
    {
        var message = item.IsGroup
            ? item.ChildCount == 0
                ? $"Remove the group \"{item.Name}\"?"
                : $"Remove the group \"{item.Name}\" and the {item.ChildCount} shortcut(s) in it?"
            : $"Remove \"{item.Name}\" from the bar?\n\nThe program or file itself is not affected.";

        if (!_dialogs.Confirm("Remove", message))
        {
            return;
        }

        Model.Find(item.Id, out var parent);
        if (!Model.Remove(item.Id))
        {
            return;
        }

        AppLog.Default.Info("Layout", $"Removed \"{item.Name}\"");

        if (item.Model is GroupItem group)
        {
            foreach (var child in group.Items)
            {
                if (_viewModels.Remove(child.Id, out var childViewModel))
                {
                    childViewModel.Detach();
                }

                Tracker.Unwatch(child.Id);
            }

            if (OpenGroup == item)
            {
                IsGroupPopupOpen = false;
            }
        }

        item.Detach();
        _viewModels.Remove(item.Id);
        Tracker.Unwatch(item.Id);
        ListFor(parent).Sync();
        OnPropertyChanged(nameof(HasMultiLaunchMembers));
        Save();
    }

    public void ToggleMultiLaunch(ItemViewModel item)
    {
        if (item.Model is not ShortcutItem shortcut)
        {
            return;
        }

        shortcut.MultiLaunch = !shortcut.MultiLaunch;
        item.RefreshFromModel();
        OnPropertyChanged(nameof(HasMultiLaunchMembers));
        Save();
    }

    public void RevealItem(ItemViewModel item)
    {
        if (item.Location is null)
        {
            return;
        }

        var result = ShellHelper.RevealInExplorer(item.Location);
        if (result.Outcome == LaunchOutcome.Failed)
        {
            _dialogs.ShowError("Can't open the location", $"{result.ErrorMessage}\n\n{item.Location}");
        }
    }

    public async Task LaunchMultiLaunchGroupAsync()
    {
        var members = Model.MultiLaunchMembers().ToList();
        if (members.Count == 0)
        {
            _dialogs.ShowInfo(
                "Multi Launch group is empty",
                "Right-click a shortcut and choose \"Add to Multi Launch group\". Every member starts together when you launch the group.");
            return;
        }

        IsGroupPopupOpen = false;
        var failures = new List<string>();
        foreach (var member in members)
        {
            var result = _launcher.Launch(member);
            if (result.Outcome == LaunchOutcome.Failed)
            {
                failures.Add($"{member.Name}: {result.ErrorMessage}");
            }

            await Task.Delay(300).ConfigureAwait(true);
        }

        if (failures.Count > 0)
        {
            _dialogs.ShowError("Some shortcuts did not start", string.Join("\n", failures));
        }
    }

    public void OpenSettings()
    {
        var edited = _dialogs.ShowSettings(Model.Settings.Clone());
        if (edited is not null)
        {
            ApplySettings(edited);
        }
    }

    public void ApplySettings(LauncherSettings settings)
    {
        var previousIconSize = IconSize;
        Model.ApplySettings(settings);
        NotifyAppearanceChanged();
        AppLog.Default.Info("Settings", $"Settings applied: theme {Model.Settings.Theme}, icons {IconSize} px, padding {CellPadding}, labels {(ShowLabels ? "on" : "off")}, {(DoubleClickToLaunch ? "double" : "single")} click, opacity {Opacity}%, hotkey {Model.Settings.ToggleHotkey ?? "none"}");

        if (IconSize != previousIconSize)
        {
            foreach (var viewModel in _viewModels.Values)
            {
                _defer(viewModel.LoadImage);
            }
        }

        Save();
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void UpdateWindowPlacement(WindowPlacement placement)
    {
        Model.Window = placement;
        Save(contentChanged: false);
    }

    public void SaveNow(bool contentChanged = false) => Save(contentChanged);

    /// <summary>Changes an item's caption. Returns false when nothing changed.</summary>
    public bool RenameItem(Guid id, string name, bool save = true)
    {
        if (!Model.Rename(id, name))
        {
            return false;
        }

        if (_viewModels.TryGetValue(id, out var viewModel))
        {
            viewModel.RefreshFromModel();
        }

        if (save)
        {
            Save();
        }

        return true;
    }

    /// <summary>A copy of the current settings, items and window placement.</summary>
    public LauncherLayout ExportLayout() => Model.ToLayout();

    /// <summary>
    /// Replaces the settings and items with those of <paramref name="layout"/> (for example a copy downloaded
    /// from Google Drive), keeping this device's window placement.
    /// </summary>
    public void ReplaceLayout(LauncherLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        var model = LauncherModel.FromLayout(layout);
        model.Window = Model.Window;
        AppLog.Default.Info("Layout", $"Replaced the layout with a loaded copy ({model.Items.Count} top-level items)");

        IsGroupPopupOpen = false;
        Model = model;
        foreach (var viewModel in _viewModels.Values)
        {
            viewModel.Detach();
        }

        _viewModels.Clear();
        Tracker.UnwatchAll();

        RootItems.Sync();
        NotifyAppearanceChanged();
        Save(contentChanged: false);
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OpenLayoutFolder()
    {
        var result = Directory.Exists(_storage.Directory)
            ? ShellHelper.RevealInExplorer(_storage.FilePath)
            : LaunchResult.Failure("The layout file has not been created yet.");
        if (result.Outcome == LaunchOutcome.Failed)
        {
            _dialogs.ShowInfo("Layout file", $"{result.ErrorMessage}\n\n{_storage.FilePath}");
        }
    }

    private void ShowAbout()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "dev";
        _dialogs.ShowInfo(
            "About Desktop Icon Grid",
            $"Desktop Icon Grid {version}\n\n" +
            "A compact launcher bar: click an icon to open it, drag icons to rearrange them, " +
            "right-click for shortcuts, groups and special items.\n\n" +
            $"Layout file:\n{_storage.FilePath}");
    }

    private ItemListViewModel ListContaining(ItemViewModel item, out int index)
    {
        Model.Find(item.Id, out var parent);
        var list = ListFor(parent);
        index = list.Items.IndexOf(item);
        if (index < 0)
        {
            index = list.Items.Count - 1;
        }

        return list;
    }

    private ItemListViewModel ListFor(GroupItem? parent) =>
        parent is null ? RootItems : GetOrCreateViewModel(parent).Children!;

    private void NotifyAppearanceChanged()
    {
        OnPropertyChanged(nameof(IconSize));
        OnPropertyChanged(nameof(CellPadding));
        OnPropertyChanged(nameof(ShowLabels));
        OnPropertyChanged(nameof(DoubleClickToLaunch));
        OnPropertyChanged(nameof(CellWidth));
        OnPropertyChanged(nameof(CellHeight));
        OnPropertyChanged(nameof(AlwaysOnTop));
        OnPropertyChanged(nameof(Opacity));
        OnPropertyChanged(nameof(Palette));
        OnPropertyChanged(nameof(PopupWidth));
        OnPropertyChanged(nameof(PopupMaxHeight));
        OnPropertyChanged(nameof(HasMultiLaunchMembers));
    }

    private void Save(bool contentChanged = true)
    {
        try
        {
            _storage.Save(Model.ToLayout());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _dialogs.ShowError("Couldn't save the layout", $"{ex.Message}\n\n{_storage.FilePath}");
        }

        if (contentChanged)
        {
            LayoutChanged?.Invoke(this, EventArgs.Empty);
        }
    }

}

public sealed class GroupActivatedEventArgs(ItemViewModel group, object? anchor) : EventArgs
{
    public ItemViewModel Group { get; } = group;

    /// <summary>The element that was clicked, used to place the popup.</summary>
    public object? Anchor { get; } = anchor;
}
