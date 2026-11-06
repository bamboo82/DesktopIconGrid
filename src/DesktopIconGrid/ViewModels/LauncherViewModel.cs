using System.IO;
using System.Reflection;
using System.Windows.Input;
using DesktopIconGrid.Core;
using DesktopIconGrid.Core.Diagnostics;
using DesktopIconGrid.Core.Imports;
using DesktopIconGrid.Core.Launching;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Core.SpecialItems;
using DesktopIconGrid.Core.Storage;
using DesktopIconGrid.Infrastructure;
using DesktopIconGrid.Resources;
using DesktopIconGrid.Services;
using DesktopIconGrid.SpecialIcons;

namespace DesktopIconGrid.ViewModels;

/// <summary>Root view model: owns the model, persists every change and exposes bar-wide commands.</summary>
public sealed class LauncherViewModel : ObservableObject, ISyncHost
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
    private readonly IWindowSwitcher? _windows;

    /// <summary>Whether a shortcut's target is missing on this PC (<see cref="LocationProbe"/>; tests give their own answer).</summary>
    private readonly Func<ShortcutItem, bool> _isMissing;
    private string _searchText = string.Empty;
    private ItemViewModel? _launching;
    private int _launchFeedbackVersion;
    private List<ItemViewModel> _searchResults = [];
    private int _searchIndex;
    private PendingRemoval? _pendingRemoval;
    private int _removalVersion;

    public LauncherViewModel(
        LauncherModel model,
        LayoutStorage storage,
        IIconProvider icons,
        IDialogService dialogs,
        ILaunchService launcher,
        ProcessTracker tracker,
        SpecialIconRegistry specialIcons,
        Action<Action>? defer = null,
        IWindowSwitcher? windows = null,
        Func<ShortcutItem, bool>? isMissing = null)
    {
        _isMissing = isMissing ?? (shortcut => LocationProbe.IsMissing(shortcut, IconWorker.IsOnSlowStorage));
        Model = model;
        _storage = storage;
        Icons = icons;
        _dialogs = dialogs;
        _launcher = launcher;
        Tracker = tracker;
        SpecialIcons = specialIcons;
        _defer = defer ?? (action => action());
        _windows = windows;
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
        UndoRemoveCommand = new RelayCommand(UndoRemove);
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

    public string CloseButtonToolTip => CloseToTray ? Strings.Main_CloseToTrayToolTip : Strings.Common_Exit;

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

    /// <summary>Room for the group's entries side by side, up to <c>PopupMaxColumns</c>; a separator counts as a cell (a line or caption leaves some to spare).</summary>
    public double PopupWidth => Math.Clamp((OpenGroup?.Model as GroupItem)?.Items.Count ?? 1, 1, PopupMaxColumns) * CellWidth + 8;

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
        if (viewModel.Model is ShortcutItem shortcut)
        {
            CheckMissing(viewModel, shortcut);
        }

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
        var slow = IconWorker.IsOnSlowStorage(model.IconPath) || IconWorker.IsOnSlowStorage((model as ShortcutItem)?.EffectiveLocation);
        IconWorker.Shared.RunAsync(() => Icons.GetItemIcon(model, size), IconWorker.LookupTimeout, model.Name, slow)
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

    /// <summary>
    /// Whether <see cref="ShortcutItem.StartIn"/> is just the folder of the program (as a Windows shortcut sets it), the
    /// shared one or this PC's own: then it says nothing the program's own folder does not, and moves with it.
    /// </summary>
    private static bool StartsInProgramFolder(ShortcutItem shortcut)
    {
        if (string.IsNullOrWhiteSpace(shortcut.StartIn))
        {
            return false;
        }

        var startIn = Environment.ExpandEnvironmentVariables(shortcut.StartIn.Trim().Trim('"')).TrimEnd('\\', '/');
        return new[] { shortcut.Location, shortcut.EffectiveLocation }
            .Select(location => Path.GetDirectoryName(Environment.ExpandEnvironmentVariables(location?.Trim().Trim('"') ?? string.Empty)))
            .Any(folder => !string.IsNullOrEmpty(folder) && string.Equals(folder.TrimEnd('\\', '/'), startIn, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>What the bar already knows of an item's target (<see cref="ItemViewModel.IsMissing"/>), when it shows the item.</summary>
    internal bool? KnownMissing(Guid id) => _viewModels.TryGetValue(id, out var viewModel) ? viewModel.IsMissing : null;

    /// <summary>The bar's own missing check (see <see cref="CheckMissing"/>), on the icon worker, for a shortcut it may not show yet.</summary>
    internal Task<bool> IsMissingAsync(ShortcutItem shortcut) =>
        IconWorker.Shared.RunAsync(() => _isMissing(shortcut))
            .ContinueWith(task => task.IsCompletedSuccessfully && task.Result, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);

    /// <summary>
    /// Checks on the icon worker whether the shortcut's target is on this PC (<see cref="LocationProbe"/>: local paths
    /// only, so a network or removable drive never stalls the bar).
    /// </summary>
    private void CheckMissing(ItemViewModel viewModel, ShortcutItem shortcut)
    {
        IconWorker.Shared.RunAsync(() => _isMissing(shortcut))
            .ContinueWith(
                task => viewModel.IsMissing = task.IsCompletedSuccessfully && task.Result,
                CancellationToken.None,
                TaskContinuationOptions.None,
                _uiScheduler);
    }

    /// <summary>
    /// Resolves the icons of the Add Windows item menu on the icon worker ahead of time, one at a time after the
    /// bar's own icons, so opening the menu for the first time does not wait for twenty shell lookups.
    /// </summary>
    public async Task WarmCatalogIconsAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(5));
        foreach (var definition in SpecialItemSections.SelectMany(s => s.Items))
        {
            var source = definition.IconPath ?? definition.Location;
            try
            {
                await IconWorker.Shared.RunAsync(() => Icons.GetIcon(source, 16), IconWorker.LookupTimeout, source);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                AppLog.Default.Debug("Icons", $"Catalog icon for {source} could not be resolved ahead of time: {ex.Message}");
            }
        }
    }

    public ItemViewModel GetOrCreateViewModel(LauncherItem item)
    {
        if (!_viewModels.TryGetValue(item.Id, out var viewModel))
        {
            viewModel = new ItemViewModel(this, item) { IsSearchDimmed = IsSearching }; // a group opened while searching
            _viewModels[item.Id] = viewModel;
            if (item is not SeparatorItem)
            {
                _defer(viewModel.LoadImage);
            }

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

    // ----- type to find -------------------------------------------------------------------------

    /// <summary>
    /// What the user typed into the bar's search box. Shortcuts and special icons anywhere (inside groups too) that
    /// match stay lit, as do the groups that hold them; everything else fades (see <see cref="ItemSearch"/>).
    /// </summary>
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value ?? string.Empty))
            {
                UpdateSearch();
            }
        }
    }

    public bool IsSearching => _searchText.Trim().Length > 0;

    /// <summary>The matches, best first.</summary>
    public IReadOnlyList<ItemViewModel> SearchResults => _searchResults;

    /// <summary>The match Enter opens (the first one until the arrows move it).</summary>
    public ItemViewModel? SelectedSearchResult => _searchIndex < _searchResults.Count ? _searchResults[_searchIndex] : null;

    /// <summary>Raised after a shortcut or special icon was opened (or its program brought forward), from a click, a key or a search.</summary>
    public event EventHandler<ItemViewModel>? ItemActivated;

    public void MoveSearchSelection(int delta)
    {
        if (_searchResults.Count == 0)
        {
            return;
        }

        _searchIndex = ((_searchIndex + delta) % _searchResults.Count + _searchResults.Count) % _searchResults.Count;
        ApplySearchStates();
    }

    /// <summary>Opens the selected match and ends the search. False when nothing matches.</summary>
    public bool LaunchSearchSelection(ActivationModifiers modifiers = ActivationModifiers.None)
    {
        if (SelectedSearchResult is not { } selected)
        {
            return false;
        }

        SearchText = string.Empty;
        Activate(selected, anchor: null, modifiers);
        return true;
    }

    public void ClearSearch() => SearchText = string.Empty;

    private void UpdateSearch()
    {
        var candidates = new List<SearchCandidate>();
        foreach (var item in Model.Items)
        {
            foreach (var entry in item is GroupItem group ? group.Items : [item])
            {
                if (entry is ShortcutItem or SpecialIconItem)
                {
                    candidates.Add(CandidateOf(entry));
                }
            }
        }

        _searchResults = ItemSearch.Rank(_searchText, candidates)
            .Select(id => Model.Find(id))
            .OfType<LauncherItem>()
            .Select(GetOrCreateViewModel)
            .ToList();
        _searchIndex = 0;
        ApplySearchStates();
        OnPropertyChanged(nameof(IsSearching));
        OnPropertyChanged(nameof(SearchResults));
    }

    private SearchCandidate CandidateOf(LauncherItem item)
    {
        if (item is SpecialIconItem special)
        {
            return new SearchCandidate(item.Id, item.Name, SpecialIcons.Resolve(special.Kind).DisplayName);
        }

        var location = (item as ShortcutItem)?.EffectiveLocation ?? string.Empty;
        var trimmed = location.Trim().TrimEnd('\\', '/');
        var cut = trimmed.LastIndexOfAny(['\\', '/']);
        var fileName = Path.GetFileNameWithoutExtension(cut >= 0 ? trimmed[(cut + 1)..] : trimmed);
        return new SearchCandidate(item.Id, item.Name, ShortcutItem.SuggestName(location), fileName);
    }

    /// <summary>
    /// Lights the matches and the groups that hold them, fades the rest, and frames the selected match together with
    /// its group, so the frame can be seen while the group is closed.
    /// </summary>
    private void ApplySearchStates()
    {
        var searching = IsSearching;
        var selected = SelectedSearchResult;
        var lit = new HashSet<Guid>(_searchResults.Select(r => r.Id));
        var framed = new HashSet<Guid>();
        if (selected is not null)
        {
            framed.Add(selected.Id);
        }

        foreach (var group in Model.Items.OfType<GroupItem>())
        {
            if (group.Items.Any(child => lit.Contains(child.Id)))
            {
                lit.Add(group.Id);
            }

            if (selected is not null && group.Items.Any(child => child.Id == selected.Id))
            {
                framed.Add(group.Id);
            }
        }

        foreach (var viewModel in _viewModels.Values)
        {
            viewModel.IsSearchDimmed = searching && !lit.Contains(viewModel.Id);
            viewModel.IsSearchSelected = searching && framed.Contains(viewModel.Id);
        }

        OnPropertyChanged(nameof(SelectedSearchResult));
    }

    // ----- actions ------------------------------------------------------------------------------

    public void Activate(ItemViewModel item, object? anchor) => Activate(item, anchor, ActivationModifiers.None);

    public void Activate(ItemViewModel item, object? anchor, ActivationModifiers modifiers)
    {
        if (item.IsSeparator)
        {
            return; // a divider or caption does nothing when clicked
        }

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
            _dialogs.Notify(item.Name, Strings.Launch_NewerVersionOpen);
            return;
        }

        if (item.Model is SpecialIconItem special)
        {
            var specialResult = item.SpecialHandler.Activate(special, modifiers);
            if (specialResult.Outcome == LaunchOutcome.Failed)
            {
                _dialogs.Notify(item.Name, specialResult.ErrorMessage ?? Strings.Common_UnknownError);
            }

            IsGroupPopupOpen = false;
            ItemActivated?.Invoke(this, item);
            return;
        }

        if (item.Model is not ShortcutItem shortcutToLaunch)
        {
            return;
        }

        if (item.IsMissing)
        {
            // Checked again now (a fixed local drive: quick): it may have come back since.
            item.IsMissing = _isMissing(shortcutToLaunch);
            if (item.IsMissing)
            {
                RepairItem(item);
                return;
            }
        }

        // The taskbar's keys: Shift or a middle click starts another instance, Ctrl+Shift starts one as administrator.
        var asAdministrator = modifiers.HasFlag(ActivationModifiers.Control) && modifiers.HasFlag(ActivationModifiers.Shift);
        var newInstance = asAdministrator || modifiers.HasFlag(ActivationModifiers.Shift) || modifiers.HasFlag(ActivationModifiers.NewInstance);
        if (!newInstance
            && _windows is not null
            && LaunchPlanner.SwitchesWhenRunning(shortcutToLaunch)
            && (string.IsNullOrWhiteSpace(shortcutToLaunch.Parameters)
                ? _windows.TrySwitch(Tracker.ProcessIdsOf(shortcutToLaunch.Id), Tracker.PackageFamilyOf(shortcutToLaunch.Id))
                : _windows.TrySwitch(Tracker.LaunchedProcessIdsOf(shortcutToLaunch.Id)))) // with parameters: only the copies it started
        {
            IsGroupPopupOpen = false;
            ItemActivated?.Invoke(this, item);
            return;
        }

        var result = _launcher.Launch(shortcutToLaunch, asAdministrator);
        if (result.Outcome == LaunchOutcome.Failed)
        {
            _dialogs.Notify(string.Format(Strings.Launch_CantOpen, item.Name), $"{result.ErrorMessage}\n{item.Location}");
            return;
        }

        if (result.Outcome == LaunchOutcome.Started)
        {
            StartLaunchFeedback(item);
        }

        IsGroupPopupOpen = false;
        ItemActivated?.Invoke(this, item);
    }

    // ----- launch feedback ----------------------------------------------------------------------

    /// <summary>How long an icon pulses after a launch at most; shortened by tests.</summary>
    internal static TimeSpan LaunchFeedbackDuration { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Completes when the current pulse has run out (at once when there is none); tests await it.</summary>
    internal Task LaunchFeedbackExpiry { get; private set; } = Task.CompletedTask;

    /// <summary>The icon that is pulsing: the one started, or the group it sits in (the popup closes on a launch).</summary>
    internal ItemViewModel? LaunchingItem => _launching;

    /// <summary>
    /// Makes the started item's icon pulse while the program comes up. It stops when the bar loses the foreground
    /// (<see cref="EndLaunchFeedback"/>, called by the window) or after <see cref="LaunchFeedbackDuration"/>.
    /// </summary>
    private void StartLaunchFeedback(ItemViewModel item)
    {
        EndLaunchFeedback();
        Model.Find(item.Id, out var parent);
        var shown = parent is null ? item : GetOrCreateViewModel(parent);
        _launching = shown;
        shown.IsLaunching = true;
        var version = _launchFeedbackVersion;
        LaunchFeedbackExpiry = EndLaunchFeedbackLaterAsync(version);
    }

    /// <summary>Stops the pulse of the last launch, if any.</summary>
    public void EndLaunchFeedback()
    {
        _launchFeedbackVersion++;
        if (_launching is { } item)
        {
            _launching = null;
            item.IsLaunching = false;
        }
    }

    private async Task EndLaunchFeedbackLaterAsync(int version)
    {
        await Task.Delay(LaunchFeedbackDuration);
        if (version == _launchFeedbackVersion)
        {
            EndLaunchFeedback();
        }
    }

    // ----- separators and colour tags ---------------------------------------------------------

    /// <summary>Adds a separator of <paramref name="kind"/> at <paramref name="index"/>; a label asks for its text first.</summary>
    public void AddSeparator(ItemListViewModel list, int index, string kind)
    {
        var separator = new SeparatorItem { Kind = kind };
        if (separator.EffectiveKind == SeparatorItem.Label)
        {
            var text = _dialogs.AskText(Strings.Separator_LabelTitle, Strings.Separator_LabelPrompt, null)?.Trim();
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            separator.Name = text;
        }

        Model.Insert(separator, list.Group, index);
        list.Sync();
        Save();
    }

    public void AddSeparatorAfter(ItemViewModel item, string kind)
    {
        var list = ListContaining(item, out var index);
        AddSeparator(list, index + 1, kind);
    }

    /// <summary>
    /// Turns a separator into another kind. Choosing a label asks for its text, offering the current one when it is a
    /// label already; a line's or gap's name means nothing (it may be "Group" from a 0.6.0 PC), so it is not offered.
    /// </summary>
    public void SetSeparatorKind(ItemViewModel item, string kind)
    {
        if (item.Model is not SeparatorItem separator)
        {
            return;
        }

        var draft = (SeparatorItem)separator.Clone();
        draft.Kind = kind;
        if (draft.EffectiveKind == SeparatorItem.Label)
        {
            var current = separator.EffectiveKind == SeparatorItem.Label ? separator.Name : null;
            var text = _dialogs.AskText(Strings.Separator_LabelTitle, Strings.Separator_LabelPrompt, current)?.Trim();
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            draft.Name = text;
        }
        else if (draft.EffectiveKind == separator.EffectiveKind)
        {
            return;
        }
        else
        {
            draft.Name = string.Empty; // a caption's text does not stay with a line or a gap
        }

        Model.Update(draft);
        item.RefreshFromModel();
        Save();
    }

    /// <summary>Gives the item a colour tag (<see cref="ColorTags"/>), or takes it away with <see langword="null"/>.</summary>
    public void SetColorTag(ItemViewModel item, string? tag)
    {
        var normalized = ColorTags.Normalize(tag);
        if (item.IsUnknown || string.Equals(item.Model.ColorTag, normalized, StringComparison.Ordinal))
        {
            return; // an item from a newer version is written back as it was read, so a tag would not be kept
        }

        var draft = item.Model.Clone();
        draft.ColorTag = normalized;
        Model.Update(draft);
        item.RefreshFromModel();
        Save();
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
            _dialogs.Notify(string.Format(Strings.Launch_CantOpenWith, target.Name), $"{result.ErrorMessage}\n{shortcut.EffectiveLocation}");
            return;
        }

        IsGroupPopupOpen = false;
    }

    private async Task DropOnSpecialAsync(ItemViewModel target, SpecialIconItem special, IReadOnlyList<string> paths)
    {
        var result = await target.SpecialHandler.DropFilesAsync(special, paths);
        if (result.Outcome == LaunchOutcome.Failed)
        {
            _dialogs.Notify(target.Name, result.ErrorMessage ?? Strings.Common_UnknownError);
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

        var draft = new GroupItem { Name = Strings.Common_Group };
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

    /// <summary>Add apps…: the installed apps the user ticks become shortcuts at <paramref name="index"/>.</summary>
    public void AddApp(ItemListViewModel list, int index) => AddApps(_dialogs.ShowAddApp(LocationsOnBar()), list, index);

    public void AddAppAfter(ItemViewModel item)
    {
        var list = ListContaining(item, out var index);
        AddApp(list, index + 1);
    }

    /// <summary>One shortcut per app, named as the Start menu names it, inserted in order.</summary>
    internal void AddApps(IReadOnlyList<InstalledApp> apps, ItemListViewModel list, int index)
    {
        if (apps.Count == 0)
        {
            return;
        }

        foreach (var app in apps)
        {
            Model.Insert(new ShortcutItem { Name = app.Name, Location = DataLocation.ForStorage(app.Location) }, list.Group, index);
            index = index == int.MaxValue ? index : index + 1;
        }

        list.Sync();
        Save();
        AppLog.Default.Info("Layout", $"Added {apps.Count} installed app(s) into \"{list.Group?.Name ?? "the bar"}\"");
    }

    /// <summary>Every shortcut's location on this PC, expanded, for marking what is already on the bar.</summary>
    private HashSet<string> LocationsOnBar() =>
        new(Model.AllShortcuts().Select(s => Environment.ExpandEnvironmentVariables(s.EffectiveLocation.Trim())), StringComparer.OrdinalIgnoreCase);

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
            _dialogs.Notify(item.Name, Strings.Launch_NewerVersionEdit);
            return;
        }

        if (item.Model is SeparatorItem)
        {
            if (item.IsLabelSeparator)
            {
                SetSeparatorKind(item, SeparatorItem.Label); // edits the caption
            }

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

    /// <summary>
    /// The Repair dialog for a shortcut: the chosen path becomes this PC's own location (other PCs that share the bar
    /// keep theirs) or, if the user says so, the location on every PC.
    /// </summary>
    public void RepairItem(ItemViewModel item)
    {
        if (item.Model is not ShortcutItem shortcut || _dialogs.ShowRepair(shortcut) is not { } repair)
        {
            return;
        }

        var draft = (ShortcutItem)shortcut.Clone();
        var path = DataLocation.ForStorage(repair.Path); // %APPDRIVE% on a portable copy's own drive
        if (StartsInProgramFolder(shortcut))
        {
            draft.StartIn = null; // it followed the old program: now every PC starts the program in its own folder
        }

        if (repair.OnlyThisPc && ThisDevice.Id is { } device)
        {
            draft.SetLocationFor(device, path);
        }
        else
        {
            draft.Location = path;
            if (ThisDevice.Id is { } here)
            {
                draft.SetLocationFor(here, null);
            }
        }

        Model.Update(draft);
        AppLog.Default.Info("Layout", $"Repaired \"{shortcut.Name}\": {repair.Path}{(repair.OnlyThisPc ? " (this PC only)" : string.Empty)}");
        item.RefreshFromModel();
        item.LoadImage();
        Tracker.Watch(shortcut);
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

    // ----- remove with undo -------------------------------------------------------------------

    /// <summary>How long the Undo offer stays after a removal; shortened by tests.</summary>
    internal static TimeSpan UndoWindow { get; set; } = TimeSpan.FromSeconds(10);

    private sealed record PendingRemoval(LauncherItem Item, Guid? ParentId, int Index);

    /// <summary>True while the last removal can still be undone (the banner under the icons is showing).</summary>
    public bool CanUndoRemove => _pendingRemoval is not null;

    public string UndoText => _pendingRemoval is null ? string.Empty : string.Format(Strings.Launch_Removed, DisplayNameOf(_pendingRemoval.Item));

    /// <summary>What an item is called in messages: its name, or for a line or gap what it is.</summary>
    private static string DisplayNameOf(LauncherItem item) => item switch
    {
        SeparatorItem { EffectiveKind: SeparatorItem.Line } => Strings.Separator_LineName,
        SeparatorItem { EffectiveKind: SeparatorItem.Gap } => Strings.Separator_GapName,
        _ => item.Name,
    };

    public ICommand UndoRemoveCommand { get; }

    /// <summary>Completes when the current Undo offer has expired (at once when there is none); tests await it.</summary>
    internal Task UndoExpiry { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// Removes a shortcut or special icon at once and offers Undo for <see cref="UndoWindow"/>. Removing a group
    /// takes everything in it with it, so that still asks first and cannot be undone.
    /// </summary>
    public void RemoveItem(ItemViewModel item)
    {
        if (item.IsGroup)
        {
            var message = item.ChildCount == 0
                ? string.Format(Strings.Launch_RemoveGroupQuestion, item.Name)
                : string.Format(Strings.Launch_RemoveGroupWithItems, item.Name, item.ChildCount);
            if (!_dialogs.Confirm(Strings.Launch_RemoveGroupTitle, message))
            {
                return;
            }
        }

        var removed = Model.Find(item.Id, out var parent);
        if (removed is null)
        {
            return;
        }

        var index = 0;
        foreach (var candidate in Model.ItemsOf(parent))
        {
            if (candidate.Id == item.Id)
            {
                break;
            }

            index++;
        }

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

        if (!item.IsGroup)
        {
            OfferUndo(new PendingRemoval(removed, parent?.Id, index));
        }
    }

    private void OfferUndo(PendingRemoval removal)
    {
        _pendingRemoval = removal;
        var version = ++_removalVersion;
        NotifyUndoChanged();
        UndoExpiry = ExpireUndoAsync(version);
    }

    private async Task ExpireUndoAsync(int version)
    {
        await Task.Delay(UndoWindow);
        if (version == _removalVersion && _pendingRemoval is not null)
        {
            _pendingRemoval = null;
            NotifyUndoChanged();
        }
    }

    /// <summary>Puts the last removed item back where it was; at the end of the root when its group has gone since.</summary>
    public void UndoRemove()
    {
        if (_pendingRemoval is not { } removal)
        {
            return;
        }

        _pendingRemoval = null;
        _removalVersion++;
        NotifyUndoChanged();
        if (Model.Contains(removal.Item.Id))
        {
            return;
        }

        var parent = removal.ParentId is { } parentId ? Model.FindGroup(parentId) : null;
        var index = removal.ParentId is not null && parent is null ? int.MaxValue : removal.Index;
        Model.Insert(removal.Item, parent, index);
        AppLog.Default.Info("Layout", $"Restored \"{removal.Item.Name}\"");
        ListFor(parent).Sync();
        OnPropertyChanged(nameof(HasMultiLaunchMembers));
        Save();
    }

    private void NotifyUndoChanged()
    {
        OnPropertyChanged(nameof(CanUndoRemove));
        OnPropertyChanged(nameof(UndoText));
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
            _dialogs.Notify(Strings.Launch_CantOpenLocation, $"{result.ErrorMessage}\n{item.Location}");
        }
    }

    public async Task LaunchMultiLaunchGroupAsync()
    {
        var members = Model.MultiLaunchMembers().ToList();
        if (members.Count == 0)
        {
            _dialogs.Notify(Strings.Launch_MultiEmptyTitle, Strings.Launch_MultiEmpty);
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
            _dialogs.Notify(Strings.Launch_SomeFailed, string.Join("\n", failures));
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
            : LaunchResult.Failure(Strings.Launch_LayoutNotCreated);
        if (result.Outcome == LaunchOutcome.Failed)
        {
            _dialogs.Notify(Strings.Launch_LayoutFileTitle, $"{result.ErrorMessage}\n{_storage.FilePath}");
        }
    }

    private void ShowAbout()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "dev";
        _dialogs.ShowInfo(Strings.About_Title, string.Format(Strings.About_Text, version, _storage.FilePath));
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
            _dialogs.Notify(Strings.Launch_SaveFailed, $"{ex.Message}\n{_storage.FilePath}");
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
