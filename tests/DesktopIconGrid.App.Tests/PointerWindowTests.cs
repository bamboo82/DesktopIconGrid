using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using DesktopIconGrid.Core;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.ViewModels;
using DesktopIconGrid.Views;

namespace DesktopIconGrid.App.Tests;

/// <summary>
/// The Show-at-pointer window built from its markup, on the shared WPF thread: what Alt+F4 does to it, and whether
/// its size, measured from that markup, shows every row (with a group's header, lines and captions) and nothing more.
/// </summary>
[Collection(WpfWindowsCollection.Name)]
public sealed class PointerWindowTests(WpfThread wpf) : IDisposable
{
    private const int WmSysCommand = 0x0112;
    private const int ScClose = 0xF060;

    private readonly string _directory = Directory.CreateTempSubdirectory("dig-pointer-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private static ShortcutItem Shortcut(int number) => new() { Name = $"Item {number}", Location = $"C:\\item{number}.exe" };

    private static LauncherModel Shortcuts(int count)
    {
        var model = new LauncherModel();
        for (var i = 0; i < count; i++)
        {
            model.Add(Shortcut(i));
        }

        return model;
    }

    private TestBar CreateBar(int shortcuts) => new(_directory, Shortcuts(shortcuts));

    /// <summary>Shows the bar's icons at the pointer, runs <paramref name="check"/> on the shown window, then closes it.</summary>
    private void WithWindow(LauncherModel model, Action<PointerWindow, LauncherViewModel> check)
    {
        wpf.Invoke(() =>
        {
            using var bar = new TestBar(_directory, model);
            var window = new PointerWindow(bar.ViewModel);
            try
            {
                window.ShowAtPointer();
                window.UpdateLayout();
                check(window, bar.ViewModel);
            }
            finally
            {
                window.CloseForGood();
            }
        });
    }

    /// <summary>Every row shows in full: no scroll bar, nothing below the bottom edge, and no empty row under the last.</summary>
    private static void EveryRowShows(PointerWindow window, double cellHeight)
    {
        var scroller = window.Icons.Scroller;
        var rows = $"rows {scroller.ExtentHeight} high in a view {scroller.ViewportHeight} high";
        Assert.Equal(Visibility.Collapsed, scroller.ComputedVerticalScrollBarVisibility);
        Assert.True(scroller.ExtentHeight <= scroller.ViewportHeight + 0.5, rows);
        Assert.True(scroller.ViewportHeight - scroller.ExtentHeight < cellHeight / 2, $"an empty row: {rows}");
    }

    [Fact]
    public void Alt_F4_hides_the_window_and_the_hotkey_shows_it_again()
    {
        wpf.Invoke(() =>
        {
            using var bar = CreateBar(4);
            var window = new PointerWindow(bar.ViewModel);
            var handle = IntPtr.Zero;
            try
            {
                window.ShowAtPointer();
                handle = new WindowInteropHelper(window).Handle;
                Assert.True(window.IsVisible);

                SendMessage(handle, WmSysCommand, ScClose, IntPtr.Zero); // what Alt+F4 sends

                Assert.False(window.IsVisible);
                Assert.True(IsWindow(handle));
                window.ShowAtPointer(); // in 0.7.0 the window was closed by now, and this threw
                Assert.True(window.IsVisible);
            }
            finally
            {
                window.CloseForGood();
            }

            Assert.False(IsWindow(handle));
        });
    }

    [Fact]
    public void An_opened_group_gets_room_for_its_header_and_every_row_and_a_search_goes_back()
    {
        // The screen pass: 11 items at the top level; the group of 7 opened in place lost its second row to its header.
        var model = Shortcuts(10);
        var tools = new GroupItem { Name = "Tools" };
        model.Add(tools);
        for (var i = 0; i < 7; i++)
        {
            model.Add(Shortcut(100 + i), tools);
        }

        WithWindow(model, (window, bar) =>
        {
            EveryRowShows(window, bar.CellHeight);
            var topLevel = window.ActualHeight;

            window.OpenGroup(bar.RootItems.Items.Single(item => item.Children is not null));
            window.UpdateLayout();

            EveryRowShows(window, bar.CellHeight);
            Assert.True(window.ActualHeight > topLevel, "no room was made for the group's header");

            bar.SearchText = "Item 1"; // a search covers every group, so it goes back to the top level
            window.UpdateLayout();

            Assert.Null(window.OpenedGroup);
            Assert.Equal(topLevel, window.ActualHeight, precision: 0);
        });
    }

    [Fact]
    public void Lines_and_captions_take_the_room_they_take_and_every_row_shows()
    {
        // 6 icons, a line, a caption, 6 icons: 0.7.0 counted 14 cells.
        var model = Shortcuts(6);
        model.Add(new SeparatorItem { Kind = SeparatorItem.Line });
        model.Add(new SeparatorItem { Kind = SeparatorItem.Label, Name = "Work in progress" });
        for (var i = 6; i < 12; i++)
        {
            model.Add(Shortcut(i));
        }

        WithWindow(model, (window, bar) => EveryRowShows(window, bar.CellHeight));
    }

    [Fact]
    public void A_few_icons_make_one_row_at_least_three_cells_wide()
    {
        WithWindow(Shortcuts(2), (window, bar) =>
        {
            EveryRowShows(window, bar.CellHeight);
            Assert.Equal(PointerWindow.MinColumns * bar.CellWidth, window.Icons.LayoutPanel!.ActualWidth, precision: 0);
        });
    }

    [Fact]
    public void More_than_five_rows_scroll_and_the_scroll_bar_takes_no_column()
    {
        WithWindow(Shortcuts(40), (window, bar) =>
        {
            var scroller = window.Icons.Scroller;
            Assert.Equal(Visibility.Visible, scroller.ComputedVerticalScrollBarVisibility);
            Assert.InRange(scroller.ViewportHeight, PointerWindow.MaxRows * bar.CellHeight, (PointerWindow.MaxRows + 0.5) * bar.CellHeight);
            Assert.True(window.Icons.LayoutPanel!.ActualWidth >= PointerWindow.MaxColumns * bar.CellWidth - 0.5, "the scroll bar took a column");
        });
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr hWnd);
}
