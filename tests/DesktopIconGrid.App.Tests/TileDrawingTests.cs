using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;
using DesktopIconGrid.Core;
using DesktopIconGrid.Core.Launching;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Core.SpecialIcons;
using DesktopIconGrid.SpecialIcons;
using DesktopIconGrid.Views;

namespace DesktopIconGrid.App.Tests;

/// <summary>Tiles in a grid built from its markup: what the release check of 0.11.0 saw on the screen (N1, N3).</summary>
[Collection(WpfWindowsCollection.Name)]
public sealed class TileDrawingTests(WpfThread wpf) : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("dig-tiles-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    /// <summary>A tile drawn in the number style with a fixed text, as a gauge at 100 % or the network tile at 999K.</summary>
    private sealed class NumberTile(string kind, string label) : ISpecialIconHandler
    {
        public string Kind => kind;

        public string DisplayName => kind;

        public string Description => kind;

        public SpecialIconItem CreateItem() => new() { Name = kind, Kind = kind };

        public GaugeState CreateState(SpecialIconItem item) => new("CPU", GaugeStyle.Number, 101, 101, label) { LabelText = label };

        public LaunchResult Activate(SpecialIconItem item, ActivationModifiers modifiers) => LaunchResult.Started;

        public bool ShowProperties(SpecialIconItem item, bool isNew) => false;
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

    private static T Ancestor<T>(DependencyObject element)
        where T : DependencyObject
    {
        for (var node = VisualTreeHelper.GetParent(element); node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is T found)
            {
                return found;
            }
        }

        throw new InvalidOperationException($"No {typeof(T).Name} above it");
    }

    [Fact]
    public void A_number_shrinks_to_fit_its_tile_instead_of_being_cut_and_each_tile_is_announced_by_its_name()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        ISpecialIconHandler[] handlers = [new NumberTile("hundred", "100%"), new NumberTile("rate", "999K")];
        var model = new LauncherModel();
        model.Add(new ShortcutItem { Name = "Tool", Location = @"C:\tool.exe" });
        foreach (var handler in handlers)
        {
            model.Add(handler.CreateItem());
        }

        TestBar? bar = null;
        Window? window = null;
        try
        {
            wpf.Invoke(() =>
            {
                bar = new TestBar(_directory, model, handlers: handlers);
                window = new Window { Width = 400, Height = 200, Content = new IconGridControl { DataContext = bar.ViewModel.RootItems } };
                window.Show();
            });
            wpf.Settle();
            wpf.Invoke(() =>
            {
                window!.UpdateLayout();

                // N1: in Segoe UI Bold at 42 "100%" is about 109 units wide and "999K" about 100, over the 98 there are.
                foreach (var label in new[] { "100%", "999K" })
                {
                    var text = Descendants(window).OfType<TextBlock>().Single(block => block.Name == "NumberText" && block.Text == label);
                    var fit = Ancestor<Viewbox>(text);
                    Assert.Equal("NumberFit", fit.Name);
                    Assert.True(text.DesiredSize.Width < text.MaxWidth, $"{label}: {text.DesiredSize.Width} wide, trimmed at {text.MaxWidth}");
                    Assert.True(fit.ActualWidth <= 98.5, $"{label}: drawn {fit.ActualWidth} wide");
                    Assert.True(fit.ActualWidth < text.DesiredSize.Width, $"{label}: not shrunk ({fit.ActualWidth} of {text.DesiredSize.Width})");
                }

                // N3: UI Automation's items are named after the tiles, not after the view model's type.
                var host = Descendants(window).OfType<ItemsControl>().Single(control => control.Name == "ItemsHost");
                var items = UIElementAutomationPeer.CreatePeerForElement(host).GetChildren();
                Assert.Equal(["Tool", "hundred", "rate"], items.Select(peer => peer.GetName()));
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
