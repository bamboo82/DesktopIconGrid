using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Resources;
using DesktopIconGrid.ViewModels;
using DesktopIconGrid.Views;

namespace DesktopIconGrid.App.Tests;

/// <summary>
/// The Settings and Sync dialogs on a screen too short for them (the screen pass: Settings was 1,089 px tall in
/// Chinese, with Save below the edge of a 1080p screen) and with labels too long for a line.
/// </summary>
[Collection(WpfWindowsCollection.Name)]
public sealed class DialogFitTests(WpfThread wpf)
{
    private static IEnumerable<T> LogicalDescendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is T match)
            {
                yield return match;
            }

            foreach (var deeper in LogicalDescendants<T>(child))
            {
                yield return deeper;
            }
        }
    }

    /// <summary>Shows <paramref name="window"/> on a "screen" <paramref name="height"/> units tall and checks that its buttons are in sight and its body scrolls.</summary>
    private static void FitsAShortScreen(Window window, ScrollViewer body, double height, params string[] buttons)
    {
        try
        {
            window.Show();
            Assert.True(window.MaxHeight <= SystemParameters.WorkArea.Height, $"taller than the work area: {window.MaxHeight}");
            window.MaxHeight = height;
            window.UpdateLayout();

            Assert.True(window.ActualHeight <= height + 0.5, $"{window.ActualHeight} units tall");
            var client = ((FrameworkElement)window.Content).ActualHeight;
            foreach (var label in buttons)
            {
                var button = LogicalDescendants<Button>(window).Single(b => Equals(b.Content, label));
                var bottom = button.TranslatePoint(new Point(0, button.ActualHeight), window).Y;
                Assert.True(button.IsVisible && bottom <= client + 0.5, $"{label} ends at {bottom}, below the window's {client}");
            }

            Assert.True(body.ExtentHeight > body.ViewportHeight, $"nothing to scroll: {body.ExtentHeight} in {body.ViewportHeight}");
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void Settings_scroll_on_a_short_screen_with_Save_and_Cancel_in_sight()
    {
        wpf.Invoke(() =>
        {
            var window = new SettingsWindow { DataContext = new SettingsViewModel(new LauncherSettings()) };
            window.Tabs.SelectedIndex = 2; // Keys, the tallest page since Settings has tabs
            FitsAShortScreen(window, window.Body, 400, Strings.Common_Save, Strings.Common_Cancel);
        });
    }

    /// <summary>
    /// Review 27 B1: the voice rows had Grid.Row 14 and 15 in a grid of 14 rows, so WPF drew them on row 13, over the
    /// window keys. Every grid on every tab: each child inside the grid's rows and columns, and no two children on one cell.
    /// </summary>
    [Fact]
    public void Every_tab_of_Settings_gives_each_control_a_cell_of_its_own()
    {
        wpf.Invoke(() =>
        {
            var window = new SettingsWindow { DataContext = new SettingsViewModel(new LauncherSettings()) };
            try
            {
                Assert.Equal(5, window.Tabs.Items.Count);
                Assert.Equal(5, window.PageList.Count);
                foreach (var page in window.PageList)
                {
                    var grids = (page.Content is Grid root ? [root] : Array.Empty<Grid>()).Concat(LogicalDescendants<Grid>(page)).Distinct().ToList();
                    Assert.NotEmpty(grids);
                    foreach (var grid in grids)
                    {
                        EachChildHasItsOwnCell(grid, (string)page.GetValue(System.Windows.Automation.AutomationProperties.NameProperty));
                    }
                }
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static void EachChildHasItsOwnCell(Grid grid, string tab)
    {
        var rows = Math.Max(1, grid.RowDefinitions.Count);
        var columns = Math.Max(1, grid.ColumnDefinitions.Count);
        var taken = new Dictionary<(int Row, int Column), UIElement>();
        foreach (UIElement child in grid.Children)
        {
            var row = Grid.GetRow(child);
            var column = Grid.GetColumn(child);
            Assert.True(row < rows, $"{tab}: {Describe(child)} is in row {row} of a grid with {rows}");
            Assert.True(column < columns, $"{tab}: {Describe(child)} is in column {column} of a grid with {columns}");
            for (var r = row; r < Math.Min(rows, row + Grid.GetRowSpan(child)); r++)
            {
                for (var c = column; c < Math.Min(columns, column + Grid.GetColumnSpan(child)); c++)
                {
                    Assert.True(taken.TryAdd((r, c), child), $"{tab}: {Describe(child)} and {Describe(taken.GetValueOrDefault((r, c))!)} share row {r}, column {c}");
                }
            }
        }
    }

    private static string Describe(UIElement element) => element switch
    {
        TextBlock text => $"\"{text.Text}\"",
        FrameworkElement { Name.Length: > 0 } named => named.Name,
        _ => element.GetType().Name,
    };

    [Fact]
    public void Settings_show_one_page_at_a_time_in_the_height_of_the_tallest()
    {
        wpf.Invoke(() =>
        {
            var window = new SettingsWindow { DataContext = new SettingsViewModel(new LauncherSettings()) };
            try
            {
                window.Show();
                window.UpdateLayout();
                Assert.Equal(0, window.Tabs.SelectedIndex); // opens on Appearance
                var height = window.ActualHeight;

                window.Tabs.SelectedIndex = 4; // General, the shortest page
                window.UpdateLayout();
                Assert.Equal([false, false, false, false, true], window.PageList.Select(page => page.IsVisible));
                Assert.Equal(height, window.ActualHeight, 1); // the page area keeps the tallest page's height

                window.Tabs.SelectedIndex = 0;
                window.UpdateLayout();
                Assert.Same(window.PageList[0], window.Body);
                Assert.True(window.PageList[0].IsVisible);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void Item_properties_scroll_on_a_short_screen_with_the_advanced_options_open()
    {
        // The review: 18 rows, no scroll bar, and eight more rows of hints once Advanced is open.
        wpf.Invoke(() =>
        {
            var window = new ItemPropertiesWindow
            {
                DataContext = new ItemPropertiesViewModel(new ShortcutItem { Name = "Tool", Location = @"C:\Tools\tool.exe" }, isNew: false, new NoIcons()),
            };
            window.Advanced.IsExpanded = true;
            FitsAShortScreen(window, window.Body, 400, Strings.Common_Save, Strings.Common_Cancel);
        });
    }

    /// <summary>Lets the window take the size and place its last change asks for.</summary>
    private static void Settle(Window window)
    {
        window.UpdateLayout();
        var frame = new DispatcherFrame();
        window.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () => frame.Continue = false);
        Dispatcher.PushFrame(frame);
        window.UpdateLayout();
    }

    [Fact]
    public void Item_properties_near_the_bottom_of_the_screen_move_up_when_Advanced_opens()
    {
        // Review 15 UI1: Advanced is closed when the dialog opens; opening it grew the window downward, under the taskbar
        // when it had opened near the bottom (centred on a bar there), with Save and Cancel.
        wpf.Invoke(() =>
        {
            var window = new ItemPropertiesWindow
            {
                DataContext = new ItemPropertiesViewModel(new ShortcutItem { Name = "Tool", Location = @"C:\Tools\tool.exe" }, isNew: false, new NoIcons()),
                WindowStartupLocation = WindowStartupLocation.Manual,
            };
            try
            {
                window.Show();
                Settle(window);
                var work = SystemParameters.WorkArea;
                window.Top = work.Bottom - window.ActualHeight - 2;
                Settle(window);
                var closedHeight = window.ActualHeight;

                window.Advanced.IsExpanded = true;
                Settle(window);

                Assert.True(window.ActualHeight > closedHeight + 20, $"Advanced did not make the dialog taller: {closedHeight} to {window.ActualHeight}");
                var toUnits = PresentationSource.FromVisual(window)!.CompositionTarget!.TransformFromDevice;
                foreach (var label in new[] { Strings.Common_Save, Strings.Common_Cancel })
                {
                    var button = LogicalDescendants<Button>(window).Single(b => Equals(b.Content, label));
                    var bottom = toUnits.Transform(button.PointToScreen(new Point(0, button.ActualHeight))).Y;
                    Assert.True(bottom <= work.Bottom + 0.5, $"{label} ends at {bottom}, below the work area's {work.Bottom}");
                }
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void Sync_scrolls_on_a_short_screen_with_Close_in_sight()
    {
        wpf.Invoke(() =>
        {
            var window = new SyncWindow();
            FitsAShortScreen(window, window.Body, 300, Strings.Common_Close);
        });
    }

    [Fact]
    public void Every_check_box_and_option_in_Settings_and_Sync_wraps_its_text()
    {
        wpf.Invoke(() =>
        {
            var wrapping = Application.Current.FindResource("WrappingLabel");
            foreach (var window in new Window[] { new SettingsWindow { DataContext = new SettingsViewModel(new LauncherSettings()) }, new SyncWindow() })
            {
                try
                {
                    var toggles = LogicalDescendants<ContentControl>(window).Where(c => c is CheckBox or RadioButton).ToList();
                    Assert.NotEmpty(toggles);
                    Assert.All(toggles, toggle => Assert.Same(wrapping, toggle.ContentTemplate));
                }
                finally
                {
                    window.Close();
                }
            }
        });
    }
}
