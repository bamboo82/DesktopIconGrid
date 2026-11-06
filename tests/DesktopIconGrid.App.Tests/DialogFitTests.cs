using System.Windows;
using System.Windows.Controls;
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
            FitsAShortScreen(window, window.Body, 400, Strings.Common_Save, Strings.Common_Cancel);
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
