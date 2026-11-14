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
    public void A_line_turned_into_a_label_offers_no_text_even_with_a_stale_name()
    {
        var (viewModel, model, dialogs) = Create();
        viewModel.AddSeparator(viewModel.RootItems, 1, SeparatorItem.Line);
        model.Items[1].Name = "Group"; // what 0.6.0 named it, before this version ignored a line's name on loading
        dialogs.TextAnswer = "Work";

        viewModel.SetSeparatorKind(viewModel.RootItems.Items[1], SeparatorItem.Label);

        Assert.Null(dialogs.OfferedText);
        Assert.Equal("Work", model.Items[1].Name);

        dialogs.TextAnswer = "Home";
        viewModel.SetSeparatorKind(viewModel.RootItems.Items[1], SeparatorItem.Label); // a label again: its text is offered

        Assert.Equal("Work", dialogs.OfferedText);
        Assert.Equal("Home", model.Items[1].Name);

        viewModel.SetSeparatorKind(viewModel.RootItems.Items[1], SeparatorItem.Gap);

        Assert.Equal(string.Empty, model.Items[1].Name);
    }

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
    public void A_Store_app_is_also_looked_for_by_its_package_so_a_suspended_one_is_found()
    {
        const string calculator = "Microsoft.WindowsCalculator_8wekyb3d8bbwe";
        var switcher = new FakeSwitcher();
        var (viewModel, model, _) = Create(switcher);
        var app = (ShortcutItem)model.Items[0];
        var withParameters = (ShortcutItem)model.Items[1]; // a second icon, so the double-click guard does not swallow the click
        app.Location = withParameters.Location = $@"shell:AppsFolder\{calculator}!App";
        withParameters.Parameters = "--x";
        withParameters.WhenRunning = ShortcutItem.WhenRunningSwitch;
        viewModel.Tracker.Watch(app);
        viewModel.Tracker.Watch(withParameters);

        viewModel.Activate(viewModel.RootItems.Items[0], null, ActivationModifiers.None);

        Assert.Equal(calculator, switcher.LastPackageFamily); // its frame is found even when the app holds no window in it

        viewModel.Activate(viewModel.RootItems.Items[1], null, ActivationModifiers.None);

        Assert.Equal(2, switcher.Calls);
        Assert.Null(switcher.LastPackageFamily); // with parameters: only the copies this shortcut started
        Assert.Empty(_launcher.Launches);
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
    public void Repair_for_every_PC_keeps_another_PCs_own_location_and_moves_a_start_in_that_was_the_program_folder()
    {
        var previous = ThisDevice.Id;
        ThisDevice.Id = "this-pc";
        try
        {
            var (viewModel, model, dialogs) = Create();
            var shortcut = (ShortcutItem)model.Items[0];
            shortcut.SetLocationFor("other-pc", @"D:\Tools\a.exe"); // that PC's path differs, so it has its own
            shortcut.StartIn = @"C:\"; // the folder of C:\a.exe, as a Windows shortcut sets it
            var other = (ShortcutItem)model.Items[1];
            other.StartIn = @"D:\Projects";
            dialogs.RepairAnswer = new RepairResult(@"E:\Apps\a.exe", OnlyThisPc: false);

            viewModel.RepairItem(viewModel.RootItems.Items[0]);

            Assert.Equal(@"E:\Apps\a.exe", shortcut.Location);
            Assert.Equal(@"D:\Tools\a.exe", shortcut.LocationFor("other-pc"));
            Assert.Null(shortcut.StartIn); // it moves with the program: each PC starts it in its own folder

            dialogs.RepairAnswer = new RepairResult(@"E:\Apps\b.exe", OnlyThisPc: true);
            viewModel.RepairItem(viewModel.RootItems.Items[1]);

            Assert.Equal(@"D:\Projects", other.StartIn); // a folder of its own stays
        }
        finally
        {
            ThisDevice.Id = previous;
        }
    }

    [Fact]
    public async Task The_names_table_marks_missing_shortcuts_with_the_bars_own_check_off_the_window_thread()
    {
        // The review: the table ran LocationProbe.IsMissing on the UI thread for every row, past the injected check.
        var testThread = Environment.CurrentManagedThreadId;
        var checkedOn = new System.Collections.Concurrent.ConcurrentBag<int>();
        var (viewModel, _, _) = Create(isMissing: shortcut =>
        {
            checkedOn.Add(Environment.CurrentManagedThreadId);
            return shortcut.Name == "B";
        });

        var table = new NameTableViewModel(viewModel);
        await table.MissingChecked.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(["B"], table.Rows.Where(r => NameTableViewModel.Matches(r, NameTableViewModel.MissingHere)).Select(r => r.GivenName));
        Assert.NotEmpty(checkedOn);
        Assert.DoesNotContain(testThread, checkedOn);
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
    public void Restoring_an_earlier_layout_keeps_the_current_one_first_and_counts_as_a_change()
    {
        var (viewModel, _, dialogs) = Create();
        var storage = new LayoutStorage(Path.Combine(_directory, "layout.json"));
        viewModel.AddPaths([@"C:\d.exe"], viewModel.RootItems, int.MaxValue); // saved: A B C d
        storage.History.Record(storage.FilePath, force: true);
        var kept = Assert.Single(storage.History.List());
        viewModel.RemoveItem(viewModel.RootItems.Items[0]); // B C d
        var changes = 0;
        viewModel.LayoutChanged += (_, _) => changes++;
        dialogs.HistoryAnswer = kept;

        viewModel.RestoreLayout();

        Assert.Equal(["A", "B", "C", "d"], Names(viewModel.Model.Items));
        Assert.Equal(kept, Assert.Single(dialogs.HistoryOffered!)); // read again: an equal entry, not the same one
        Assert.Equal(2, storage.History.List().Count); // the bar before the restore is kept too
        Assert.Equal(1, changes);

        dialogs.HistoryAnswer = null; // Cancel
        viewModel.RestoreLayout();
        Assert.Equal(4, viewModel.Model.Items.Count);
    }

    [Fact]
    public void A_folder_marked_as_a_stack_opens_as_one_and_Shift_or_a_missing_folder_opens_it_the_usual_way()
    {
        var (viewModel, model, dialogs) = Create();
        var folder = new ShortcutItem { Name = "Downloads", Location = _directory, OpenAsStack = true, StackNewestFirst = true };
        // Two more icons on the folder for Shift and the middle click: a second click on one icon within 0.4 s is taken
        // for a double-click and dropped.
        var shifted = new ShortcutItem { Name = "Downloads 2", Location = _directory, OpenAsStack = true };
        var middle = new ShortcutItem { Name = "Downloads 3", Location = _directory, OpenAsStack = true };
        var gone = new ShortcutItem { Name = "Gone", Location = Path.Combine(_directory, "nope"), OpenAsStack = true };
        model.Add(folder);
        model.Add(shifted);
        model.Add(middle);
        model.Add(gone);
        viewModel.RootItems.Sync();

        viewModel.Activate(viewModel.GetOrCreateViewModel(folder), null, ActivationModifiers.None);

        Assert.Equal([(_directory, true)], dialogs.Stacks);
        Assert.Empty(_launcher.Launches);

        // A file dropped onto a program inside the stack goes through the launcher's open-with path.
        Assert.NotNull(dialogs.StackOpenWith);
        dialogs.StackOpenWith!(@"C:\Tools\edit.exe", [@"C:\a.txt"]);
        var opened = Assert.Single(_launcher.OpenedWith);
        Assert.Equal(@"C:\Tools\edit.exe", opened.Location);
        Assert.Equal([@"C:\a.txt"], opened.Paths);

        // Shift (and a middle click, which arrives as NewInstance) opens the folder itself, as before stacks.
        viewModel.Activate(viewModel.GetOrCreateViewModel(shifted), null, ActivationModifiers.Shift);
        Assert.Single(dialogs.Stacks);
        Assert.Equal(_directory, Assert.Single(_launcher.Launches).Location);
        viewModel.Activate(viewModel.GetOrCreateViewModel(middle), null, ActivationModifiers.NewInstance);
        Assert.Single(dialogs.Stacks);
        Assert.Equal(2, _launcher.Launches.Count);
        Assert.Equal(_directory, _launcher.Launches[1].Location);

        viewModel.Activate(viewModel.GetOrCreateViewModel(gone), null, ActivationModifiers.None); // not a folder here: launched as usual
        Assert.Single(dialogs.Stacks);
        Assert.Equal(3, _launcher.Launches.Count);
        Assert.Equal(gone.Location, _launcher.Launches[^1].Location);
    }

    /// <summary>What the tracker's scan would find running: program path → process ids.</summary>
    private static Dictionary<string, IReadOnlyList<int>> Running(params (string Path, int[] Ids)[] programs)
    {
        var running = new Dictionary<string, IReadOnlyList<int>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, ids) in programs)
        {
            running[path] = ids;
        }

        return running;
    }

    /// <summary>An empty file to stand for a program, so the tracker watches the shortcut's path.</summary>
    private string Program(string name)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllBytes(path, []);
        return path;
    }

    [Fact]
    public async Task A_workspace_starts_its_members_and_puts_their_windows_where_they_were_remembered()
    {
        var switcher = new FakeSwitcher { Answer = false };
        var (viewModel, model, dialogs) = Create(switcher);
        viewModel.WorkspaceStep = TimeSpan.FromMilliseconds(5);
        viewModel.WorkspaceWindowWait = TimeSpan.FromSeconds(5);
        _launcher.Result = LaunchResult.Started;
        var exe = Program("b.exe");
        var shortcut = (ShortcutItem)model.Items[1];
        shortcut.Location = exe;
        viewModel.Tracker.Watch(shortcut);
        var b = viewModel.RootItems.Items[1];
        ((ShortcutItem)model.Items[0]).MultiLaunch = true;

        dialogs.TextAnswer = " Work ";
        viewModel.AddToNewWorkspace(b);
        Assert.Equal([DesktopIconGrid.Core.Launching.Workspaces.MultiLaunch, "Work"], viewModel.WorkspaceNames);
        Assert.True(viewModel.IsInWorkspace(b, "work"));

        Assert.Equal(0, viewModel.CaptureWorkspace("Work")); // nothing has a window yet
        var remembered = new SavedWindow(100, 50, 800, 600, false);
        switcher.Shown.Add(FakeSwitcher.Window(1, 500));
        switcher.Positions[new IntPtr(1)] = remembered;
        viewModel.Tracker.ApplySnapshot(Running((exe, [500])));
        Assert.Equal(1, viewModel.CaptureWorkspace("Work"));

        // Closed again. Started, it has a process at once and shows its window two looks later.
        switcher.Shown.Clear();
        viewModel.Tracker.ApplySnapshot(Running((exe, [501])));
        switcher.Coming.Add((FakeSwitcher.Window(2, 501), 2)); // the start's first look is the one before anything is started
        await viewModel.StartWorkspaceAsync("work");

        Assert.Equal([(exe, false)], _launcher.Launches); // only the member, not the Multi Launch one
        Assert.Equal([(new IntPtr(2), remembered), (new IntPtr(2), remembered)], switcher.Placed); // when it showed, and once more for late movers

        // Review 24 R3: a member is skipped only when it shows a window, not because some process of its program exists.
        await viewModel.StartWorkspaceAsync("Work");
        Assert.Single(_launcher.Launches); // only moved
        Assert.Equal(4, switcher.Placed.Count);

        // The Multi Launch group goes the same way and still starts a member without a window, as before 0.10.0.
        await viewModel.LaunchMultiLaunchGroupAsync();
        Assert.Contains(_launcher.Launches, l => l.Location == @"C:\a.exe");
        var launchedSoFar = _launcher.Launches.Count;

        viewModel.ToggleWorkspace(b, "Work");
        Assert.Equal([DesktopIconGrid.Core.Launching.Workspaces.MultiLaunch], viewModel.WorkspaceNames);
        var noticesBefore = dialogs.Notices.Count;
        await viewModel.StartWorkspaceAsync("Work"); // gone: said, nothing started
        Assert.Equal(launchedSoFar, _launcher.Launches.Count);
        Assert.Equal(noticesBefore + 1, dialogs.Notices.Count);
    }

    [Fact]
    public async Task Members_on_one_program_get_a_window_each_and_one_with_parameters_counts_only_what_the_bar_started_for_it()
    {
        var switcher = new FakeSwitcher { Answer = false };
        var (viewModel, model, _) = Create(switcher);
        viewModel.WorkspaceStep = TimeSpan.FromMilliseconds(5);
        viewModel.WorkspaceWindowWait = TimeSpan.FromMilliseconds(300);
        _launcher.Result = LaunchResult.Started;
        var editor = Program("editor.exe");
        var other = Program("other.exe");
        var notes = (ShortcutItem)model.Items[0];
        var todo = (ShortcutItem)model.Items[1];
        var tool = (ShortcutItem)model.Items[2];
        (notes.Location, notes.Parameters) = (editor, "notes.txt");
        (todo.Location, todo.Parameters) = (editor, "todo.txt");
        tool.Location = other;
        foreach (var member in new[] { notes, todo, tool })
        {
            viewModel.Tracker.Watch(member);
            DesktopIconGrid.Core.Launching.Workspaces.SetMember(member, "Work", true);
        }

        // The editor runs twice: process 11 the bar started for "notes" (window 2, on top), process 10 the user opened
        // with another file (window 1). The tool shows window 5.
        viewModel.Tracker.ApplySnapshot(Running((editor, [10, 11]), (other, [30])));
        viewModel.Tracker.Remember(notes.Id, 11);
        switcher.Shown.AddRange([FakeSwitcher.Window(2, 11), FakeSwitcher.Window(1, 10), FakeSwitcher.Window(5, 30)]);
        var notesAt = new SavedWindow(600, 0, 500, 500, false);
        var todoAt = new SavedWindow(0, 0, 500, 500, false);
        (switcher.Positions[new IntPtr(2)], switcher.Positions[new IntPtr(1)], switcher.Positions[new IntPtr(5)]) = (notesAt, todoAt, new SavedWindow(0, 600, 300, 300, false));

        // W3: one window each; "notes" its own although window 1 is of the same program. "todo", which the bar did not
        // start, falls back to the window no other member has.
        Assert.Equal(3, viewModel.CaptureWorkspace("Work"));

        // The tool is closed. Started, "todo" opens in the editor's running process (10) as a new window (3).
        switcher.Shown.RemoveAt(2);
        viewModel.Tracker.ApplySnapshot(Running((editor, [10, 11])));
        switcher.Coming.Add((FakeSwitcher.Window(3, 10), 2)); // the start's first look is the one before anything is started
        var timer = System.Diagnostics.Stopwatch.StartNew();
        await viewModel.StartWorkspaceAsync("Work");

        // L5: "notes" shows a window of what the bar started for it, so it is only moved. "todo" has parameters and the
        // bar started nothing for it: the user's window 1 does not make it run, so it is started.
        Assert.Equal([(editor, false), (other, false)], _launcher.Launches);
        // "notes" moved at once, "todo" onto the new window and not the user's, both once more for late movers.
        Assert.Equal([(2, notesAt), (3, todoAt), (2, notesAt), (3, todoAt)], switcher.Placed.Select(p => ((int)p.Window, p.Target)));
        // The tool never shows a window: its position is given up when the wait is over, and the start still ends.
        Assert.True(timer.Elapsed >= viewModel.WorkspaceWindowWait);
    }

    /// <summary>One workspace member on "app.exe" with a remembered position, its window gone and a process 41 waiting for one.</summary>
    private (LauncherViewModel ViewModel, FakeSwitcher Switcher, SavedWindow Remembered) OneMemberWorkspace(bool running)
    {
        var switcher = new FakeSwitcher { Answer = false };
        var (viewModel, model, _) = Create(switcher);
        viewModel.WorkspaceStep = TimeSpan.FromMilliseconds(5);
        viewModel.WorkspaceWindowWait = TimeSpan.FromSeconds(2);
        _launcher.Result = LaunchResult.Started;
        var exe = Program("app.exe");
        var member = (ShortcutItem)model.Items[0];
        member.Location = exe;
        viewModel.Tracker.Watch(member);
        DesktopIconGrid.Core.Launching.Workspaces.SetMember(member, "Work", true);
        var remembered = new SavedWindow(100, 100, 700, 500, false);
        switcher.Shown.Add(FakeSwitcher.Window(1, 40));
        switcher.Positions[new IntPtr(1)] = remembered;
        viewModel.Tracker.ApplySnapshot(Running((exe, [40])));
        Assert.Equal(1, viewModel.CaptureWorkspace("Work"));
        if (!running)
        {
            switcher.Shown.Clear();
            viewModel.Tracker.ApplySnapshot(Running((exe, [41])));
        }

        return (viewModel, switcher, remembered);
    }

    /// <summary>Review 27 W3, and the splash-then-main case through the bar's own loop (owed from WP7).</summary>
    [Fact]
    public async Task A_member_whose_window_closes_while_it_starts_takes_the_next_one_and_is_placed_again()
    {
        var (viewModel, switcher, _) = OneMemberWorkspace(running: false);

        // A splash screen with a title bar (window 2) shows at the first look after the start and closes at the next,
        // when the main window (3) shows. The start's own first look is the one before anything is started.
        var start = switcher.Looks; // the capture looked already
        switcher.OnLook = look =>
        {
            if (look == start + 2)
            {
                switcher.Shown.Insert(0, FakeSwitcher.Window(2, 41));
            }
            else if (look == start + 3)
            {
                switcher.Shown.RemoveAll(window => window.Handle == new IntPtr(2));
                switcher.Shown.Insert(0, FakeSwitcher.Window(3, 41));
            }
        };
        await viewModel.StartWorkspaceAsync("Work");

        var order = switcher.Placed.Select(p => (int)p.Window).ToList();
        Assert.Equal(2, order[0]); // the splash, as it was the window then
        Assert.Contains(3, order); // then the main window
        Assert.Equal(3, order[^1]); // last, the main window's late pass
        Assert.True(order.LastIndexOf(2) < order.IndexOf(3)); // the splash is never placed again after the main window
    }

    /// <summary>Review 27 W2: once more 2 s after the member's own placement, but not over the user's minimise.</summary>
    [Fact]
    public async Task The_late_pass_leaves_alone_a_window_the_user_minimised_meanwhile()
    {
        var (viewModel, switcher, remembered) = OneMemberWorkspace(running: true);
        var start = switcher.Looks;
        switcher.OnLook = look =>
        {
            if (look == start + 2) // after it was placed at once
            {
                switcher.Shown[0] = switcher.Shown[0] with { IsMinimized = true };
            }
        };

        await viewModel.StartWorkspaceAsync("Work");

        Assert.Equal([(new IntPtr(1), remembered)], switcher.Placed); // placed at once; the late pass was skipped
        Assert.Empty(_launcher.Launches); // it runs: only moved
    }

    [Fact]
    public void The_command_line_adds_into_a_named_group_once_and_launches_by_name()
    {
        var (viewModel, _, dialogs) = Create();

        viewModel.AddPathsFromCommandLine([@"C:\d.exe", @"C:\e.exe"], " tools ");
        viewModel.AddPathsFromCommandLine([@"C:\f.exe"], "Tools"); // the same group, any case
        viewModel.AddPathsFromCommandLine([@"C:\g.exe"], null);

        var group = Assert.Single(viewModel.Model.Items.OfType<GroupItem>());
        Assert.Equal("tools", group.Name);
        Assert.Equal(["d", "e", "f"], Names(group.Items));
        Assert.Equal(["A", "B", "C", "tools", "g"], Names(viewModel.Model.Items));

        Assert.True(viewModel.LaunchByName("b"));
        Assert.Contains(_launcher.Launches, l => l.Location == @"C:\b.exe");
        var before = dialogs.Notices.Count; // launching may report on the fake launch itself
        Assert.False(viewModel.LaunchByName("Nothing"));
        Assert.Equal(before + 1, dialogs.Notices.Count);
    }

    [Fact]
    public void Import_puts_the_ticked_items_after_the_icon_or_one_group_per_source_on_the_bar()
    {
        var (viewModel, model, dialogs) = Create();
        ImportCandidate ubuntu = new(ImportSource.WindowsTerminal, "Ubuntu", "wt.exe", "-p \"Ubuntu\"");
        ImportCandidate web = new(ImportSource.Putty, "web", @"C:\PuTTY\putty.exe", "-load \"web\"");
        dialogs.ImportAnswer = new ImportRequest([ubuntu, web], GroupPerSource: false);

        viewModel.ImportAfter(viewModel.RootItems.Items[0]);

        Assert.Equal(["A", "Ubuntu", "web", "B", "C"], Names(model.Items));
        Assert.Equal("-p \"Ubuntu\"", ((ShortcutItem)model.Items[1]).Parameters);
        Assert.True(dialogs.ImportCanGroupSeen);

        dialogs.ImportAnswer = new ImportRequest([ubuntu, web], GroupPerSource: true);
        viewModel.Import(viewModel.RootItems, int.MaxValue);
        Assert.Equal([ImportViewModel.SourceName(ImportSource.WindowsTerminal), ImportViewModel.SourceName(ImportSource.Putty)], Names(model.Items).TakeLast(2));
        Assert.Equal(["Ubuntu"], Names(Assert.IsType<GroupItem>(model.Items[^2]).Items));

        dialogs.ImportAnswer = null; // Cancel
        viewModel.Import(viewModel.RootItems, int.MaxValue);
        Assert.Equal(7, model.Items.Count);
    }

    [Fact]
    public void Import_into_a_group_never_makes_a_group_inside_it()
    {
        var (viewModel, model, dialogs) = Create();
        var group = new GroupItem { Name = "G" };
        model.Add(group);
        viewModel.RootItems.Sync();
        var groupList = new ItemListViewModel(viewModel, group);
        dialogs.ImportAnswer = new ImportRequest([new ImportCandidate(ImportSource.Desktop, "Tool", @"C:\Tool\tool.exe")], GroupPerSource: true);

        viewModel.Import(groupList, int.MaxValue);

        Assert.False(dialogs.ImportCanGroupSeen);
        Assert.Equal(["Tool"], Names(group.Items));
    }

    [Fact]
    public async Task Import_marks_what_is_on_the_bar_and_ticks_none_of_it_with_select_all()
    {
        var choices = new ImportViewModel(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "WT.EXE -p \"Ubuntu\"" }, canGroup: false);
        await choices.LoadAsync(() => Task.FromResult<IReadOnlyList<ImportCandidate>?>(
        [
            new(ImportSource.WindowsTerminal, "Ubuntu", "wt.exe", "-p \"Ubuntu\""),
            new(ImportSource.WindowsTerminal, "PowerShell", "wt.exe", "-p \"PowerShell\""),
            new(ImportSource.WindowsTerminal, "PowerShell", "wt.exe", "-p \"PowerShell\""), // listed twice: offered once
        ]), icon: null, CancellationToken.None);

        choices.SelectAllCommand.Execute(null);
        choices.GroupPerSource = true;

        Assert.Equal([true, false], choices.Choices.Select(c => c.IsOnBar));
        Assert.Equal(["PowerShell"], choices.Chosen.Select(c => c.Name));
        Assert.False(choices.GroupPerSource); // only on the bar itself
    }

    [Fact]
    public async Task Import_says_so_when_the_search_ran_out_of_time()
    {
        // Review 19 I5: a timeout read as "Nothing to import was found".
        var choices = new ImportViewModel(new HashSet<string>(), canGroup: true);

        await choices.LoadAsync(() => Task.FromResult<IReadOnlyList<ImportCandidate>?>(null), icon: null, CancellationToken.None);

        Assert.Equal(Strings.Import_TimedOut, choices.Status);
        Assert.Empty(choices.Choices);
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

