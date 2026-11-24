using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using DesktopIconGrid.Core;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Core.SpecialIcons;
using DesktopIconGrid.ViewModels;
using DesktopIconGrid.Views;

namespace DesktopIconGrid.App.Tests;

/// <summary>
/// The Show-at-pointer window built from its markup, on the shared WPF thread: what Alt+F4 does to it, and, since 0.15.0
/// (WP3), that it is the bar's twin: the bar's icon area, the icons where the bar has them, the bar's background.
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

    /// <summary>An icon area of <paramref name="columns"/> cells and <paramref name="height"/> units of rows, as a bar has it (the grid's margin of 1 included).</summary>
    private static Size Area(LauncherViewModel bar, int columns, double height) => new((columns * bar.CellWidth) + 2, height + 2);

    /// <summary>
    /// Shows the bar's icons at the pointer, lets the WPF thread settle, runs <paramref name="check"/> on the shown
    /// window and closes it. Made <paramref name="ahead"/>, as the bar does, its bindings are alive before it is shown;
    /// otherwise it is shown in the turn it is made, as when the hotkey comes first.
    /// </summary>
    private void WithWindow(LauncherModel model, Action<PointerWindow, LauncherViewModel> check, bool ahead = true, IEnumerable<DesktopIconGrid.SpecialIcons.ISpecialIconHandler>? handlers = null, Func<LauncherViewModel, Size>? area = null)
    {
        TestBar? bar = null;
        PointerWindow? window = null;
        try
        {
            wpf.Invoke(() =>
            {
                bar = new TestBar(_directory, model, handlers: handlers);
                window = new PointerWindow(bar.ViewModel);
                if (area is not null)
                {
                    var viewModel = bar.ViewModel;
                    window.BarIconArea = () => area(viewModel);
                }

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

    /// <summary>Every row shows in full: no scroll bar and nothing below the bottom edge. (The bar's area may have room under the last row.)</summary>
    private static void EveryRowShows(PointerWindow window)
    {
        var scroller = window.Icons.Scroller;
        Assert.True(window.Icons.ItemsHost.Items.Count > 0, $"no items: {Describe(window)}");
        Assert.True(scroller.ComputedVerticalScrollBarVisibility == Visibility.Collapsed, $"a scroll bar: {Describe(window)}");
        Assert.True(scroller.ExtentHeight <= scroller.ViewportHeight + 0.5, $"rows cut off: {Describe(window)}");
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
        // it was as invisible as before with 1 unit to click. A bar six cells wide: its rows hold their cells exactly.
        const int columns = 6;
        var model = Shortcuts(columns);
        model.Add(new SeparatorItem { Kind = SeparatorItem.Line });
        model.Add(Shortcut(99));
        WithWindow(model, (window, _) =>
        {
            var scroller = window.Icons.Scroller;
            var host = window.Icons.ItemsHost;
            var divider = (FrameworkElement)host.ItemContainerGenerator.ContainerFromIndex(columns);
            var lastIcon = (FrameworkElement)host.ItemContainerGenerator.ContainerFromIndex(columns - 1);
            var middle = divider.TranslatePoint(new Point(divider.ActualWidth / 2, divider.ActualHeight / 2), scroller);

            Assert.True(middle.X > 0 && middle.X < scroller.ViewportWidth, $"the divider's middle is at {middle.X}, outside the view's {scroller.ViewportWidth}: {Describe(window)}");
            var hit = scroller.InputHitTest(middle) as DependencyObject;
            Assert.True(IsWithin(hit, divider), $"a click on the divider's middle at {middle} lands on {hit}");

            var iconMiddle = lastIcon.TranslatePoint(new Point(lastIcon.ActualWidth / 2, lastIcon.ActualHeight / 2), scroller);
            Assert.True(IsWithin(scroller.InputHitTest(iconMiddle) as DependencyObject, lastIcon), "the last icon of the row is no longer hit in its middle");
        }, area: bar => Area(bar, columns, 3 * bar.CellHeight));
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
    public void An_opened_group_adds_its_header_and_the_icon_area_keeps_its_size_and_a_search_goes_back()
    {
        // The screen pass found a group of 7 losing its second row to its header. Since 0.15.0 (WP3) the header is added
        // under the search box and the icon area keeps the bar's size. Runs 239–242: the window kept the top level's
        // height, because the measure it was fitted with had been the last one and WPF skipped it (entry 140).
        var model = Shortcuts(10);
        var tools = new GroupItem { Name = "Tools" };
        model.Add(tools);
        for (var i = 0; i < 7; i++)
        {
            model.Add(Shortcut(100 + i), tools);
        }

        WithWindow(model, (window, bar) =>
        {
            EveryRowShows(window);
            var topLevel = window.MeasureSize(Size.Empty).Height; // as when the window was fitted once it was shown
            var area = window.Icons.Height;
            Assert.Equal(topLevel, window.ActualHeight, precision: 0);

            window.OpenGroup(bar.RootItems.Items.Single(item => item.Children is not null));
            window.UpdateLayout();

            EveryRowShows(window);
            var header = window.GroupHeader.ActualHeight + window.GroupHeader.Margin.Top + window.GroupHeader.Margin.Bottom;
            Assert.True(header > 10, $"the header takes {header}");
            Assert.Equal(topLevel + header, window.ActualHeight, precision: 0);
            Assert.Equal(topLevel + header, window.MeasureSize(Size.Empty).Height, precision: 1);
            Assert.Equal(area, window.Icons.Height, precision: 1);

            bar.SearchText = "Item 1"; // a search covers every group, so it goes back to the top level
            window.UpdateLayout();

            Assert.Null(window.OpenedGroup);
            Assert.Equal(topLevel, window.ActualHeight, precision: 0);
            Assert.Equal(topLevel, window.MeasureSize(Size.Empty).Height, precision: 1);
        }, area: bar => Area(bar, 6, 2 * bar.CellHeight));
    }

    /// <summary>
    /// Shows <paramref name="model"/> at the pointer in each form in turn (true: the hold form), letting the WPF thread
    /// settle after each showing as <see cref="WithWindow"/> does, and runs <paramref name="check"/> on each.
    /// </summary>
    private void InForms(LauncherModel model, Func<LauncherViewModel, Size> area, params (bool Held, Action<PointerWindow, LauncherViewModel> Check)[] steps)
    {
        TestBar? bar = null;
        PointerWindow? window = null;
        try
        {
            wpf.Invoke(() =>
            {
                bar = new TestBar(_directory, model);
                var viewModel = bar.ViewModel;
                window = new PointerWindow(viewModel) { BarIconArea = () => area(viewModel) };
            });
            wpf.Settle();
            foreach (var (held, check) in steps)
            {
                wpf.Invoke(() =>
                {
                    window!.Dismiss();
                    window.ShowAtPointer(held);
                });
                wpf.Settle();
                wpf.Invoke(() =>
                {
                    window!.UpdateLayout();
                    check(window, bar!.ViewModel);
                });
            }
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

    /// <summary>Where <paramref name="element"/>'s top left corner is in <paramref name="window"/>.</summary>
    private static Point Origin(FrameworkElement element, Window window) => element.TranslatePoint(default, window);

    [Fact]
    public void The_hold_form_has_no_search_row_and_is_shorter_by_it_and_the_tap_form_is_as_before()
    {
        // 0.16.0 WP1, the owner's request: the window shown while the key is held has no search box. It is shorter by the
        // box's row (with the gap under it), its icon area keeps the bar's size, and the icons sit directly at the top, as
        // far from the top as from the side. The tap form keeps its box, laid out as before, also after a hold.
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var tap = Size.Empty;
        var row = 0.0;
        var iconsTop = 0.0;
        var area = Size.Empty;
        InForms(
            Shortcuts(8),
            bar => Area(bar, 4, 2 * bar.CellHeight),
            (false, (window, _) =>
            {
                Assert.False(window.IsHoldForm);
                Assert.Equal(Visibility.Visible, window.SearchRow.Visibility);
                tap = window.MeasureSize(Size.Empty);
                Assert.Equal(tap.Height, window.ActualHeight, precision: 0);
                row = window.SearchRow.ActualHeight + window.SearchRow.Margin.Top + window.SearchRow.Margin.Bottom;
                Assert.True(row > 15, $"the search row takes {row}");
                iconsTop = Origin(window.Icons, window).Y;
                area = new Size(window.Icons.Width, window.Icons.Height);
                Assert.Same(window.SearchBox, window.StartElement);
                Assert.False(window.Icons.Focusable); // Down from the box steps to the first icon, not to the area
            }),
            (true, (window, _) =>
            {
                Assert.True(window.IsHoldForm);
                Assert.Equal(Visibility.Collapsed, window.SearchRow.Visibility);
                Assert.False(window.SearchBox.IsVisible);
                var hold = window.MeasureSize(Size.Empty);
                Assert.Equal(tap.Height - row, hold.Height, precision: 1);
                Assert.Equal(tap.Width, hold.Width, precision: 1);
                Assert.Equal(hold.Height, window.ActualHeight, precision: 0);
                Assert.Equal(area, new Size(window.Icons.Width, window.Icons.Height));
                var origin = Origin(window.Icons, window);
                Assert.Equal(iconsTop - row, origin.Y, precision: 1);
                Assert.Equal(origin.X, origin.Y, precision: 1); // directly at the top
                Assert.Same(window.Icons, window.StartElement); // the keys go to the icon area, none to a text box
                Assert.True(window.Icons.Focusable);
            }),
            (false, (window, _) =>
            {
                Assert.False(window.IsHoldForm);
                Assert.Equal(Visibility.Visible, window.SearchRow.Visibility);
                var again = window.MeasureSize(Size.Empty);
                Assert.Equal(tap.Width, again.Width, precision: 1);
                Assert.Equal(tap.Height, again.Height, precision: 1);
                Assert.Equal(tap.Height, window.ActualHeight, precision: 0);
                Assert.Equal(iconsTop, Origin(window.Icons, window).Y, precision: 1);
                Assert.Same(window.SearchBox, window.StartElement);
                Assert.False(window.Icons.Focusable);
            }));
    }

    [Fact]
    public void A_search_left_before_a_hold_never_filters_the_hold_form()
    {
        // 0.16.0 WP1: the hold form clears a search as the tap form does, so every icon is listed and none is faded.
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        InForms(
            Shortcuts(6),
            bar => Area(bar, 6, bar.CellHeight),
            (false, (window, bar) =>
            {
                window.Dismiss();
                bar.SearchText = "Item 3"; // typed into the bar's box, say, before the key was held
                Assert.True(bar.IsSearching);
                Assert.Contains(bar.RootItems.Items, item => item.IsSearchDimmed);

                window.ShowAtPointer(held: true);
                window.UpdateLayout();

                Assert.False(bar.IsSearching);
                Assert.Equal(string.Empty, bar.SearchText);
                Assert.DoesNotContain(bar.RootItems.Items, item => item.IsSearchDimmed);
                Assert.Equal(6, window.Icons.ItemsHost.Items.Count);
                EveryRowShows(window);
            }));
    }

    [Fact]
    public void A_group_opened_in_the_hold_form_has_its_header_directly_at_the_top()
    {
        // 0.16.0 WP1: the way back is still there, at the very top; the window grows by the header and the gap under it.
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var model = Shortcuts(4);
        var tools = new GroupItem { Name = "Tools" };
        model.Add(tools);
        model.Add(Shortcut(100), tools);
        InForms(
            model,
            bar => Area(bar, 6, 2 * bar.CellHeight),
            (true, (window, bar) =>
            {
                var topLevel = window.MeasureSize(Size.Empty).Height;

                window.OpenGroup(bar.RootItems.Items.Single(item => item.Children is not null));
                window.UpdateLayout();

                Assert.Equal(Visibility.Visible, window.GroupHeader.Visibility);
                var header = Origin(window.GroupHeader, window);
                Assert.Equal(header.X, header.Y, precision: 1); // nothing above it
                var line = window.GroupHeader.ActualHeight + window.GroupHeader.Margin.Top + window.GroupHeader.Margin.Bottom;
                Assert.True(line > 10, $"the header takes {line}");
                Assert.Equal(header.Y + line, Origin(window.Icons, window).Y, precision: 1);
                Assert.Equal(topLevel + line, window.MeasureSize(Size.Empty).Height, precision: 1);
                Assert.Equal(topLevel + line, window.ActualHeight, precision: 0);
                Assert.Same(window.Icons, window.StartElement);
                Assert.Equal(Visibility.Collapsed, window.SearchRow.Visibility);
            }));
    }

    [Fact]
    public void The_notice_line_shows_under_the_icons_as_wide_as_they_are_and_the_window_grows_by_it()
    {
        // 0.15.0 WP1: the first drag refused on a locked grid says so in the bar's notice row, and here under the icons.
        WithWindow(Shortcuts(6), (window, bar) =>
        {
            var before = window.ActualHeight;
            var width = window.ActualWidth;
            var area = window.Icons.Height;
            Assert.Equal(Visibility.Collapsed, window.MessageRow.Visibility);

            bar.LockGrid = true;
            Assert.False(IconGridControl.MayMove(bar.RootItems.Items[0]));
            window.UpdateLayout();

            Assert.Equal(Visibility.Visible, window.MessageRow.Visibility);
            Assert.Equal(DesktopIconGrid.Resources.Strings.Main_GridLocked, window.MessageText.Text);
            var line = window.MessageRow.ActualHeight + window.MessageRow.Margin.Top + window.MessageRow.Margin.Bottom;
            Assert.True(line > 10, $"the line takes {line}");
            Assert.Equal(window.Icons.Width, window.MessageRow.ActualWidth, precision: 1); // it wraps within the icons' width
            Assert.Equal(before + line, window.ActualHeight, precision: 0);
            Assert.Equal(width, window.ActualWidth, precision: 0);
            Assert.Equal(area, window.Icons.Height, precision: 1);

            bar.LockGrid = false; // takes the notice back
            window.UpdateLayout();

            Assert.Equal(Visibility.Collapsed, window.MessageRow.Visibility);
            Assert.Equal(before, window.ActualHeight, precision: 0);
        }, area: bar => Area(bar, 4, bar.CellHeight));
    }

    [Fact]
    public void The_icon_area_is_the_bars_and_the_icons_fall_where_the_bars_do()
    {
        // 0.15.0 WP3: a bar five cells wide that wraps, with a line, a caption, a row line with a caption and a row-start
        // line. The window's icon area has the bar's size, and each entry lies where it lies in a bar's grid of that size.
        var model = Shortcuts(6);
        model.Add(new SeparatorItem { Kind = SeparatorItem.Line });
        model.Add(new SeparatorItem { Kind = SeparatorItem.Label, Name = "Work in progress" });
        model.Add(new SeparatorItem { Kind = SeparatorItem.RowLine, Name = "Tools" });
        model.Add(new SeparatorItem { Kind = SeparatorItem.RowHead });
        for (var i = 6; i < 12; i++)
        {
            model.Add(Shortcut(i));
        }

        Size? expected = null;
        WithWindow(model, (window, bar) =>
        {
            var area = expected!.Value;
            Assert.Equal(area.Width, window.Icons.ActualWidth, precision: 1);
            Assert.Equal(area.Height, window.Icons.ActualHeight, precision: 1);

            // The bar's grid: the same control at the same size, in a window with the bar's font.
            var barGrid = new IconGridControl { Width = area.Width, Height = area.Height, DataContext = bar.RootItems };
            var barWindow = new Window
            {
                Content = barGrid,
                DataContext = bar,
                SizeToContent = SizeToContent.WidthAndHeight,
                WindowStyle = WindowStyle.None,
                ShowInTaskbar = false,
                ShowActivated = false,
                UseLayoutRounding = true,
            };
            TextOptions.SetTextFormattingMode(barWindow, TextFormattingMode.Display);
            DesktopIconGrid.Services.UiFonts.ApplyBar(barWindow);
            try
            {
                barWindow.Show();
                barWindow.UpdateLayout();
                Rect BoxOf(IconGridControl grid, int index)
                {
                    var container = (FrameworkElement)grid.ItemsHost.ItemContainerGenerator.ContainerFromIndex(index);
                    return container.TransformToAncestor(grid.ItemsHost).TransformBounds(new Rect(container.RenderSize));
                }

                var count = window.Icons.ItemsHost.Items.Count;
                Assert.Equal(count, barGrid.ItemsHost.Items.Count);
                for (var i = 0; i < count; i++)
                {
                    var mine = BoxOf(window.Icons, i);
                    var theBars = BoxOf(barGrid, i);
                    Assert.True(Math.Abs(mine.X - theBars.X) < 1 && Math.Abs(mine.Y - theBars.Y) < 1 && Math.Abs(mine.Width - theBars.Width) < 1,
                        $"entry {i} is at {mine}, on the bar at {theBars}");
                }

                Assert.Equal(barGrid.Scroller.ComputedVerticalScrollBarVisibility, window.Icons.Scroller.ComputedVerticalScrollBarVisibility);
            }
            finally
            {
                barWindow.Close();
            }
        }, area: bar => (expected = Area(bar, 5, (4 * bar.CellHeight) + 30)).Value);
    }

    [Fact]
    public void An_icon_area_too_large_for_the_work_area_gives_up_what_does_not_fit()
    {
        // 0.15.0 WP3: a maximised or edge-docked bar on a larger monitor. Measured in WPF units at the monitor's scale.
        var bar = new Size(1500, 900);
        var around = new Size(8, 40);
        Assert.Equal(bar, PointerWindow.FitIconArea(bar, around, new Size(2000, 1200))); // fits as it is
        Assert.Equal(new Size(992, 728), PointerWindow.FitIconArea(bar, around, new Size(1000, 768))); // capped both ways
        Assert.Equal(new Size(1500, 728), PointerWindow.FitIconArea(bar, around, new Size(1600, 768))); // the height only
        Assert.Equal(bar, PointerWindow.FitIconArea(bar, around, Size.Empty)); // no work area known: held to nothing

        // A window from a bar far larger than any screen stays on the pointer's work area, and its icons flow again.
        WithWindow(Shortcuts(30), (window, _) =>
        {
            var work = SystemParameters.WorkArea;
            Assert.True(window.ActualWidth <= work.Width + 1 && window.ActualHeight <= work.Height + 1,
                $"the window is {window.ActualWidth}×{window.ActualHeight} on a work area of {work.Width}×{work.Height}");
            Assert.True(window.Icons.ActualWidth < 20000, "the icon area was not held back");
        }, area: _ => new Size(20000, 12000));
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
            EveryRowShows(window);
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
        }, area: bar => Area(bar, 0, (4 * bar.CellHeight) + 40) with { Width = FlowLayout.NarrowSize + (3 * bar.CellWidth) + 2 });
    }

    [Fact]
    public void Names_and_captions_take_the_windows_text_edge_and_none_without_it()
    {
        // Review 37 T1: the edge is the window's resource while its background is see-through (since 0.15.0 the pointer
        // window has it as the bar does, through BarSurface); set here by hand.
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

    [Fact]
    public void Glyphs_and_gauge_text_in_the_themes_colour_and_a_grey_value_take_an_edge_and_coloured_ones_none()
    {
        // Review 38 T2: the Voice microphone (a symbol in the theme's colour, no tile colour), a gauge's text in the theme's
        // colour, and a value in the neutral grey (the clock's time) take an edge while the bar is see-through; a symbol on
        // a tile colour and a green value do not.
        var model = new LauncherModel();
        model.Add(new SpecialIconItem { Kind = SpecialIconKinds.Voice, Name = "Voice" });
        model.Add(new ShortcutItem { Name = "Tiled", Location = @"C:\Tools\tool.exe", TextIcon = new TextIcon("T", Fit: true, Background: "#3366CC") });
        model.Add(new SpecialIconItem { Kind = "test-ring", Name = "Ring" });
        model.Add(new SpecialIconItem { Kind = "test-bar", Name = "Bar" });
        model.Add(new SpecialIconItem { Kind = "test-clock", Name = "Clock" });
        model.Add(new SpecialIconItem { Kind = "test-ok", Name = "Ok" });
        DesktopIconGrid.SpecialIcons.ISpecialIconHandler[] handlers =
        [
            new FakeIconHandler(SpecialIconKinds.Voice, glyph: DesktopIconGrid.Core.SpecialIcons.BuiltInTextIcons.Voice),
            new FakeIconHandler("test-ring", gauge: () => Gauge("RING", DesktopIconGrid.Core.SpecialIcons.GaugeStyle.Ring, 42, null, null)),
            new FakeIconHandler("test-bar", gauge: () => Gauge("BAR", DesktopIconGrid.Core.SpecialIcons.GaugeStyle.Bar, 7, null, null)),
            new FakeIconHandler("test-clock", gauge: () => Gauge("CLOCK", DesktopIconGrid.Core.SpecialIcons.GaugeStyle.Number, null, "12:34", DesktopIconGrid.Core.SpecialIcons.CommandLevel.Unknown)),
            new FakeIconHandler("test-ok", gauge: () => Gauge("OK", DesktopIconGrid.Core.SpecialIcons.GaugeStyle.Number, null, "Done", DesktopIconGrid.Core.SpecialIcons.CommandLevel.Ok)),
        ];
        var hasGlyph = TextIconTile.HasGlyph;
        TextIconTile.HasGlyph = _ => true; // the microphone, whatever symbol fonts the test machine has
        try
        {
            WithWindow(model, CheckEdges, handlers: handlers);
        }
        finally
        {
            TextIconTile.HasGlyph = hasGlyph;
        }

        static void CheckEdges(PointerWindow window, LauncherViewModel bar)
        {
            // A gauge's template has all three styles, the two not used collapsed: the one shown.
            System.Windows.Controls.TextBlock Text(string text) => Descendants(window).OfType<System.Windows.Controls.TextBlock>().Single(block => block.Text == text && block.IsVisible);
            var tiles = Descendants(window).OfType<TextIconTile>().ToList();
            var voice = tiles.Single(tile => tile.Icon == DesktopIconGrid.Core.SpecialIcons.BuiltInTextIcons.Voice);
            var tiled = tiles.Single(tile => tile.Icon?.Background is not null);
            Assert.Null(voice.FitBox!.Effect);
            Assert.Null(Text("12:34").Effect);

            MainWindow.SetSeeThroughLook(window.Resources, "#FFFFFF", bar.IconSize);
            window.UpdateLayout();
            var edge = window.Resources[MainWindow.TextEdgeKey];
            var gaugeEdge = (System.Windows.Media.Effects.DropShadowEffect)window.Resources[MainWindow.GaugeTextEdgeKey];
            var neutralEdge = (System.Windows.Media.Effects.DropShadowEffect)window.Resources[MainWindow.GaugeNeutralEdgeKey];
            Assert.Same(edge, voice.FitBox!.Effect); // on the box, at the bar's scale, not on the text the box scales
            Assert.Null(voice.TextElement.Effect);
            Assert.Null(tiled.FitBox!.Effect); // on a tile colour
            Assert.Null(tiled.TextElement.Effect);
            Assert.Same(gaugeEdge, Text("42%").Effect); // the ring's value
            Assert.Same(gaugeEdge, Text("BAR").Effect); // the bar style's caption and value
            Assert.Same(gaugeEdge, Text("7%").Effect);
            Assert.Same(neutralEdge, Text("12:34").Effect); // the clock's grey time
            Assert.Null(Text("Done").Effect); // green

            // In the gauge's box of 100 units, scaled to the icon: as wide on the bar as the names' edge.
            Assert.Equal(MainWindow.TextEdgeBlur * 100 / bar.IconSize, gaugeEdge.BlurRadius, 3);
            Assert.Equal(gaugeEdge.BlurRadius, neutralEdge.BlurRadius, 3);
            Assert.Equal(System.Windows.Media.Color.FromRgb(0x1A, 0x1A, 0x1A), neutralEdge.Color); // the grey is light: a dark edge
            Assert.Equal(System.Windows.Media.Color.FromRgb(0x1A, 0x1A, 0x1A), gaugeEdge.Color); // under the white of the dark theme

            MainWindow.SetSeeThroughLook(window.Resources, null, bar.IconSize);
            Assert.Null(voice.FitBox!.Effect);
            Assert.Null(Text("42%").Effect);
            Assert.Null(Text("12:34").Effect);
        }
    }

    [Fact]
    public void Lines_take_the_text_colour_and_the_edge_while_see_through_and_the_bars_marks_the_edge()
    {
        // Review 39 T3: the three line separators, in the theme's faint line colour on a solid background, in the text colour
        // with the names' edge on a see-through one; the grip, the glyphs of ✕, ▸ and ←, and what Enter opens get the edge.
        var model = Shortcuts(2);
        model.Add(new SeparatorItem { Kind = SeparatorItem.Line });
        model.Add(new SeparatorItem { Kind = SeparatorItem.RowHead });
        model.Add(new SeparatorItem { Kind = SeparatorItem.RowLine });
        WithWindow(model, (window, bar) =>
        {
            var lines = Descendants(window).OfType<DividerLine>().Where(line => line.IsVisible).ToList();
            Assert.Equal(3, lines.Count);
            var theme = window.FindResource(DesktopIconGrid.Services.ThemeManager.LineStrokeKey);
            Assert.All(lines, line => Assert.Same(theme, line.Stroke));
            Assert.All(lines, line => Assert.Null(line.Effect));
            // ← is in the group's header, collapsed, so its button has no template yet: it is in the logical tree only.
            var back = LogicalDescendants(window).OfType<System.Windows.Controls.TextBlock>().Single(block => block.Text == "←");
            var target = LogicalDescendants(window).OfType<System.Windows.Controls.TextBlock>().Single(block => block.Style == window.FindResource("SearchTargetStyle"));
            Assert.Null(back.Effect);
            Assert.Null(target.Effect);

            MainWindow.SetSeeThroughLook(window.Resources, "#FFFFFF", bar.IconSize);
            window.UpdateLayout();
            var edge = window.Resources[MainWindow.TextEdgeKey];
            Assert.All(lines, line =>
            {
                var stroke = Assert.IsType<SolidColorBrush>(line.Stroke);
                Assert.Equal((Colors.White, 1.0), (stroke.Color, stroke.Opacity)); // the text colour, at full strength
                Assert.Same(edge, line.Effect);
            });
            Assert.Same(edge, back.Effect); // the glyph style ✕ and ▸ have on the bar
            Assert.Same(edge, target.Effect);

            MainWindow.SetSeeThroughLook(window.Resources, null, bar.IconSize);
            Assert.All(lines, line => Assert.Same(theme, line.Stroke)); // the theme's own again
            Assert.All(lines, line => Assert.Null(line.Effect));
            Assert.Null(back.Effect);

            // The grip lives only on the bar; its style, in a window of its own.
            var grip = new System.Windows.Controls.Border { Style = (Style)window.FindResource("GripStyle"), Width = 10, Height = 40 };
            var host = new Window { Content = grip, Width = 60, Height = 80, ShowInTaskbar = false, ShowActivated = false };
            try
            {
                host.Show();
                Assert.Null(grip.Effect);
                MainWindow.SetSeeThroughLook(host.Resources, "#202020", bar.IconSize);
                Assert.Same(host.Resources[MainWindow.TextEdgeKey], grip.Effect);
                Assert.Equal(Colors.White, Assert.IsType<System.Windows.Media.Effects.DropShadowEffect>(grip.Effect).Color); // under dark marks
            }
            finally
            {
                host.Close();
            }
        });
    }

    [Fact]
    public void A_value_with_no_level_is_neutral_until_it_has_one()
    {
        var state = Gauge("CPU", DesktopIconGrid.Core.SpecialIcons.GaugeStyle.Number, null, null, null);
        Assert.True(state.IsNeutral); // no sample yet
        var changed = new List<string?>();
        state.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        state.Level = DesktopIconGrid.Core.SpecialIcons.CommandLevel.Ok;
        Assert.False(state.IsNeutral);
        Assert.Contains(nameof(state.IsNeutral), changed);
        state.Level = null;
        state.Update(42, "CPU");
        Assert.False(state.IsNeutral); // green from the percentage
        state.Level = DesktopIconGrid.Core.SpecialIcons.CommandLevel.Unknown;
        Assert.True(state.IsNeutral);
    }

    private static DesktopIconGrid.SpecialIcons.GaugeState Gauge(string caption, DesktopIconGrid.Core.SpecialIcons.GaugeStyle style, double? percent, string? label, DesktopIconGrid.Core.SpecialIcons.CommandLevel? level)
    {
        var state = new DesktopIconGrid.SpecialIcons.GaugeState(caption, style, warningAt: 80, criticalAt: 95, string.Empty) { LabelText = label, Level = level };
        state.Update(percent, caption);
        return state;
    }

    /// <summary>A special icon kind with a glyph or a gauge and nothing else.</summary>
    private sealed class FakeIconHandler(string kind, TextIcon? glyph = null, Func<DesktopIconGrid.SpecialIcons.GaugeState>? gauge = null) : DesktopIconGrid.SpecialIcons.ISpecialIconHandler
    {
        public string Kind => kind;

        public string DisplayName => kind;

        public string Description => kind;

        public TextIcon? DefaultTextIcon => glyph;

        public SpecialIconItem CreateItem() => new() { Kind = kind, Name = kind };

        public DesktopIconGrid.SpecialIcons.GaugeState? CreateState(SpecialIconItem item) => gauge?.Invoke();

        public DesktopIconGrid.Core.Launching.LaunchResult Activate(SpecialIconItem item, DesktopIconGrid.SpecialIcons.ActivationModifiers modifiers) =>
            DesktopIconGrid.Core.Launching.LaunchResult.Started;

        public bool ShowProperties(SpecialIconItem item, bool isNew) => false;
    }

    private static IEnumerable<object> LogicalDescendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root))
        {
            yield return child;
            if (child is DependencyObject node)
            {
                foreach (var below in LogicalDescendants(node))
                {
                    yield return below;
                }
            }
        }
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
    public void A_window_shown_in_the_turn_it_is_made_has_the_bars_icon_area()
    {
        // Its first measure comes before its bindings are alive; the icon area is the bar's all the same.
        WithWindow(Shortcuts(12), (window, bar) =>
        {
            EveryRowShows(window);
            Assert.Equal(Area(bar, 6, 2 * bar.CellHeight).Width, window.Icons.ActualWidth, precision: 1);
        }, ahead: false, area: bar => Area(bar, 6, 2 * bar.CellHeight));
    }

    [Fact]
    public void A_few_icons_keep_the_bars_icon_area()
    {
        // 0.15.0 WP3: no fitting to the icons any more (before, at least three cells): the bar's width and height.
        WithWindow(Shortcuts(2), (window, bar) =>
        {
            EveryRowShows(window);
            Assert.Equal(Area(bar, 7, 3 * bar.CellHeight), new Size(Math.Round(window.Icons.ActualWidth, 1), Math.Round(window.Icons.ActualHeight, 1)));
        }, area: bar => Area(bar, 7, 3 * bar.CellHeight));
    }

    [Fact]
    public void Rows_past_the_bars_height_scroll()
    {
        // 0.15.0 WP3: as on the bar, past its height (before, past five rows).
        WithWindow(Shortcuts(40), (window, bar) =>
        {
            var scroller = window.Icons.Scroller;
            Assert.True(scroller.ComputedVerticalScrollBarVisibility == Visibility.Visible, $"no scroll bar: {Describe(window)}");
            Assert.Equal(Area(bar, 6, 3 * bar.CellHeight).Height, window.Icons.ActualHeight, precision: 1);
        }, area: bar => Area(bar, 6, 3 * bar.CellHeight));
    }

    [Fact]
    public void The_window_has_the_bars_see_through_look_below_100_percent_and_none_at_100_or_under_High_Contrast()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        // 0.15.0 WP3: the bar's background on this window, through the same BarSurface.
        wpf.Invoke(() =>
        {
            using var bar = CreateBar(4);
            var settings = bar.ViewModel.Model.Settings;
            settings.BarBackdrop = LauncherSettings.BackdropSolid; // a backdrop, where Windows has one, would take over
            var window = new PointerWindow(bar.ViewModel);
            try
            {
                settings.Opacity = 70;
                window.ShowAtPointer();
                Assert.True(window.Surface.SeeThrough, "not see-through at 70 %");
                Assert.Equal(Brushes.Transparent, window.Background);
                Assert.True(window.Resources.Contains(MainWindow.TextEdgeKey), "no text edge while see-through");
                Assert.Equal(TextRenderingMode.Grayscale, TextOptions.GetTextRenderingMode(window));
                window.Dismiss();

                settings.Opacity = LauncherSettings.MaxOpacity; // shows at the next opening
                window.ShowAtPointer();
                Assert.False(window.Surface.SeeThrough);
                Assert.False(window.Resources.Contains(MainWindow.TextEdgeKey));
                Assert.Equal(TextRenderingMode.ClearType, TextOptions.GetTextRenderingMode(window)); // opaque (0.15.0 WP9)
                Assert.Equal(TextFormattingMode.Display, TextOptions.GetTextFormattingMode(window));
                Assert.True(window.UseLayoutRounding);
                window.Dismiss();

                settings.Opacity = 70;
                window.Surface.HighContrast = () => true;
                window.ShowAtPointer();
                Assert.False(window.Surface.SeeThrough, "see-through under High Contrast");
                Assert.False(window.Resources.Contains(MainWindow.TextEdgeKey));
                Assert.NotEqual(Brushes.Transparent, window.Background);
                Assert.Equal(TextRenderingMode.ClearType, TextOptions.GetTextRenderingMode(window)); // the same rule under High Contrast
            }
            finally
            {
                window.CloseForGood();
            }
        });
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr hWnd);
}
