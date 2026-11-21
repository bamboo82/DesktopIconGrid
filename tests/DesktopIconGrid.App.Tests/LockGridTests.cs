using System.IO;
using System.Windows;
using MenuItem = System.Windows.Controls.MenuItem;
using DesktopIconGrid.Core;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Resources;
using DesktopIconGrid.ViewModels;
using DesktopIconGrid.Views;

namespace DesktopIconGrid.App.Tests;

/// <summary>
/// 0.15.0 WP1, 鎖定格線: while it is on nothing on the grid moves by dragging, files from elsewhere are still taken, and the
/// first refused drag in a run says how to unlock it.
/// </summary>
[Collection(WpfWindowsCollection.Name)]
public sealed class LockGridTests(WpfThread wpf) : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("dig-lock-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    /// <summary>A shortcut, a group holding one, a special icon and one separator of each kind.</summary>
    private static LauncherModel EveryKind()
    {
        var model = new LauncherModel();
        model.Add(new ShortcutItem { Name = "Editor", Location = @"C:\Tools\editor.exe" });
        var group = new GroupItem { Name = "Tools" };
        model.Add(group);
        model.Add(new ShortcutItem { Name = "Inner", Location = @"C:\Tools\inner.exe" }, group);
        model.Add(new SpecialIconItem { Kind = "voice", Name = "Voice" });
        model.Add(new SeparatorItem { Kind = SeparatorItem.Line });
        model.Add(new SeparatorItem { Kind = SeparatorItem.RowHead });
        model.Add(new SeparatorItem { Kind = SeparatorItem.RowLine, Name = "Row" });
        model.Add(new SeparatorItem { Kind = SeparatorItem.Gap });
        model.Add(new SeparatorItem { Kind = SeparatorItem.Label, Name = "Caption" });
        return model;
    }

    private static LauncherModel Three()
    {
        var model = new LauncherModel();
        model.Add(new ShortcutItem { Name = "A", Location = @"C:\a.exe" });
        model.Add(new ShortcutItem { Name = "B", Location = @"C:\b.exe" });
        model.Add(new ShortcutItem { Name = "C", Location = @"C:\c.exe" });
        return model;
    }

    private static List<string> Names(IEnumerable<LauncherItem> items) => items.Select(item => item.Name).ToList();

    [Fact]
    public async Task No_item_of_any_kind_may_be_dragged_while_locked_and_the_first_refusal_alone_says_so()
    {
        var previous = LauncherViewModel.BarMessageWindow;
        LauncherViewModel.BarMessageWindow = TimeSpan.FromMilliseconds(50);
        try
        {
            using var bar = new TestBar(_directory, EveryKind());
            var viewModel = bar.ViewModel;
            var group = viewModel.RootItems.Items.Single(item => item.IsGroup);
            var items = viewModel.RootItems.Items.Concat(group.Children!.Items).ToList();
            Assert.Equal(9, items.Count);

            Assert.All(items, item => Assert.True(IconGridControl.MayMove(item), item.Name));
            Assert.False(viewModel.HasBarMessage); // unlocked: nothing to say

            viewModel.LockGrid = true;
            Assert.True(bar.Model.Settings.LockGrid);
            Assert.False(IconGridControl.MayMove(items[0]));
            Assert.Equal(Strings.Main_GridLocked, viewModel.BarMessage);
            Assert.All(items, item => Assert.False(IconGridControl.MayMove(item), item.Name));

            await viewModel.BarMessageExpiry; // the notice row's own timer
            Assert.Null(viewModel.BarMessage);
            Assert.False(IconGridControl.MayMove(items[0]));
            Assert.False(viewModel.HasBarMessage); // later refusals in the run are silent
        }
        finally
        {
            LauncherViewModel.BarMessageWindow = previous;
        }
    }

    [Fact]
    public void Unlocking_takes_the_notice_back_and_items_move_again()
    {
        using var bar = new TestBar(_directory, Three());
        var viewModel = bar.ViewModel;
        var item = viewModel.RootItems.Items[0];
        viewModel.LockGrid = true;
        Assert.False(IconGridControl.MayMove(item));
        Assert.True(viewModel.HasBarMessage);

        viewModel.LockGrid = false;

        Assert.False(viewModel.HasBarMessage);
        Assert.False(bar.Model.Settings.LockGrid);
        Assert.True(IconGridControl.MayMove(item));
    }

    [Fact]
    public void A_locked_grid_takes_files_from_elsewhere_but_moves_none_of_its_items()
    {
        using var bar = new TestBar(_directory, Three());
        var viewModel = bar.ViewModel;
        var list = viewModel.RootItems;
        var c = list.Items[2].Id;
        var atStart = new IconGridControl.DropTarget(DragDropEffects.Move, IconGridControl.DropAction.Insert, 0, null);
        viewModel.LockGrid = true;

        IconGridControl.CompleteDrop(list, atStart, c, []); // the grid was locked while the drag was on its way
        Assert.Equal(["A", "B", "C"], Names(bar.Model.Items));

        IconGridControl.CompleteDrop(list, atStart with { Effects = DragDropEffects.Copy }, null, [@"C:\Tools\new.exe"]);
        Assert.Equal(["new", "A", "B", "C"], Names(bar.Model.Items));

        viewModel.LockGrid = false;
        IconGridControl.CompleteDrop(list, atStart, c, []);
        Assert.Equal(["C", "new", "A", "B"], Names(bar.Model.Items));
    }

    [Fact]
    public void The_empty_areas_menu_shows_the_lock_and_turns_it()
    {
        // The empty area's menu, the same in the bar, a group and the window at the pointer. A context menu takes its
        // owner's data context when it opens; here it is given it, and the bindings settle.
        TestBar? bar = null;
        IconGridControl? grid = null;
        try
        {
            wpf.Invoke(() =>
            {
                bar = new TestBar(_directory, Three());
                grid = new IconGridControl { DataContext = bar.ViewModel.RootItems };
                grid.Scroller.ContextMenu.DataContext = grid.DataContext;
            });
            wpf.Settle();
            wpf.Invoke(() =>
            {
                var viewModel = bar!.ViewModel;
                var item = grid!.Scroller.ContextMenu.Items.OfType<MenuItem>().Single(entry => Equals(entry.Header, Strings.Menu_LockGrid));
                Assert.True(item.IsCheckable);
                Assert.False(item.IsChecked);

                item.IsChecked = true;
                Assert.True(viewModel.LockGrid);

                viewModel.LockGrid = false; // from the bar's ▸ menu, or a sync
                Assert.False(item.IsChecked);
            });
        }
        finally
        {
            wpf.Invoke(() => bar?.Dispose());
        }
    }
}
