using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
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

    /// <summary>
    /// Shows the bar's icons at the pointer, lets the WPF thread settle, runs <paramref name="check"/> on the shown
    /// window and closes it. Made <paramref name="ahead"/>, as the bar does, its bindings are alive before it is shown;
    /// otherwise it is shown in the turn it is made, as when the hotkey comes first.
    /// </summary>
    private void WithWindow(LauncherModel model, Action<PointerWindow, LauncherViewModel> check, bool ahead = true)
    {
        TestBar? bar = null;
        PointerWindow? window = null;
        try
        {
            wpf.Invoke(() =>
            {
                bar = new TestBar(_directory, model);
                window = new PointerWindow(bar.ViewModel);
                if (!ahead)
                {
                    window.ShowAtPointer();
                }
            });
            if (ahead)
            {
                wpf.Settle();
                wpf.Invoke(() => window!.ShowAtPointer());
            }

            wpf.Settle();
            wpf.Invoke(() =>
            {
                window!.UpdateLayout();
                check(window, bar!.ViewModel);
            });
        }
        finally
        {
            wpf.Invoke(() =>
            {
                window?.CloseForGood();
                bar?.Dispose();
            });
        }
    }

    private static string Describe(PointerWindow window)
    {
        var scroller = window.Icons.Scroller;
        return $"window {window.ActualWidth}×{window.ActualHeight}, {window.Icons.ItemsHost.Items.Count} items, "
            + $"rows {scroller.ExtentHeight} high in a view {scroller.ViewportWidth}×{scroller.ViewportHeight}";
    }

    /// <summary>Every row shows in full: no scroll bar, nothing below the bottom edge, and no empty row under the last.</summary>
    private static void EveryRowShows(PointerWindow window, double cellHeight)
    {
        var scroller = window.Icons.Scroller;
        Assert.True(window.Icons.ItemsHost.Items.Count > 0, $"no items: {Describe(window)}");
        Assert.True(scroller.ComputedVerticalScrollBarVisibility == Visibility.Collapsed, $"a scroll bar: {Describe(window)}");
        Assert.True(scroller.ExtentHeight <= scroller.ViewportHeight + 0.5, $"rows cut off: {Describe(window)}");
        Assert.True(scroller.ViewportHeight - scroller.ExtentHeight < cellHeight / 2, $"an empty row: {Describe(window)}");
    }

    private static bool IsWithin(DependencyObject? element, DependencyObject ancestor)
    {
        for (var current = element; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }
        }

        return false;
    }

    [Fact]
    public void A_divider_after_a_full_row_is_seen_and_hit_inside_the_view()
    {
        // Review 15 UI2: the divider of a full row took its room past the row's end, where the scroll viewer clips, so
        // it was as invisible as before with 1 unit to click. The pointer window's rows hold their cells exactly.
        var model = Shortcuts(PointerWindow.MaxColumns);
        model.Add(new SeparatorItem { Kind = SeparatorItem.Line });
        model.Add(Shortcut(99));
        WithWindow(model, (window, _) =>
        {
            var scroller = window.Icons.Scroller;
            var host = window.Icons.ItemsHost;
            var divider = (FrameworkElement)host.ItemContainerGenerator.ContainerFromIndex(PointerWindow.MaxColumns);
            var lastIcon = (FrameworkElement)host.ItemContainerGenerator.ContainerFromIndex(PointerWindow.MaxColumns - 1);
            var middle = divider.TranslatePoint(new Point(divider.ActualWidth / 2, divider.ActualHeight / 2), scroller);

            Assert.True(middle.X > 0 && middle.X < scroller.ViewportWidth, $"the divider's middle is at {middle.X}, outside the view's {scroller.ViewportWidth}: {Describe(window)}");
            var hit = scroller.InputHitTest(middle) as DependencyObject;
            Assert.True(IsWithin(hit, divider), $"a click on the divider's middle at {middle} lands on {hit}");

            var iconMiddle = lastIcon.TranslatePoint(new Point(lastIcon.ActualWidth / 2, lastIcon.ActualHeight / 2), scroller);
            Assert.True(IsWithin(scroller.InputHitTest(iconMiddle) as DependencyObject, lastIcon), "the last icon of the row is no longer hit in its middle");
        });
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
    public void Row_lines_and_row_start_lines_end_their_rows_and_every_row_shows()
    {
        // 0.14.0 WP1: 2 icons, a row line with a caption, a row-start line and 3 icons, a row line, 1 icon. Measured in
        // one row the widest row is the row-start line's (a line and 3 cells), so the first row has room it does not use.
        var model = Shortcuts(2);
        model.Add(new SeparatorItem { Kind = SeparatorItem.RowLine, Name = "Work" });
        model.Add(new SeparatorItem { Kind = SeparatorItem.RowHead });
        for (var i = 2; i < 5; i++)
        {
            model.Add(Shortcut(i));
        }

        model.Add(new SeparatorItem { Kind = SeparatorItem.RowLine });
        model.Add(Shortcut(5));
        WithWindow(model, (window, bar) =>
        {
            EveryRowShows(window, bar.CellHeight);
            var host = window.Icons.ItemsHost;
            Rect BoxOf(int index)
            {
                var container = (FrameworkElement)host.ItemContainerGenerator.ContainerFromIndex(index);
                return container.TransformToAncestor(host).TransformBounds(new Rect(container.RenderSize));
            }

            static void Near(double expected, double actual, string what) =>
                Assert.True(Math.Abs(expected - actual) < 1, $"{what} is {actual}, not {expected}");

            var panelWidth = window.Icons.LayoutPanel!.ActualWidth;
            Near(FlowLayout.NarrowSize + (3 * bar.CellWidth), panelWidth, "the panel's width");
            var captioned = BoxOf(2);
            Near(0, captioned.X, "the captioned row line's left");
            Near(bar.CellHeight, captioned.Y, "the captioned row line's top"); // it ends the first row where it is
            Near(panelWidth, captioned.Width, "the captioned row line's width");
            Assert.True(captioned.Height > FlowLayout.NarrowSize + 1, $"the captioned strip is {captioned.Height} high");

            var head = BoxOf(3);
            Near(0, head.X, "the row-start line's left");
            Near(captioned.Bottom, head.Y, "the row-start line's top");
            Near(FlowLayout.NarrowSize, head.Width, "the row-start line's width");
            Near(FlowLayout.NarrowSize, BoxOf(4).X, "the first icon after the row-start line");

            var plain = BoxOf(7);
            Near(head.Bottom, plain.Y, "the plain row line's top"); // the full row before it makes no empty row
            Near(FlowLayout.NarrowSize, plain.Height, "the plain row line's height");
            Near(0, BoxOf(8).X, "the last icon's left");
            Near(plain.Bottom, BoxOf(8).Y, "the last icon's top");
        });
    }

    [Fact]
    public void Names_and_captions_take_the_windows_text_edge_and_an_opaque_window_has_none()
    {
        // Review 37 T1: the bar sets the edge while its background is see-through; the pointer window, opaque, never does.
        var model = Shortcuts(2);
        model.Add(new SeparatorItem { Kind = SeparatorItem.RowLine, Name = "Work" });
        WithWindow(model, (window, _) =>
        {
            System.Windows.Controls.TextBlock Text(string text) => Descendants(window).OfType<System.Windows.Controls.TextBlock>().First(block => block.Text == text);
            Assert.Null(Text("Item 0").Effect);

            var edge = MainWindow.TextEdgeFor("#FFFFFF");
            window.Resources[MainWindow.TextEdgeKey] = edge;
            window.UpdateLayout();
            Assert.Same(edge, Text("Item 0").Effect);
            Assert.Same(edge, Text("Work").Effect); // a row line's caption
            Assert.Equal((0.0, true), (edge.ShadowDepth, edge.IsFrozen));
            Assert.Equal(System.Windows.Media.Color.FromRgb(0x1A, 0x1A, 0x1A), edge.Color); // under light text
            Assert.Equal(System.Windows.Media.Colors.White, MainWindow.TextEdgeFor("#202020").Color); // under dark text

            window.Resources.Remove(MainWindow.TextEdgeKey);
            Assert.Null(Text("Item 0").Effect);
        });
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var below in Descendants(child))
            {
                yield return below;
            }
        }
    }

    [Fact]
    public void A_row_lines_caption_keeps_24_units_of_line_unless_the_strip_is_one_icon_wide()
    {
        // 0.14.0 WP1 (A1): 6 in, 6 to the line, at least 24 of line, 6 from the edge; in a narrow strip it takes what a label would.
        Assert.Equal(200 - 42, RowLinePanel.CaptionRoom(200));
        Assert.Equal(84 - 42, RowLinePanel.CaptionRoom(84)); // exactly half the strip
        Assert.Equal(52 - 12, RowLinePanel.CaptionRoom(52)); // a bar one icon wide: the caption first
        Assert.Equal(double.PositiveInfinity, RowLinePanel.CaptionRoom(double.PositiveInfinity));
        Assert.Equal(0, RowLinePanel.CaptionRoom(8));
    }

    [Fact]
    public void A_window_shown_in_the_turn_it_is_made_fits_itself_once_its_bindings_are_alive()
    {
        // Its first measure sees an empty grid; the fit after showing it puts that right.
        WithWindow(Shortcuts(12), (window, bar) => EveryRowShows(window, bar.CellHeight), ahead: false);
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
            Assert.True(scroller.ComputedVerticalScrollBarVisibility == Visibility.Visible, $"no scroll bar: {Describe(window)}");
            Assert.True(
                scroller.ViewportHeight >= PointerWindow.MaxRows * bar.CellHeight && scroller.ViewportHeight < (PointerWindow.MaxRows + 0.5) * bar.CellHeight,
                $"not five rows: {Describe(window)}");
            Assert.True(window.Icons.LayoutPanel!.ActualWidth >= PointerWindow.MaxColumns * bar.CellWidth - 0.5, "the scroll bar took a column");
        });
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr hWnd);
}
