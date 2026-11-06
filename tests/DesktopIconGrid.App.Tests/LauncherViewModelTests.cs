using System.IO;
using System.Windows.Media;
using System.Windows.Threading;
using DesktopIconGrid.Core;
using DesktopIconGrid.Core.Launching;
using DesktopIconGrid.Core.Imports;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Core.Storage;
using DesktopIconGrid.Resources;
using DesktopIconGrid.Services;
using DesktopIconGrid.SpecialIcons;
using DesktopIconGrid.ViewModels;

namespace DesktopIconGrid.App.Tests;

/// <summary>Remove without a question, Undo within the window: the bar-level behaviour behind the banner.</summary>
[Collection("Shared icon worker")]
public sealed class LauncherViewModelTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("dig-launcher-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private sealed class RecordingDialogs : IDialogService
    {
        public bool ConfirmAnswer { get; set; } = true;

        public List<string> Confirmations { get; } = [];

        public List<string> Notices { get; } = [];

        public List<string> Modals { get; } = [];

        public bool ShowItemProperties(LauncherItem item, bool isNew) => false;

        public LauncherSettings? ShowSettings(LauncherSettings current) => null;

        public bool Confirm(string title, string message)
        {
            Confirmations.Add(title);
            return ConfirmAnswer;
        }

        public void ShowError(string title, string message) => Modals.Add(title);

        public void ShowInfo(string title, string message) => Modals.Add(title);

        public void Notify(string title, string message) => Notices.Add(title);

        public void ShowSync()
        {
        }

        public void ShowLog(LogViewerViewModel viewModel)
        {
        }

        public void ShowNameTable(LauncherViewModel launcher)
        {
        }

        public void ShowAppLog()
        {
        }

        public string? TextAnswer { get; set; }

        public string? AskText(string title, string prompt, string? initialValue) => TextAnswer;

        public string? PickFolder(string title, string? initialFolder) => null;

        public List<string> RepairRequests { get; } = [];

        public RepairResult? RepairAnswer { get; set; }

        public RepairResult? ShowRepair(ShortcutItem shortcut)
        {
            RepairRequests.Add(shortcut.Name);
            return RepairAnswer;
        }

        public IReadOnlyList<InstalledApp> AddAppAnswer { get; set; } = [];

        public IReadOnlySet<string>? OnBarSeen { get; private set; }

        public IReadOnlyList<InstalledApp> ShowAddApp(IReadOnlySet<string> locationsOnBar)
        {
            OnBarSeen = locationsOnBar;
            return AddAppAnswer;
        }
    }

    /// <summary>Records every launch and answers with <see cref="Result"/> (a failure unless a test says otherwise).</summary>
    private sealed class RecordingLauncher : ILaunchService
    {
        public List<(string Location, bool AsAdministrator)> Launches { get; } = [];

        public LaunchResult Result { get; set; } = LaunchResult.Failure("nope");

        public LaunchResult Launch(ShortcutItem shortcut, bool asAdministrator = false)
        {
            Launches.Add((shortcut.Location, asAdministrator));
            return Result;
        }

        public LaunchResult LaunchWith(ShortcutItem shortcut, IReadOnlyList<string> paths) => LaunchResult.Failure("nope");
    }

    private sealed class NoIcons : IIconProvider
    {
        public ImageSource? GetIcon(string source, int pixelSize) => null;

        public ImageSource? GetItemIcon(LauncherItem item, int pixelSize) => null;

        public ImageSource? GetFolderIcon(int pixelSize) => null;

        public bool CanResolve(string? source) => false;

        public void ClearCache()
        {
        }
    }

    /// <summary>Stands in for the window switcher: records what it was asked and answers as told.</summary>
    private sealed class FakeSwitcher : IWindowSwitcher
    {
        public bool Answer { get; set; } = true;

        public int Calls { get; private set; }

        public IReadOnlySet<int>? LastProcessIds { get; private set; }

        public bool TrySwitch(IReadOnlySet<int> processIds)
        {
            Calls++;
            LastProcessIds = processIds;
            return Answer;
        }
    }

    private readonly RecordingLauncher _launcher = new();

    /// <summary>
    /// A bar of three shortcuts to made-up paths. Nothing counts as missing unless <paramref name="isMissing"/> says so:
    /// the real check would find those paths missing on a CI machine, at a moment of its choosing.
    /// </summary>
    private (LauncherViewModel ViewModel, LauncherModel Model, RecordingDialogs Dialogs) Create(IWindowSwitcher? windows = null, Func<ShortcutItem, bool>? isMissing = null)
    {
        var model = new LauncherModel();
        model.Add(new ShortcutItem { Name = "A", Location = "C:\\a.exe" });
        model.Add(new ShortcutItem { Name = "B", Location = "C:\\b.exe" });
        model.Add(new ShortcutItem { Name = "C", Location = "C:\\c.exe" });
        var dialogs = new RecordingDialogs();
        var tracker = new ProcessTracker(Dispatcher.CurrentDispatcher, Path.Combine(_directory, "logs"));
        var viewModel = new LauncherViewModel(
            model,
            new LayoutStorage(Path.Combine(_directory, "layout.json")),
            new NoIcons(),
            dialogs,
            _launcher,
            tracker,
            new SpecialIconRegistry([]),
            action => action(),
            windows,
            isMissing ?? (_ => false));
        return (viewModel, model, dialogs);
    }

    private static IEnumerable<string> Names(IEnumerable<LauncherItem> items) => items.Select(i => i.Name);

    [Fact]
    public void Removing_a_shortcut_asks_nothing_and_undo_puts_it_back_where_it_was()
    {
        var previous = LauncherViewModel.UndoWindow;
        LauncherViewModel.UndoWindow = TimeSpan.FromSeconds(30);
        try
        {
            var (viewModel, model, dialogs) = Create();

            viewModel.RemoveItem(viewModel.RootItems.Items[1]);

            Assert.Equal(["A", "C"], Names(model.Items));
            Assert.Empty(dialogs.Confirmations);
            Assert.True(viewModel.CanUndoRemove);
            Assert.Contains("B", viewModel.UndoText);

            viewModel.UndoRemove();

            Assert.Equal(["A", "B", "C"], Names(model.Items));
            Assert.Equal(["A", "B", "C"], viewModel.RootItems.Items.Select(i => i.Name));
            Assert.False(viewModel.CanUndoRemove);
            viewModel.UndoRemove(); // nothing pending: no change, no exception
            Assert.Equal(["A", "B", "C"], Names(model.Items));
        }
        finally
        {
            LauncherViewModel.UndoWindow = previous;
        }
    }

    [Fact]
    public async Task The_undo_offer_expires()
    {
        var previous = LauncherViewModel.UndoWindow;
        LauncherViewModel.UndoWindow = TimeSpan.FromMilliseconds(50);
        try
        {
            var (viewModel, model, _) = Create();
            viewModel.RemoveItem(viewModel.RootItems.Items[0]);
            Assert.True(viewModel.CanUndoRemove);

            await viewModel.UndoExpiry; // the offer's own timer, however busy the test host is

            Assert.False(viewModel.CanUndoRemove);
            viewModel.UndoRemove();
            Assert.Equal(["B", "C"], Names(model.Items));
        }
        finally
        {
            LauncherViewModel.UndoWindow = previous;
        }
    }

    [Fact]
    public void Removing_a_group_still_asks_and_a_no_keeps_it()
    {
        var (viewModel, model, dialogs) = Create();
        model.Add(new GroupItem { Name = "G", Items = [new ShortcutItem { Name = "Inner", Location = "x" }] });
        viewModel.RootItems.Sync();
        dialogs.ConfirmAnswer = false;

        viewModel.RemoveItem(viewModel.RootItems.Items[3]);

        Assert.Equal([Strings.Launch_RemoveGroupTitle], dialogs.Confirmations);
        Assert.Equal(["A", "B", "C", "G"], Names(model.Items));
        Assert.False(viewModel.CanUndoRemove);
    }

    [Fact]
    public void Undo_restores_into_the_group_or_to_the_root_when_the_group_is_gone()
    {
        var previous = LauncherViewModel.UndoWindow;
        LauncherViewModel.UndoWindow = TimeSpan.FromSeconds(30);
        try
        {
            var (viewModel, model, dialogs) = Create();
            var group = new GroupItem { Name = "G", Items = [new ShortcutItem { Name = "First", Location = "x" }, new ShortcutItem { Name = "Second", Location = "y" }] };
            model.Add(group);
            viewModel.RootItems.Sync();
            var groupViewModel = viewModel.RootItems.Items[3];
            var second = groupViewModel.Children!.Items[1];

            viewModel.RemoveItem(second);
            Assert.Equal(["First"], Names(group.Items));
            viewModel.UndoRemove();
            Assert.Equal(["First", "Second"], Names(group.Items));

            viewModel.RemoveItem(groupViewModel.Children!.Items[0]);
            dialogs.ConfirmAnswer = true;
            viewModel.RemoveItem(groupViewModel); // the group goes while "First" is still undoable
            viewModel.UndoRemove();

            Assert.Equal(["A", "B", "C", "First"], Names(model.Items)); // its group is gone: it lands at the end of the root
        }
        finally
        {
            LauncherViewModel.UndoWindow = previous;
        }
    }

    [Fact]
    public void A_shortcut_that_fails_to_start_is_reported_without_a_message_box()
    {
        var (viewModel, _, dialogs) = Create();

        viewModel.Activate(viewModel.RootItems.Items[0], null, ActivationModifiers.None);

        Assert.Single(dialogs.Notices);
        Assert.Empty(dialogs.Modals);
    }

    [Fact]
    public void A_click_on_a_running_program_switches_to_it_instead_of_starting_another()
    {
        var switcher = new FakeSwitcher();
        var (viewModel, _, dialogs) = Create(switcher);

        viewModel.Activate(viewModel.RootItems.Items[0], null, ActivationModifiers.None);

        Assert.Equal(1, switcher.Calls);
        Assert.Empty(_launcher.Launches);
        Assert.Empty(dialogs.Notices);
    }

    [Fact]
    public void Without_a_window_to_switch_to_the_program_starts()
    {
        var (viewModel, _, _) = Create(new FakeSwitcher { Answer = false });

        viewModel.Activate(viewModel.RootItems.Items[0], null, ActivationModifiers.None);

        Assert.Equal([(@"C:\a.exe", false)], _launcher.Launches);
    }

    [Theory]
    [InlineData(ActivationModifiers.Shift, false)]
    [InlineData(ActivationModifiers.NewInstance, false)]
    [InlineData(ActivationModifiers.Control | ActivationModifiers.Shift, true)]
    public void Shift_or_a_middle_click_start_another_and_Ctrl_Shift_starts_one_elevated(ActivationModifiers modifiers, bool asAdministrator)
    {
        var switcher = new FakeSwitcher();
        var (viewModel, _, _) = Create(switcher);

        viewModel.Activate(viewModel.RootItems.Items[1], null, modifiers);

        Assert.Equal(0, switcher.Calls);
        Assert.Equal([(@"C:\b.exe", asAdministrator)], _launcher.Launches);
    }

    [Fact]
    public void A_shortcut_set_to_start_another_never_switches()
    {
        var switcher = new FakeSwitcher();
        var (viewModel, model, _) = Create(switcher);
        ((ShortcutItem)model.Items[2]).WhenRunning = ShortcutItem.WhenRunningStart;

        viewModel.Activate(viewModel.RootItems.Items[2], null, ActivationModifiers.None);

        Assert.Equal(0, switcher.Calls);
        Assert.Equal([(@"C:\c.exe", false)], _launcher.Launches);
    }

    [Fact]
    public void Typing_lights_the_matches_and_the_groups_that_hold_them_and_fades_the_rest()
    {
        var (viewModel, model, _) = Create();
        model.Add(new GroupItem
        {
            Name = "Tools",
            Items = [new ShortcutItem { Name = "WinSCP", Location = @"C:\Tools\WinSCP.exe" }, new ShortcutItem { Name = "Putty", Location = @"C:\Tools\putty.exe" }],
        });
        viewModel.RootItems.Sync();
        var group = viewModel.RootItems.Items[3];

        viewModel.SearchText = "ws"; // the initials of WinSCP

        Assert.True(viewModel.IsSearching);
        var match = Assert.Single(viewModel.SearchResults);
        Assert.Equal("WinSCP", match.Name);
        Assert.Same(match, viewModel.SelectedSearchResult);
        Assert.False(match.IsSearchDimmed);
        Assert.True(match.IsSearchSelected);
        Assert.False(group.IsSearchDimmed);
        Assert.True(group.IsSearchSelected); // framed with its match, which is out of sight while the group is closed
        Assert.All(viewModel.RootItems.Items.Take(3), item => Assert.True(item.IsSearchDimmed));
        Assert.True(group.Children!.Items.Single(i => i.Name == "Putty").IsSearchDimmed); // opened during the search

        viewModel.ClearSearch();

        Assert.False(viewModel.IsSearching);
        Assert.Null(viewModel.SelectedSearchResult);
        Assert.All(viewModel.RootItems.Items.Concat(group.Children!.Items), item => Assert.False(item.IsSearchDimmed || item.IsSearchSelected));
    }

    [Fact]
    public void The_arrows_move_the_frame_round_the_matches_and_Enter_opens_the_framed_one()
    {
        _launcher.Result = LaunchResult.Started;
        var (viewModel, model, _) = Create();
        model.Add(new ShortcutItem { Name = "Mail", Location = @"C:\mail.exe" });
        model.Add(new ShortcutItem { Name = "Mailbox cleaner", Location = @"C:\clean.exe" });
        viewModel.RootItems.Sync();
        ItemViewModel? activated = null;
        viewModel.ItemActivated += (_, item) => activated = item;

        viewModel.SearchText = "mail";
        Assert.Equal(["Mail", "Mailbox cleaner"], viewModel.SearchResults.Select(r => r.Name));

        viewModel.MoveSearchSelection(1);
        Assert.Equal("Mailbox cleaner", viewModel.SelectedSearchResult!.Name);
        viewModel.MoveSearchSelection(1);
        Assert.Equal("Mail", viewModel.SelectedSearchResult!.Name); // round to the first
        viewModel.MoveSearchSelection(-1);
        Assert.Equal("Mailbox cleaner", viewModel.SelectedSearchResult!.Name); // and back to the last
        Assert.True(viewModel.SelectedSearchResult.IsSearchSelected);
        Assert.False(viewModel.SearchResults[0].IsSearchSelected);

        Assert.True(viewModel.LaunchSearchSelection());

        Assert.Equal([(@"C:\clean.exe", false)], _launcher.Launches);
        Assert.Equal("Mailbox cleaner", activated?.Name);
        Assert.Equal(string.Empty, viewModel.SearchText);
        Assert.False(viewModel.LaunchSearchSelection()); // nothing typed, nothing to open
    }

    [Fact]
    public async Task A_started_program_s_icon_pulses_until_the_bar_loses_the_foreground_or_the_time_runs_out()
    {
        var previous = LauncherViewModel.LaunchFeedbackDuration;
        LauncherViewModel.LaunchFeedbackDuration = TimeSpan.FromMilliseconds(50);
        try
        {
            _launcher.Result = LaunchResult.Started;
            var (viewModel, model, _) = Create();
            model.Add(new GroupItem { Name = "Tools", Items = [new ShortcutItem { Name = "Inner", Location = @"C:\inner.exe" }] });
            viewModel.RootItems.Sync();
            var first = viewModel.RootItems.Items[0];

            viewModel.Activate(first, null, ActivationModifiers.None);
            Assert.True(first.IsLaunching);
            viewModel.EndLaunchFeedback(); // the bar lost the foreground to the program
            Assert.False(first.IsLaunching);

            var group = viewModel.RootItems.Items[3];
            var inner = group.Children!.Items[0];
            viewModel.Activate(inner, null, ActivationModifiers.None);
            Assert.True(group.IsLaunching); // the popup closes on a launch, so the group's icon shows it
            Assert.False(inner.IsLaunching);
            await viewModel.LaunchFeedbackExpiry;
            Assert.False(group.IsLaunching);

            _launcher.Result = LaunchResult.Failure("nope");
            viewModel.Activate(viewModel.RootItems.Items[1], null, ActivationModifiers.None);
            Assert.Null(viewModel.LaunchingItem); // nothing started, nothing pulses
        }
        finally
        {
            LauncherViewModel.LaunchFeedbackDuration = previous;
        }
    }

    [Fact]
    public void Separators_go_after_an_icon_or_at_the_end_and_a_label_asks_for_its_text()
    {
        var (viewModel, model, dialogs) = Create();

        viewModel.AddSeparatorAfter(viewModel.RootItems.Items[0], SeparatorItem.Line);
        Assert.Equal(["A", "", "B", "C"], Names(model.Items));
        Assert.Equal(FlowSlot.Narrow, viewModel.RootItems.Items[1].Slot);

        dialogs.TextAnswer = null; // Cancel
        viewModel.AddSeparator(viewModel.RootItems, int.MaxValue, SeparatorItem.Label);
        Assert.Equal(4, model.Items.Count);

        dialogs.TextAnswer = "  Games ";
        viewModel.AddSeparator(viewModel.RootItems, int.MaxValue, SeparatorItem.Label);
        var label = viewModel.RootItems.Items[4];
        Assert.Equal("Games", label.Name);
        Assert.True(label.IsLabelSeparator);
        Assert.Equal(FlowSlot.Auto, label.Slot);

        viewModel.SetSeparatorKind(viewModel.RootItems.Items[1], SeparatorItem.Gap);
        Assert.True(viewModel.RootItems.Items[1].IsGapSeparator);
        Assert.Equal(FlowSlot.Cell, viewModel.RootItems.Items[1].Slot);

        dialogs.TextAnswer = "Fun";
        viewModel.EditItem(label); // F2 or Properties on a label edits its text
        Assert.Equal("Fun", label.Name);

        viewModel.Activate(label, null, ActivationModifiers.None); // a click on a separator does nothing
        Assert.Empty(_launcher.Launches);

        viewModel.RemoveItem(viewModel.RootItems.Items[1]);
        Assert.Contains(Strings.Separator_GapName, viewModel.UndoText); // not an empty name
    }

    [Fact]
    public void A_colour_tag_is_set_and_cleared()
    {
        var (viewModel, model, _) = Create();
        var item = viewModel.RootItems.Items[0];

        viewModel.SetColorTag(item, "Red");

        Assert.Equal(ColorTags.Red, model.Items[0].ColorTag);
        Assert.True(item.HasColorTag);
        Assert.NotNull(item.ColorTagBrush);
        Assert.Equal(Strings.Tag_Red, item.ColorTagName);

        viewModel.SetColorTag(item, null);

        Assert.Null(model.Items[0].ColorTag);
        Assert.False(item.HasColorTag);
    }

    [Fact]
    public void The_names_table_leaves_separators_out_and_filters_by_colour_tag()
    {
        var (viewModel, model, _) = Create();
        model.Add(new SeparatorItem { Kind = SeparatorItem.Label, Name = "Section" });
        model.Items[0].ColorTag = ColorTags.Red;
        model.Items[2].ColorTag = ColorTags.Blue;

        var table = new NameTableViewModel(viewModel);

        Assert.Equal(["A", "B", "C"], table.Rows.Select(r => r.GivenName));
        table.SelectedTagFilter = table.TagFilters.Single(f => f.Tag == ColorTags.Red);
        Assert.Equal(["A"], table.RowsView.Cast<NameRowViewModel>().Select(r => r.GivenName));
        table.SelectedTagFilter = table.TagFilters.Single(f => f.Tag == NameTableViewModel.NoTag);
        Assert.Equal(["B"], table.RowsView.Cast<NameRowViewModel>().Select(r => r.GivenName));
        table.SelectedTagFilter = table.TagFilters[0];
        Assert.Equal(3, table.RowsView.Cast<object>().Count());
    }

    [Fact]
    public void Repair_points_the_shortcut_at_the_chosen_path_for_this_PC_only_or_for_every_PC()
    {
        var previous = ThisDevice.Id;
        ThisDevice.Id = "this-pc";
        try
        {
            var (viewModel, model, dialogs) = Create();
            var item = viewModel.RootItems.Items[0];
            var shortcut = (ShortcutItem)model.Items[0];
            dialogs.RepairAnswer = new RepairResult(@"D:\Apps\a.exe", OnlyThisPc: true);

            viewModel.RepairItem(item);

            Assert.Equal(@"C:\a.exe", shortcut.Location); // other PCs keep theirs
            Assert.Equal(@"D:\Apps\a.exe", shortcut.EffectiveLocation);
            Assert.Equal(@"D:\Apps\a.exe", item.Location);

            dialogs.RepairAnswer = new RepairResult(@"E:\a.exe", OnlyThisPc: false);
            viewModel.RepairItem(item);

            Assert.Equal(@"E:\a.exe", shortcut.Location);
            Assert.Null(shortcut.Locations);

            dialogs.RepairAnswer = null; // Cancel
            viewModel.RepairItem(item);

            Assert.Equal(@"E:\a.exe", shortcut.Location);
            Assert.Equal(3, dialogs.RepairRequests.Count);
        }
        finally
        {
            ThisDevice.Id = previous;
        }
    }

    [Fact]
    public void A_click_on_a_shortcut_that_is_still_missing_opens_Repair_instead_of_launching()
    {
        var stillMissing = true;
        var (viewModel, _, dialogs) = Create(isMissing: _ => stillMissing);
        var item = viewModel.RootItems.Items[0];
        item.IsMissing = true;

        viewModel.Activate(item, null, ActivationModifiers.None);

        Assert.Equal(["A"], dialogs.RepairRequests);
        Assert.Empty(_launcher.Launches);

        stillMissing = false; // it came back since the icon was drawn
        viewModel.Activate(viewModel.RootItems.Items[1], null, ActivationModifiers.None); // another icon: no double-click guard
        item.IsMissing = true;
        viewModel.Activate(viewModel.RootItems.Items[0], null, ActivationModifiers.None);

        Assert.False(item.IsMissing);
        Assert.Single(dialogs.RepairRequests);
        Assert.Contains(_launcher.Launches, l => l.Location == @"C:\a.exe");
    }

    [Fact]
    public void Add_app_puts_the_ticked_apps_after_the_icon_and_tells_the_dialog_what_is_on_the_bar()
    {
        var (viewModel, model, dialogs) = Create();
        dialogs.AddAppAnswer =
        [
            new InstalledApp("Calculator", @"shell:AppsFolder\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", IsStoreApp: true),
            new InstalledApp("Code", @"C:\Programs\Code.exe", IsStoreApp: false),
        ];

        viewModel.AddAppAfter(viewModel.RootItems.Items[0]);

        Assert.Equal(["A", "Calculator", "Code", "B", "C"], Names(model.Items));
        Assert.Equal(@"C:\Programs\Code.exe", ((ShortcutItem)model.Items[2]).Location);
        Assert.Contains(@"c:\A.EXE", dialogs.OnBarSeen!); // any case

        dialogs.AddAppAnswer = []; // Cancel
        viewModel.AddApp(viewModel.RootItems, int.MaxValue);
        Assert.Equal(5, model.Items.Count);
    }

    [Fact]
    public void A_shortcut_with_parameters_starts_another_unless_told_to_switch_and_then_only_to_its_own_copies()
    {
        var exe = Path.Combine(_directory, "mstsc.exe");
        File.WriteAllBytes(exe, []);
        var switcher = new FakeSwitcher();
        var (viewModel, model, _) = Create(switcher);
        var byDefault = (ShortcutItem)model.Items[0];
        var toldToSwitch = (ShortcutItem)model.Items[1]; // a second icon, so the double-click guard does not swallow the click
        foreach (var shortcut in new[] { byDefault, toldToSwitch })
        {
            shortcut.Location = exe;
            shortcut.Parameters = "/v:server2";
            viewModel.Tracker.Watch(shortcut);
        }

        toldToSwitch.WhenRunning = ShortcutItem.WhenRunningSwitch;
        viewModel.Tracker.ApplySnapshot(new Dictionary<string, IReadOnlyList<int>>(StringComparer.OrdinalIgnoreCase) { [exe] = [4242] }); // server1, opened elsewhere

        viewModel.Activate(viewModel.RootItems.Items[0], null, ActivationModifiers.None);

        Assert.Equal(0, switcher.Calls); // the default with parameters: start another
        Assert.Single(_launcher.Launches);

        viewModel.Activate(viewModel.RootItems.Items[1], null, ActivationModifiers.None);

        Assert.Equal(1, switcher.Calls);
        Assert.Empty(switcher.LastProcessIds!); // not the server1 window: this shortcut started nothing that still runs
    }
}

