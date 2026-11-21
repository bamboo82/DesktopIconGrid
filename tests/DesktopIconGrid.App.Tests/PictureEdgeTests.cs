using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using DesktopIconGrid.Core;
using DesktopIconGrid.Core.Launching;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Core.SpecialIcons;
using DesktopIconGrid.SpecialIcons;
using DesktopIconGrid.Views;

namespace DesktopIconGrid.App.Tests;

/// <summary>Review 41 U2: over a background picture a widget's text and an icon's glyph take a stronger edge.</summary>
[Collection(WpfWindowsCollection.Name)]
public sealed class PictureEdgeTests(WpfThread wpf) : IDisposable
{
    private static readonly Color NearBlack = Color.FromRgb(0x1A, 0x1A, 0x1A);

    private readonly string _directory = Directory.CreateTempSubdirectory("dig-edge-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    /// <summary>A number tile whose level the test sets.</summary>
    private sealed class LevelTile : ISpecialIconHandler
    {
        public GaugeState? Last { get; private set; }

        public string Kind => "level";

        public string DisplayName => "level";

        public string Description => "level";

        public SpecialIconItem CreateItem() => new() { Name = "level", Kind = Kind };

        public GaugeState CreateState(SpecialIconItem item) => Last = new GaugeState("LVL", GaugeStyle.Number, 101, 101, "7") { LabelText = "7", Level = CommandLevel.Ok };

        public LaunchResult Activate(SpecialIconItem item, ActivationModifiers modifiers) => LaunchResult.Started;

        public bool ShowProperties(SpecialIconItem item, bool isNew) => false;
    }

    private static DropShadowEffect Edge(ResourceDictionary resources, string key) => Assert.IsType<DropShadowEffect>(resources[key]);

    [Fact]
    public void Over_a_picture_glyphs_and_gauge_text_have_a_full_strength_edge_twice_as_wide_and_the_names_keep_theirs()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        wpf.Invoke(() =>
        {
            var resources = new ResourceDictionary();
            MainWindow.SetSeeThroughLook(resources, "#FFFFFF", 40, picture: true);

            var names = Edge(resources, MainWindow.TextEdgeKey);
            Assert.Equal((0.95, MainWindow.TextEdgeBlur), (names.Opacity, names.BlurRadius)); // the names keep theirs

            var glyph = Edge(resources, MainWindow.GlyphEdgeKey);
            Assert.Equal((1.0, MainWindow.TextEdgeBlur * 2, NearBlack), (glyph.Opacity, glyph.BlurRadius, glyph.Color));

            var box = 100.0 / 40;
            var gauge = Edge(resources, MainWindow.GaugeTextEdgeKey);
            Assert.Equal(1.0, gauge.Opacity);
            Assert.Equal(MainWindow.TextEdgeBlur * 2 * box, gauge.BlurRadius, 6);
            var neutral = Edge(resources, MainWindow.GaugeNeutralEdgeKey);
            Assert.Equal((1.0, NearBlack), (neutral.Opacity, neutral.Color)); // the clock's grey: dark, against the grey itself
            Assert.Equal((1.0, NearBlack), (Edge(resources, MainWindow.GaugeLevelLightEdgeKey).Opacity, Edge(resources, MainWindow.GaugeLevelLightEdgeKey).Color));
            Assert.Equal(Colors.White, Edge(resources, MainWindow.GaugeLevelDarkEdgeKey).Color);
            Assert.Equal(1.0, Edge(resources, MainWindow.GaugeLightTextEdgeKey).Opacity); // a widget's chosen colour too (WP7b)

            // See-through without a picture: as before, the glyphs on the names' very edge, no edge for green, orange and red.
            MainWindow.SetSeeThroughLook(resources, "#FFFFFF", 40);
            Assert.Same(resources[MainWindow.TextEdgeKey], resources[MainWindow.GlyphEdgeKey]);
            Assert.Equal(0.95, Edge(resources, MainWindow.GaugeTextEdgeKey).Opacity);
            Assert.Equal(MainWindow.TextEdgeBlur * box, Edge(resources, MainWindow.GaugeTextEdgeKey).BlurRadius, 6);
            Assert.False(resources.Contains(MainWindow.GaugeLevelLightEdgeKey));
            Assert.False(resources.Contains(MainWindow.GaugeLevelDarkEdgeKey));

            MainWindow.SetSeeThroughLook(resources, null, 40);
            Assert.False(resources.Contains(MainWindow.GlyphEdgeKey));
            Assert.False(resources.Contains(MainWindow.GaugeTextEdgeKey));
        });
    }

    [Fact]
    public void Green_and_orange_values_take_the_dark_edge_and_red_the_white_one_only_over_a_picture()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var tile = new LevelTile();
        var model = new LauncherModel();
        model.Add(tile.CreateItem());
        model.Add(new ShortcutItem { Name = "Tool", Location = @"C:\tool.exe", TextIcon = BuiltInTextIcons.Voice });
        TestBar? bar = null;
        Window? window = null;
        try
        {
            wpf.Invoke(() =>
            {
                bar = new TestBar(_directory, model, handlers: [tile]);
                window = new Window { Width = 240, Height = 140, Content = new IconGridControl { DataContext = bar.ViewModel.RootItems } };
                window.Show();
            });
            wpf.Settle();
            wpf.Invoke(() =>
            {
                static IEnumerable<DependencyObject> Descendants(DependencyObject root)
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

                var number = Descendants(window!).OfType<TextBlock>().Single(block => block.Name == "NumberText" && block.Text == "7");
                var glyph = Descendants(window!).OfType<TextIconTile>().Single(t => t.Icon == BuiltInTextIcons.Voice);
                var gauge = tile.Last!;
                Assert.Null(number.Effect);

                MainWindow.SetSeeThroughLook(window!.Resources, "#FFFFFF", bar!.ViewModel.IconSize);
                window.UpdateLayout();
                Assert.Null(number.Effect); // see-through alone: green reads without one, as before
                Assert.Same(window.Resources[MainWindow.TextEdgeKey], glyph.FitBox!.Effect);

                MainWindow.SetSeeThroughLook(window.Resources, "#FFFFFF", bar.ViewModel.IconSize, picture: true);
                window.UpdateLayout();
                Assert.True(gauge.IsLightLevel);
                Assert.Same(window.Resources[MainWindow.GaugeLevelLightEdgeKey], number.Effect); // green: near-black
                Assert.Same(window.Resources[MainWindow.GlyphEdgeKey], glyph.FitBox!.Effect);
                Assert.NotSame(window.Resources[MainWindow.TextEdgeKey], glyph.FitBox.Effect);

                gauge.Level = CommandLevel.Warning;
                window.UpdateLayout();
                Assert.Same(window.Resources[MainWindow.GaugeLevelLightEdgeKey], number.Effect); // orange: near-black

                gauge.Level = CommandLevel.Failed;
                window.UpdateLayout();
                Assert.False(gauge.IsLightLevel);
                Assert.Same(window.Resources[MainWindow.GaugeLevelDarkEdgeKey], number.Effect); // red: white

                gauge.Level = CommandLevel.Unknown;
                window.UpdateLayout();
                Assert.Same(window.Resources[MainWindow.GaugeNeutralEdgeKey], number.Effect); // the grey keeps its own
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
