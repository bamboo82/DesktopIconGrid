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
    /// <summary>Whether neither the element nor anything around it is collapsed or hidden.</summary>
    private static bool IsShown(FrameworkElement element)
    {
        for (DependencyObject? node = element; node is not null; node = LogicalTreeHelper.GetParent(node))
        {
            if (node is UIElement { Visibility: not Visibility.Visible })
            {
                return false;
            }
        }

        return true;
    }

    private static void FitsAShortScreen(Window window, ScrollViewer body, double height, params string[] buttons)
    {
        try
        {
            window.Show();
            Assert.True(window.MaxHeight <= SystemParameters.WorkArea.Height, $"taller than the work area: {window.MaxHeight}");
            window.MaxHeight = height;
            window.UpdateLayout();

            Assert.True(window.ActualHeight <= height + 0.5, $"{window.ActualHeight} units tall");
            // The bottom of the content in the window's units, where the buttons are measured: a dialog scaled for the text
            // size (0.13.0 WP4) has its content's ActualHeight before the scale. At 100 % it is that height.
            var root = (FrameworkElement)window.Content;
            var client = root.TranslatePoint(new Point(0, root.ActualHeight), window).Y;
            foreach (var label in buttons)
            {
                // The one in sight: Settings also has a Cancel for a font download, collapsed until one runs (0.12.0 WP5).
                var button = LogicalDescendants<Button>(window).Single(b => Equals(b.Content, label) && IsShown(b));
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
                Assert.Equal(6, window.Tabs.Items.Count);
                Assert.Equal(6, window.PageList.Count);
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
                Assert.Equal([false, false, false, false, true, false], window.PageList.Select(page => page.IsVisible));
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

    [Theory]
    [InlineData("en-US", 0.9)]
    [InlineData("en-US", 1.0)]
    [InlineData("en-US", 1.5)]
    [InlineData("zh-Hant", 0.9)]
    [InlineData("zh-Hant", 1.0)]
    [InlineData("zh-Hant", 1.5)]
    [InlineData("zh-Hans", 0.9)]
    [InlineData("zh-Hans", 1.0)]
    [InlineData("zh-Hans", 1.5)]
    public void The_Portable_tab_is_last_and_fits_and_the_tabs_stay_in_one_row(string language, double scale)
    {
        // 0.14.0 WP4: the data location on a tab of its own, moved there from General unchanged.
        wpf.Invoke(() =>
        {
            var before = System.Threading.Thread.CurrentThread.CurrentUICulture;
            System.Threading.Thread.CurrentThread.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo(language);
            Services.UiFonts.Configure(null, null, scale);
            var window = new SettingsWindow { DataContext = new SettingsViewModel(new LauncherSettings()) };
            try
            {
                window.Show();
                window.Tabs.SelectedIndex = window.PageList.ToList().IndexOf(window.PortablePage);
                Settle(window);
                var what = $"{language} at {scale:P0}";
                Assert.Equal(window.Tabs.Items.Count - 1, window.Tabs.SelectedIndex);
                Assert.True(window.PortablePage.IsAncestorOf(window.DataLocationBlock), "the data location is not on the Portable tab");
                Assert.False(window.GeneralPage.IsAncestorOf(window.DataLocationBlock));
                TabsInOneRow(window, what);
                FitsAcross(window.PortablePage, what);
            }
            finally
            {
                window.Close();
                Services.UiFonts.Configure(null);
                System.Threading.Thread.CurrentThread.CurrentUICulture = before;
            }
        });
    }

    /// <summary>Every tab of Settings on the strip's first row, none past its end.</summary>
    private static void TabsInOneRow(SettingsWindow window, string what)
    {
        var tabs = window.Tabs.Items.Cast<TabItem>().ToList();
        var top = tabs[0].TranslatePoint(default, window.Tabs).Y;
        foreach (var tab in tabs)
        {
            var at = tab.TranslatePoint(default, window.Tabs);
            Assert.True(Math.Abs(at.Y - top) < tabs[0].ActualHeight / 2, $"{what}: \"{tab.Header}\" is on another row ({at.Y}, the first at {top})");
            Assert.True(at.X + tab.ActualWidth <= window.Tabs.ActualWidth + 0.5, $"{what}: \"{tab.Header}\" ends at {at.X + tab.ActualWidth} of {window.Tabs.ActualWidth}");
        }
    }

    /// <summary>Every text, box and button shown on <paramref name="page"/> within its width, and none squeezed.</summary>
    private static void FitsAcross(ScrollViewer page, string what)
    {
        var shown = LogicalDescendants<FrameworkElement>(page)
            .Where(element => element is TextBlock or Button or System.Windows.Controls.Primitives.ToggleButton or TextBox or ComboBox && IsShown(element))
            .ToList();
        Assert.NotEmpty(shown);
        foreach (var element in shown)
        {
            var right = element.TranslatePoint(new Point(element.ActualWidth, 0), page).X;
            Assert.True(right <= page.ViewportWidth + 0.5, $"{what}: {Describe(element)} ends at {right} of {page.ViewportWidth}");
            Assert.True(element.ActualWidth + element.Margin.Left + element.Margin.Right >= element.DesiredSize.Width - 0.5, $"{what}: {Describe(element)} is squeezed");
        }
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

    [Theory]
    [InlineData("en-US")]
    [InlineData("zh-Hant")]
    public void Settings_and_Properties_at_150_percent_fit_a_short_screen_with_their_buttons_in_sight(string language)
    {
        // 0.13.0 WP4: the whole dialog is scaled; on a screen too short for it the body scrolls and the buttons stay.
        wpf.Invoke(() =>
        {
            var before = System.Threading.Thread.CurrentThread.CurrentUICulture;
            System.Threading.Thread.CurrentThread.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo(language);
            Services.UiFonts.Configure(null, null, 1.5);
            try
            {
                var settings = new SettingsWindow { DataContext = new SettingsViewModel(new LauncherSettings()) };
                Assert.Equal(1.5, ((FrameworkElement)settings.Content).LayoutTransform.Value.M11);
                settings.Tabs.SelectedIndex = 2; // Keys, the tallest page
                FitsAShortScreen(settings, settings.Body, 600, Strings.Common_Save, Strings.Common_Cancel);

                var properties = new ItemPropertiesWindow
                {
                    DataContext = new ItemPropertiesViewModel(new ShortcutItem { Name = "Tool", Location = @"C:\Tools\tool.exe" }, isNew: false, new NoIcons()),
                };
                properties.Advanced.IsExpanded = true;
                FitsAShortScreen(properties, properties.Body, 600, Strings.Common_Save, Strings.Common_Cancel);
            }
            finally
            {
                Services.UiFonts.Configure(null);
                System.Threading.Thread.CurrentThread.CurrentUICulture = before;
            }
        });
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("zh-Hant")]
    [InlineData("zh-Hans")]
    public void The_headings_of_the_Sync_window_fit_it_and_the_Google_one_says_what_it_is_for_in_a_line_that_wraps(string language)
    {
        // 0.12.2, review 29 N3: "Google 帳戶（用於 Google 雲端硬碟上的副本與 Google 雲端硬…" was cut at the window's edge.
        wpf.Invoke(() =>
        {
            var before = System.Threading.Thread.CurrentThread.CurrentUICulture;
            System.Threading.Thread.CurrentThread.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo(language);
            var window = new SyncWindow();
            try
            {
                window.Show();
                Settle(window);
                var content = (FrameworkElement)window.Content;
                var headed = LogicalDescendants<HeaderedContentControl>(window).Where(control => control is GroupBox or Expander).ToList();
                Assert.Equal(5, headed.Count); // four groups and the credentials
                foreach (var control in headed)
                {
                    var header = Assert.IsType<string>(control.Header);
                    var text = Visuals(control).OfType<TextBlock>().First(block => block.Text == header);
                    var right = text.TranslatePoint(new Point(text.ActualWidth, 0), content).X;
                    Assert.True(right <= content.ActualWidth + 0.5, $"{language}: \"{header}\" ends at {right} of {content.ActualWidth}");
                    Assert.True(text.ActualWidth >= text.DesiredSize.Width - 0.5, $"{language}: \"{header}\" is squeezed");
                }

                var account = headed.OfType<GroupBox>().Single(box => (string)box.Header == Strings.Sync_AccountForBuckets);
                Assert.True(Strings.Sync_AccountForBuckets.Length <= 14, Strings.Sync_AccountForBuckets); // a short heading
                var hint = (TextBlock)window.FindName("AccountHint");
                Assert.Equal(Strings.Sync_AccountForBucketsHint, hint.Text);
                Assert.Equal(TextWrapping.Wrap, hint.TextWrapping);
                Assert.True(hint.TranslatePoint(new Point(hint.ActualWidth, 0), account).X <= account.ActualWidth + 0.5);
            }
            finally
            {
                window.Close();
                System.Threading.Thread.CurrentThread.CurrentUICulture = before;
            }
        });
    }

    private static IEnumerable<DependencyObject> Visuals(DependencyObject root)
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var below in Visuals(child))
            {
                yield return below;
            }
        }
    }

    [Fact]
    public void The_icon_buttons_of_Properties_wrap_beside_the_preview_and_none_is_cut_in_English()
    {
        // Review 28 A2: "Change icon…", "Text or symbol icon…" and "Use default" did not fit; "Use default" was cut.
        wpf.Invoke(() =>
        {
            var before = System.Threading.Thread.CurrentThread.CurrentUICulture;
            System.Threading.Thread.CurrentThread.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo("en-US");
            var window = new ItemPropertiesWindow
            {
                DataContext = new ItemPropertiesViewModel(new ShortcutItem { Name = "Tool", Location = @"C:\Tools\tool.exe" }, isNew: false, new NoIcons()),
            };
            try
            {
                window.Show();
                Settle(window);
                var row = window.IconButtons;
                var buttons = row.Children.OfType<Button>().ToList();
                Assert.Equal([Strings.Props_ChangeIcon, Strings.Props_TextIcon, Strings.Props_UseDefault], buttons.Select(button => (string)button.Content));
                foreach (var button in buttons)
                {
                    var right = button.TranslatePoint(new Point(button.ActualWidth, 0), row).X;
                    Assert.True(right <= row.ActualWidth + 0.5, $"{button.Content} ends at {right} of {row.ActualWidth}");
                    Assert.True(button.ActualWidth + button.Margin.Left + button.Margin.Right >= button.DesiredSize.Width - 0.5, $"{button.Content} is squeezed");
                }
            }
            finally
            {
                window.Close();
                System.Threading.Thread.CurrentThread.CurrentUICulture = before;
            }
        });
    }

    [Fact]
    public void Send_to_PC_opens_at_once_lists_the_PCs_after_it_shows_and_shows_the_menus_choice()
    {
        // Review 27 S2/S3, from the markup: the bindings, styles and resources the dialog uses are all there.
        wpf.Invoke(() =>
        {
            var item = new SpecialIconItem { Name = Strings.SendTo_Name, Kind = DesktopIconGrid.Core.SpecialIcons.SpecialIconKinds.SendToDevice };
            var listed = new TaskCompletionSource<IReadOnlyList<DesktopIconGrid.Core.Sync.InboxDevice>>();
            var viewModel = new SendToDeviceViewModel(item, receives: true, receivesByMenu: false, _ => listed.Task);
            var window = new SendToDeviceWindow { DataContext = viewModel };
            try
            {
                window.Show();
                Assert.True(viewModel.IsLooking); // the window is up while the list is looked for
                var status = (TextBlock)window.FindName("StatusLine");
                Assert.Equal(Strings.SendTo_Looking, status.Text);
                Assert.False(LogicalDescendants<ComboBox>(window).Single().IsEnabled);

                listed.SetResult([new("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "Den"), new("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", "Study")]);
                var deadline = DateTime.UtcNow.AddSeconds(10);
                while (viewModel.IsLooking && DateTime.UtcNow < deadline)
                {
                    Settle(window);
                }

                Assert.False(viewModel.IsLooking);
                Assert.Equal(Strings.SendTo_Hint, status.Text);
                var combo = LogicalDescendants<ComboBox>(window).Single();
                Assert.True(combo.IsEnabled);
                Assert.Equal(2, combo.Items.Count);
                Assert.True(LogicalDescendants<Button>(window).Single(b => Equals(b.Content, Strings.Common_OK)).IsEnabled);
                var box = LogicalDescendants<CheckBox>(window).Single();
                Assert.True(box.IsChecked == true && box.IsEnabled);
                Assert.True(box.ActualWidth <= ((FrameworkElement)window.Content).ActualWidth + 0.5); // the long label wraps
            }
            finally
            {
                window.Close();
            }

            // The menu turns receiving on: the box is ticked, cannot be changed, and says why.
            var byMenu = new SendToDeviceWindow { DataContext = new SendToDeviceViewModel(item, receives: false, receivesByMenu: true, _ => listed.Task) };
            try
            {
                byMenu.Show();
                var box = LogicalDescendants<CheckBox>(byMenu).Single();
                Assert.True(box.IsChecked == true);
                Assert.False(box.IsEnabled);
                Assert.True(LogicalDescendants<TextBlock>(byMenu).Single(t => t.Text == Strings.SendTo_ReceivesByMenu).IsVisible);
            }
            finally
            {
                byMenu.Close();
            }
        });
    }

    [Fact]
    public void The_parameter_rules_are_collapsed_until_More_and_then_four_lines_under_the_hint()
    {
        // 0.12.0 WP2, from the markup: the link, its bindings and the list of rules.
        wpf.Invoke(() =>
        {
            var viewModel = new ItemPropertiesViewModel(new ShortcutItem { Name = "Tool", Location = @"C:\Tools\tool.exe" }, isNew: false, new NoIcons());
            var window = new ItemPropertiesWindow { DataContext = viewModel };
            try
            {
                window.Show();
                Settle(window);
                Assert.Equal(Visibility.Collapsed, window.ParametersRules.Visibility);
                var collapsed = window.ActualHeight;

                viewModel.ToggleParametersRules();
                Settle(window);
                Assert.Equal(Visibility.Visible, window.ParametersRules.Visibility);
                Assert.Equal(4, window.ParametersRules.Items.Count);
                Assert.True(window.ActualHeight > collapsed, $"the window did not grow ({window.ActualHeight} after {collapsed})");
                Assert.Equal(Strings.Props_ParametersLess, string.Concat(window.ParametersRulesLink.Inlines.OfType<System.Windows.Documents.Run>().Select(run => run.Text)));
            }
            finally
            {
                window.Close();
            }
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
