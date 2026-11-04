using System.IO;
using System.Windows.Media;
using System.Windows.Threading;
using DesktopIconGrid.Core;
using DesktopIconGrid.Core.Launching;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Core.Storage;
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
        public LaunchResult Launch(ShortcutItem shortcut) => LaunchResult.Failure("nope");

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

    private (LauncherViewModel ViewModel, LauncherModel Model, RecordingDialogs Dialogs) Create()
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
            new FailingLauncher(),
            tracker,
            new SpecialIconRegistry([]),
            action => action());
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
        LauncherViewModel.UndoWindow = TimeSpan.FromMilliseconds(100);
        try
        {
            var (viewModel, model, _) = Create();
            viewModel.RemoveItem(viewModel.RootItems.Items[0]);
            Assert.True(viewModel.CanUndoRemove);

            await Task.Delay(600);

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

        Assert.Equal(["Remove group"], dialogs.Confirmations);
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
}
