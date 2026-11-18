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

/// <summary>0.12.0 WP4 (U3): the names table built from its markup, with its usage columns and the line under it.</summary>
[Collection(WpfWindowsCollection.Name)]
public sealed class NameTableWindowTests(WpfThread wpf) : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("dig-names-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void The_table_opens_1060_wide_within_the_work_area_with_the_usage_columns_and_the_line_under_it()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        wpf.Invoke(() =>
        {
            var model = new LauncherModel();
            model.Add(new ShortcutItem { Name = "Notepad", Location = @"C:\Windows\notepad.exe" });
            using var bar = new TestBar(_directory, model);
            var window = new NameTableWindow { DataContext = new NameTableViewModel(bar.ViewModel) };
            try
            {
                window.Show();
                window.UpdateLayout();
                var frame = new DispatcherFrame();
                window.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () => frame.Continue = false);
                Dispatcher.PushFrame(frame);
                window.UpdateLayout();

                Assert.Equal(Math.Min(1060, window.MaxWidth), window.ActualWidth, 1.0);
                var headers = window.Grid.Columns.Select(column => column.Header as string).ToList();
                var colourTag = headers.IndexOf(Strings.Names_ColorTag);
                Assert.Equal([Strings.Names_Uses, Strings.Names_LastUsed], headers.Skip(colourTag + 1).Take(2));
                Assert.Equal(160, window.Grid.Columns[^1].MinWidth);
                Assert.Equal(120, window.Grid.Columns[^2].Width.Value);

                Assert.Equal(Visibility.Visible, window.UsageLine.Visibility);
                Assert.StartsWith(Strings.Names_UsageSince[..Strings.Names_UsageSince.IndexOf('{', StringComparison.Ordinal)], window.UsageLine.Text, StringComparison.Ordinal);
            }
            finally
            {
                window.Close();
            }
        });
    }
}
