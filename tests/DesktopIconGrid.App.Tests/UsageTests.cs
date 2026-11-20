using System.ComponentModel;
using System.IO;
using System.Windows.Threading;
using DesktopIconGrid.Core;
using DesktopIconGrid.Core.Launching;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Core.SpecialIcons;
using DesktopIconGrid.Core.Storage;
using DesktopIconGrid.Resources;
using DesktopIconGrid.Services;
using DesktopIconGrid.SpecialIcons;
using DesktopIconGrid.ViewModels;

namespace DesktopIconGrid.App.Tests;

/// <summary>0.12.0 WP4 (U1, U3, U4): what the bar counts as a use, and the names table's usage columns.</summary>
[Collection("Shared icon worker")]
public sealed class UsageTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("dig-usage-").FullName;
    private readonly RecordingLauncher _launcher = new();

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    /// <summary>A special icon whose click starts something: still no count.</summary>
    private sealed class StartingTile : ISpecialIconHandler
    {
        public string Kind => "starting-tile";

        public string DisplayName => Kind;

        public string Description => Kind;

        public SpecialIconItem CreateItem() => new() { Name = "Tile", Kind = Kind };

        public GaugeState? CreateState(SpecialIconItem item) => null;

        public LaunchResult Activate(SpecialIconItem item, ActivationModifiers modifiers) => LaunchResult.Started;

        public bool ShowProperties(SpecialIconItem item, bool isNew) => false;
    }

    /// <summary>Shortcuts A, B and C, a group with D, a folder stack and a special icon.</summary>
    private (LauncherViewModel ViewModel, LauncherModel Model, RecordingDialogs Dialogs) Create(IWindowSwitcher? windows = null)
    {
        var model = new LauncherModel();
        model.Add(new ShortcutItem { Name = "A", Location = @"C:\a.exe" });
        model.Add(new ShortcutItem { Name = "B", Location = @"C:\b.exe" });
        model.Add(new ShortcutItem { Name = "C", Location = @"C:\c.exe" });
        var group = new GroupItem { Name = "Tools" };
        group.Items.Add(new ShortcutItem { Name = "D", Location = @"C:\d.exe" });
        model.Add(group);
        model.Add(new ShortcutItem { Name = "Stack", Location = _directory, OpenAsStack = true });
        var tile = new StartingTile();
        model.Add(tile.CreateItem());
        var dialogs = new RecordingDialogs();
        var viewModel = new LauncherViewModel(
            model,
            new LayoutStorage(Path.Combine(_directory, "layout.json")),
            new NoIcons(),
            dialogs,
            _launcher,
            new ProcessTracker(Dispatcher.CurrentDispatcher, Path.Combine(_directory, "logs")),
            new SpecialIconRegistry([tile]),
            action => action(),
            windows,
            _ => false);
        return (viewModel, model, dialogs);
    }

    private static ItemViewModel Item(LauncherViewModel viewModel, string name) =>
        viewModel.RootItems.Items.Single(item => item.Name == name);

    private static int Uses(LauncherViewModel viewModel, LauncherItem item) => viewModel.Usage.Of(item.Id)?.Count ?? 0;

    [Fact]
    public void A_click_counts_once_when_the_shortcut_starts_and_never_when_it_fails_or_is_cancelled()
    {
        var (viewModel, model, _) = Create();
        _launcher.Result = LaunchResult.Started;
        viewModel.Activate(Item(viewModel, "A"), anchor: null);
        viewModel.Activate(Item(viewModel, "A"), anchor: null); // a natural double-click: one start
        Assert.Equal(1, Uses(viewModel, model.Items[0]));

        _launcher.Result = LaunchResult.Failure("gone");
        viewModel.Activate(Item(viewModel, "B"), anchor: null);
        _launcher.Result = LaunchResult.Cancelled; // an approval or a question cancelled
        viewModel.Activate(Item(viewModel, "C"), anchor: null);
        Assert.Equal((0, 0), (Uses(viewModel, model.Items[1]), Uses(viewModel, model.Items[2])));
    }

    [Fact]
    public void A_window_brought_forward_and_a_folder_stack_opened_count_but_a_program_opened_inside_the_stack_does_not()
    {
        var (viewModel, model, dialogs) = Create(new FakeSwitcher { Answer = true });
        _launcher.Result = LaunchResult.Failure("not started: brought forward instead");

        viewModel.Activate(Item(viewModel, "A"), anchor: null);
        Assert.Empty(_launcher.Launches);
        Assert.Equal(1, Uses(viewModel, model.Items[0]));

        var stack = model.Items.Single(item => item.Name == "Stack");
        viewModel.Activate(Item(viewModel, "Stack"), anchor: null);
        Assert.Single(dialogs.Stacks);
        Assert.Equal(1, Uses(viewModel, stack));

        _launcher.WithResult = LaunchResult.Started;
        dialogs.StackOpenWith!(@"C:\Tools\viewer.exe", [@"C:\x.txt"]);
        Assert.Equal(1, Uses(viewModel, stack));
        Assert.Equal(2, viewModel.Usage.Snapshot().Count); // A and the stack: nothing for the program inside it
    }

    [Fact]
    public void A_click_that_minimises_the_program_in_front_is_no_use_and_says_so()
    {
        // 0.12.2, review 29 N2: a second click on a running program whose window was in front minimised it, nothing
        // was started, and Uses went up by one.
        var switcher = new FakeSwitcher { Kind = SwitchKind.Minimize };
        var (viewModel, model, _) = Create(switcher);

        Assert.Equal(LaunchOutcome.Minimized, viewModel.ActivateAndReport(Item(viewModel, "A"), anchor: null, ActivationModifiers.None));
        Assert.Equal(1, switcher.Calls);
        Assert.Empty(_launcher.Launches);
        Assert.Equal(0, Uses(viewModel, model.Items[0]));

        switcher.Kind = SwitchKind.Activate; // the next click brings it forward: that is a use
        Assert.Equal(LaunchOutcome.Started, viewModel.ActivateAndReport(Item(viewModel, "A"), anchor: null, ActivationModifiers.None));
        Assert.Equal(1, Uses(viewModel, model.Items[0]));
    }

    [Fact]
    public void A_drop_counts_once_whatever_the_number_of_files_and_launch_by_name_counts_like_a_click()
    {
        var (viewModel, model, _) = Create();
        _launcher.WithResult = LaunchResult.Started;
        viewModel.OpenWith(Item(viewModel, "B"), [@"C:\1.txt", @"C:\2.txt", @"C:\3.txt"]);
        Assert.Equal(1, Uses(viewModel, model.Items[1]));

        _launcher.WithResult = LaunchResult.Failure("refused");
        viewModel.OpenWith(Item(viewModel, "B"), [@"C:\4.txt"]);
        Assert.Equal(1, Uses(viewModel, model.Items[1]));

        _launcher.Result = LaunchResult.Started;
        Assert.True(viewModel.LaunchByName("C")); // --launch, and voice, go the way of a click
        Assert.Equal(1, Uses(viewModel, model.Items[2]));
    }

    [Fact]
    public async Task Each_member_a_workspace_or_Multi_Launch_starts_counts_once()
    {
        var (viewModel, model, _) = Create();
        viewModel.WorkspaceStep = TimeSpan.FromMilliseconds(5);
        ((ShortcutItem)model.Items[0]).MultiLaunch = true;
        ((ShortcutItem)model.Items[2]).MultiLaunch = true;
        _launcher.Result = LaunchResult.Started;

        await viewModel.LaunchMultiLaunchGroupAsync();

        Assert.Equal((1, 0, 1), (Uses(viewModel, model.Items[0]), Uses(viewModel, model.Items[1]), Uses(viewModel, model.Items[2])));
    }

    [Fact]
    public void Special_icons_and_groups_have_no_count()
    {
        var (viewModel, model, _) = Create();
        viewModel.Activate(Item(viewModel, "Tile"), anchor: null);
        viewModel.Activate(Item(viewModel, "Tools"), anchor: null);

        Assert.Empty(viewModel.Usage.Snapshot());
        Assert.Equal(0, Uses(viewModel, model.Items.Single(item => item is SpecialIconItem)));
    }

    [Fact]
    public void Every_date_of_the_names_table_is_in_Windows_regional_format_whatever_the_interface_language()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        // Review 28 A3: an English bar on a zh-TW Windows showed 2026/11/18 beside 11/19/2026 12:33 AM.
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        var uiCulture = System.Globalization.CultureInfo.CurrentUICulture;
        var taiwan = System.Globalization.CultureInfo.GetCultureInfo("zh-TW");
        System.Globalization.CultureInfo.CurrentCulture = taiwan;
        System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo("en-US");
        try
        {
            var now = DateTimeOffset.UtcNow;
            var usage = new UsageLog(null, () => now.AddDays(-45));
            var (viewModel, model, _) = Create();
            viewModel.UseUsage(usage);
            usage.Count(model.Items[0].Id); // A: 45 days ago, so its date is shown
            var modified = new DateTimeOffset(2026, 11, 18, 16, 33, 0, TimeSpan.Zero);
            model.Items[0].ModifiedAt = modified;

            var row = new NameTableViewModel(viewModel, now).Rows.Single(row => row.InitialName == "A");

            Assert.Equal(modified.LocalDateTime.ToString("g", taiwan), row.ModifiedText);
            Assert.Equal(now.AddDays(-45).LocalDateTime.ToString("d", taiwan), row.LastUsedText);
            Assert.NotEqual(modified.LocalDateTime.ToString("g", System.Globalization.CultureInfo.GetCultureInfo("en-US")), row.ModifiedText);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = culture;
            System.Globalization.CultureInfo.CurrentUICulture = uiCulture;
        }
    }

    [Fact]
    public void The_usage_columns_sort_as_numbers_and_times_and_never_used_comes_before_any_date()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var clock = now.AddDays(-200);
        var usage = new UsageLog(null, () => clock);
        var (viewModel, model, _) = Create();
        viewModel.UseUsage(usage);
        usage.Count(model.Items[1].Id); // B: once, 200 days ago
        clock = now.AddDays(-3);
        for (var i = 0; i < 12; i++)
        {
            usage.Count(model.Items[0].Id); // A: 12 uses, the last 3 days ago
        }

        var table = new NameTableViewModel(viewModel, now);
        var shortcuts = table.Rows.Where(row => row.Uses is not null).ToList();
        Assert.Equal(["A", "B", "C", "D", "Stack"], shortcuts.Select(row => row.InitialName).Order());

        table.RowsView.SortDescriptions.Add(new SortDescription(nameof(NameRowViewModel.Uses), ListSortDirection.Descending));
        Assert.Equal(["A", "B"], table.RowsView.Cast<NameRowViewModel>().Take(2).Select(row => row.InitialName));
        Assert.Equal(12, table.Rows.Single(row => row.InitialName == "A").Uses);
        Assert.Equal(0, table.Rows.Single(row => row.InitialName == "C").Uses); // written "0"
        Assert.Null(table.Rows.Single(row => row.InitialName == "Tools").Uses); // a group: an empty cell

        table.RowsView.SortDescriptions.Clear();
        table.RowsView.SortDescriptions.Add(new SortDescription(nameof(NameRowViewModel.LastUsed), ListSortDirection.Descending));
        var byLastUse = table.RowsView.Cast<NameRowViewModel>().Where(row => row.Uses is not null).Select(row => row.InitialName).ToList();
        Assert.Equal(["A", "B"], byLastUse.Take(2));
        Assert.Equal(string.Format(System.Globalization.CultureInfo.CurrentCulture, Strings.Names_DaysAgo, 3), table.Rows.Single(row => row.InitialName == "A").LastUsedText);
        Assert.Equal("—", table.Rows.Single(row => row.InitialName == "C").LastUsedText);
        Assert.Null(table.Rows.Single(row => row.InitialName == "C").LastUsedToolTip);
        Assert.NotNull(table.Rows.Single(row => row.InitialName == "B").LastUsedToolTip);
    }

    [Fact]
    public void Not_used_in_90_days_and_the_line_under_the_table_follow_how_long_this_PC_has_counted()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var (viewModel, model, _) = Create();

        // Counting began 30 days ago: a shortcut never used is not listed yet, and the line says when it will be.
        var clock = now.AddDays(-30);
        var young = new UsageLog(null, () => clock);
        viewModel.UseUsage(young);
        var early = new NameTableViewModel(viewModel, now);
        early.SelectedTagFilter = early.TagFilters.Single(filter => filter.Tag == NameTableViewModel.NotUsed90);
        Assert.Empty(early.RowsView.Cast<NameRowViewModel>());
        Assert.Contains(UsageRules.CompleteFrom(now.AddDays(-30)).ToLocalTime().ToString("d", System.Globalization.CultureInfo.CurrentCulture), early.UsageLine, StringComparison.Ordinal);
        Assert.Equal(early.TagFilters[^1], early.SelectedTagFilter); // the last entry of Show

        // Counting began 120 days ago: B used 100 days ago and the never-used ones are listed, A used lately is not.
        clock = now.AddDays(-120);
        var old = new UsageLog(null, () => clock);
        viewModel.UseUsage(old);
        clock = now.AddDays(-100);
        old.Count(model.Items[1].Id);
        clock = now.AddDays(-2);
        old.Count(model.Items[0].Id);
        var later = new NameTableViewModel(viewModel, now);
        later.SelectedTagFilter = later.TagFilters.Single(filter => filter.Tag == NameTableViewModel.NotUsed90);
        Assert.Equal(["B", "C", "D", "Stack"], later.RowsView.Cast<NameRowViewModel>().Select(row => row.InitialName).Order());
        Assert.Equal(string.Format(Strings.Names_UsageSince, now.AddDays(-120).ToLocalTime().ToString("d", System.Globalization.CultureInfo.CurrentCulture)), later.UsageLine);
    }

    [Fact]
    public async Task The_saver_writes_at_most_once_per_interval_and_at_exit_off_the_bar_thread()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var file = Path.Combine(_directory, UsageLog.FileName);
        var log = new UsageLog(file, thisPc: () => "test-pc"); // not ThisDevice: other tests may set it meanwhile
        log.Load();
        var id = Guid.NewGuid();
        var interval = UsageSaver.Interval;
        UsageSaver.Interval = TimeSpan.FromSeconds(30);
        try
        {
            var reads = 0;
            var saver = new UsageSaver(log, () => new HashSet<Guid> { id }, read => { reads++; return Task.FromResult(read()); });
            log.Count(id); // the first write comes at once
            await WaitForAsync(() => File.Exists(file));
            var first = File.GetLastWriteTimeUtc(file);

            log.Count(id); // within the interval: it waits
            await Task.Delay(500);
            Assert.Equal(first, File.GetLastWriteTimeUtc(file));
            Assert.Equal(1, reads);

            await saver.FlushAsync(); // at exit
            var again = new UsageLog(file, thisPc: () => "test-pc");
            again.Load();
            Assert.Equal(2, again.Of(id)!.Count);
        }
        finally
        {
            UsageSaver.Interval = interval;
        }
    }

    [Fact]
    public void The_wait_before_a_write_is_on_a_clock_that_only_goes_forward_and_never_longer_than_the_interval()
    {
        // Review 28 U3: the wall clock set back by J made the next write wait J + 10 s, and past 49.7 days
        // Task.Delay threw and nothing more was written that session.
        var interval = TimeSpan.FromSeconds(10);
        Assert.Equal(TimeSpan.Zero, UsageSaver.WaitBefore(null, 1_000, interval)); // the first write comes at once
        Assert.Equal(TimeSpan.FromSeconds(7), UsageSaver.WaitBefore(1_000, 4_000, interval));
        Assert.Equal(TimeSpan.Zero, UsageSaver.WaitBefore(1_000, 60_000, interval));
        Assert.Equal(TimeSpan.Zero, UsageSaver.WaitBefore(0, (long)TimeSpan.FromDays(60).TotalMilliseconds, interval));
        Assert.Equal(interval, UsageSaver.WaitBefore(5_000, 1_000, interval)); // never more than the interval
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++)
        {
            await Task.Delay(50);
        }

        Assert.True(condition());
    }
}
