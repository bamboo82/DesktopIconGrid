using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DesktopIconGrid.Core;
using DesktopIconGrid.Core.Launching;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Core.SpecialIcons;
using DesktopIconGrid.SpecialIcons;
using DesktopIconGrid.Views;

namespace DesktopIconGrid.App.Tests;

/// <summary>
/// The mouse wheel over a tile that takes it, in a grid built from its markup (review 27 F1): the event's source is
/// an element of the gauge's drawing, whose data is the gauge, not the icon.
/// </summary>
[Collection(WpfWindowsCollection.Name)]
public sealed class WheelOverTileTests(WpfThread wpf) : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("dig-wheel-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    /// <summary>A tile with a ring that takes the wheel, as the volume tile does, and remembers the notches it got.</summary>
    private sealed class WheelTile : ISpecialIconHandler
    {
        public List<int> Notches { get; } = [];

        public string Kind => "test-wheel";

        public string DisplayName => "Wheel";

        public string Description => "Takes the wheel";

        public SpecialIconItem CreateItem() => new() { Name = "Wheel", Kind = Kind };

        public GaugeState CreateState(SpecialIconItem item)
        {
            var state = new GaugeState("VOL", GaugeStyle.Ring, 101, 101, "50");
            state.Update(50, "50");
            return state;
        }

        public LaunchResult Activate(SpecialIconItem item, ActivationModifiers modifiers) => LaunchResult.Started;

        public bool ShowProperties(SpecialIconItem item, bool isNew) => false;

        public bool Wheel(SpecialIconItem item, int notches)
        {
            if (notches != 0)
            {
                Notches.Add(notches);
            }

            return true;
        }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var deeper in Descendants(child))
            {
                yield return deeper;
            }
        }
    }

    /// <summary>A wheel event as Windows raises it, from <paramref name="source"/> up: the preview first, then the bubbling one.</summary>
    private static bool Turn(UIElement source, int delta)
    {
        var preview = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, delta) { RoutedEvent = UIElement.PreviewMouseWheelEvent };
        source.RaiseEvent(preview);
        return preview.Handled;
    }

    [Fact]
    public void A_turn_over_a_tiles_drawing_turns_the_tile_and_one_that_began_over_another_icon_scrolls_the_bar()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var tile = new WheelTile();
        var model = new LauncherModel();
        model.Add(new ShortcutItem { Name = "Tool", Location = @"C:\tool.exe" });
        model.Add(tile.CreateItem());
        TestBar? bar = null;
        Window? window = null;
        try
        {
            wpf.Invoke(() =>
            {
                bar = new TestBar(_directory, model, handlers: [tile]);
                window = new Window { Width = 400, Height = 200, Content = new IconGridControl { DataContext = bar.ViewModel.RootItems } };
                window.Show();
            });
            wpf.Settle();
            wpf.Invoke(() =>
            {
                window!.UpdateLayout();
                var items = bar!.ViewModel.RootItems.Items;
                var drawing = Descendants(window).OfType<FrameworkElement>().First(element => element.DataContext is GaugeState);
                var ring = Descendants(drawing).OfType<TextBlock>().First(); // the ring's number: its data is the gauge
                var tool = Descendants(window).OfType<FrameworkElement>()
                    .First(element => element.DataContext == items[0] && VisualTreeHelper.GetChildrenCount(element) == 0);
                Assert.Same(items[1], IconGridControl.ItemAt(ring));

                Assert.True(Turn(ring, 120)); // taken: the bar does not scroll
                Assert.Equal([1], tile.Notches);
                Assert.True(Turn(ring, -240));
                Assert.Equal([1, -2], tile.Notches);

                // A scroll that began over another icon goes on scrolling the bar when the tile comes under the pointer.
                Assert.False(Turn(tool, 120));
                Assert.False(Turn(ring, 120));
                Assert.Equal([1, -2], tile.Notches);
            });
        }
        finally
        {
            wpf.Invoke(() =>
            {
                window?.Close();
                bar?.Dispose();
            });
        }
    }
}
