using System.IO;
using System.Windows.Media;
using System.Windows.Threading;
using DesktopIconGrid.Core;
using DesktopIconGrid.Core.Launching;
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
    }

    private sealed class FailingLauncher : ILaunchService
    {
        public List<(string Location, bool AsAdministrator)> Launches { get; } = [];

        public LaunchResult Launch(ShortcutItem shortcut, bool asAdministrator = false)
        {
            Launches.Add((shortcut.Location, asAdministrator));
            return LaunchResult.Failure("nope");
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

        public bool TrySwitch(IReadOnlySet<int> processIds)
        {
            Calls++;
            return Answer;
        }
    }

    private readonly FailingLauncher _launcher = new();

    private (LauncherViewModel ViewModel, LauncherModel Model, RecordingDialogs Dialogs) Create(IWindowSwitcher? windows = null)
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
            windows);
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
}
