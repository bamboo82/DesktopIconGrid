using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DesktopIconGrid.Core;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Resources;
using DesktopIconGrid.ViewModels;
using DesktopIconGrid.Views;

namespace DesktopIconGrid.App.Tests;

/// <summary>
/// 0.12.0 WP4 (U3): the names table built from its markup, with its usage columns and the line under it; its column
/// widths and how it opens within the work area (release check V1, U3).
/// </summary>
[Collection(WpfWindowsCollection.Name)]
public sealed class NameTableWindowTests(WpfThread wpf) : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("dig-names-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void The_table_opens_1230_wide_within_the_work_area_with_the_usage_columns_and_the_line_under_it()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        wpf.Invoke(() =>
        {
            var window = Open();
            try
            {
                // Release check U3: a window the user sizes only opens within the work area; no limit stays on it.
                var expected = Math.Min(1230, Math.Max(320, SystemParameters.WorkArea.Width - 16));
                Assert.Equal(expected, window.ActualWidth, 1.0);
                Assert.True(double.IsPositiveInfinity(window.MaxWidth), $"MaxWidth {window.MaxWidth}");
                Assert.True(double.IsPositiveInfinity(window.MaxHeight), $"MaxHeight {window.MaxHeight}");

                var headers = window.Grid.Columns.Select(column => column.Header as string).ToList();
                var colourTag = headers.IndexOf(Strings.Names_ColorTag);
                Assert.Equal([Strings.Names_Uses, Strings.Names_LastUsed], headers.Skip(colourTag + 1).Take(2));

                // Review 28 A3: Modified is the row's text in Windows' regional format, not a StringFormat of the
                // interface language.
                var modified = Assert.IsType<DataGridTextColumn>(window.Grid.Columns.Single(column => (column.Header as string) == Strings.Common_Modified));
                Assert.Equal(nameof(NameRowViewModel.ModifiedText), Assert.IsType<System.Windows.Data.Binding>(modified.Binding).Path.Path);

                Assert.Equal(Visibility.Visible, window.UsageLine.Visibility);
                Assert.StartsWith(Strings.Names_UsageSince[..Strings.Names_UsageSince.IndexOf('{', StringComparison.Ordinal)], window.UsageLine.Text, StringComparison.Ordinal);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void Every_column_keeps_its_minimum_and_a_narrow_table_scrolls_sideways()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        wpf.Invoke(() =>
        {
            var window = Open();
            try
            {
                // Release check V1: widths and minimums, Location last taking the rest; Kind and Colour tag as English
                // needs them (0.12.1, review 28 A1).
                Assert.Equal(
                    [(160.0, 120.0), (120.0, 90.0), (120.0, 90.0), (110.0, 72.0), (72.0, 60.0), (96.0, 72.0), (48.0, 48.0), (80.0, 80.0), (170.0, 170.0)],
                    window.Grid.Columns.Take(9).Select(column => (column.Width.Value, column.MinWidth)));
                Assert.True(window.Grid.Columns[^1].Width.IsStar);
                Assert.Equal(160, window.Grid.Columns[^1].MinWidth);

                foreach (var width in new[] { 1230.0, 700 })
                {
                    window.Width = width;
                    Settle(window);
                    foreach (var column in window.Grid.Columns)
                    {
                        Assert.True(column.ActualWidth >= column.MinWidth - 0.5, $"at {width}: {column.Header} is {column.ActualWidth}, less than {column.MinWidth}");
                    }
                }

                var scroller = Descendants(window.Grid).OfType<ScrollViewer>().First();
                Assert.True(scroller.ScrollableWidth > 0, $"at 700 the table should scroll sideways: {scroller.ExtentWidth} in {scroller.ViewportWidth}");
                Assert.Equal(Visibility.Visible, scroller.ComputedHorizontalScrollBarVisibility);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private NameTableWindow Open()
    {
        var model = new LauncherModel();
        model.Add(new ShortcutItem { Name = "Notepad", Location = @"C:\Windows\notepad.exe" });
        var bar = new TestBar(_directory, model);
        var window = new NameTableWindow { DataContext = new NameTableViewModel(bar.ViewModel) };
        window.Closed += (_, _) => bar.Dispose();
        window.Show();
        Settle(window);
        return window;
    }

    private static void Settle(Window window)
    {
        window.UpdateLayout();
        var frame = new DispatcherFrame();
        window.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () => frame.Continue = false);
        Dispatcher.PushFrame(frame);
        window.UpdateLayout();
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var below in Descendants(child))
            {
                yield return below;
            }
        }
    }
}
