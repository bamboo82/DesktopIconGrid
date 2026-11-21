using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using DesktopIconGrid.Core;
using DesktopIconGrid.Core.Launching;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Core.SpecialIcons;
using DesktopIconGrid.Resources;
using DesktopIconGrid.Services;
using DesktopIconGrid.SpecialIcons;
using DesktopIconGrid.ViewModels;
using DesktopIconGrid.Views;

namespace DesktopIconGrid.App.Tests;

/// <summary>0.15.0 WP7b: the text icon window for a row line's caption and a widget's text, and how they are drawn.</summary>
[Collection(WpfWindowsCollection.Name)]
public sealed class CaptionAndWidgetLookTests(WpfThread wpf) : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("dig-look-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    /// <summary>A widget whose value is fixed: the clock's way (the number style) or a monitor's ring; its look can be set.</summary>
    private sealed class LookTile(string kind, GaugeStyle style, string label, bool hasTextLook = true) : ISpecialIconHandler
    {
        public GaugeState? Last { get; private set; }

        public string Kind => kind;

        public string DisplayName => kind;

        public string Description => kind;

        public bool HasTextLook => hasTextLook;

        public SpecialIconItem CreateItem() => new() { Name = kind, Kind = kind };

        public GaugeState CreateState(SpecialIconItem item) => Last = new GaugeState(kind.ToUpperInvariant(), style, 101, 101, label) { LabelText = label };

        public LaunchResult Activate(SpecialIconItem item, ActivationModifiers modifiers) => LaunchResult.Started;

        public bool ShowProperties(SpecialIconItem item, bool isNew) => false;
    }

    private static TextLookDefaults CaptionDefaults => new(11, FontWeights.SemiBold, new FontFamily("Segoe UI"));

    private static readonly TextLook Full = new() { Font = "Arial", Size = 16, Bold = true, Color = "#FF0000", TileColor = "#2D4A6E", OffsetY = -5 };

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

    private static T Named<T>(DependencyObject root, string name)
        where T : FrameworkElement => Descendants(root).OfType<T>().First(element => element.Name == name);

    private static Color ColourOf(Brush? brush) => Assert.IsType<SolidColorBrush>(brush).Color;

    private static byte[] Render(FrameworkElement element)
    {
        var width = (int)Math.Ceiling(element.ActualWidth);
        var height = (int)Math.Ceiling(element.ActualHeight);
        var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        target.Render(element);
        var pixels = new byte[width * height * 4];
        target.CopyPixels(pixels, width * 4, 0);
        return pixels;
    }

    [Fact]
    public void Each_mode_shows_what_it_sets_and_OK_without_a_change_leaves_no_look()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        wpf.Invoke(() =>
        {
            var caption = TextIconViewModel.ForCaption("工作", null, CaptionDefaults);
            Assert.Equal((Strings.TextIcon_TitleCaption, true, false), (caption.Title, caption.ShowsSides, caption.IsTextReadOnly));
            Assert.Equal((Strings.TextIcon_Caption, Strings.TextIcon_PlateColour), (caption.TextLabel, caption.TileColorLabel));
            Assert.False(caption.CanOverImage);
            Assert.Equal(TextIconViewModel.MaxCaptionLength, caption.MaxTextLength);
            Assert.Equal(Strings.TextIcon_OwnFont, caption.SelectedFont!.DisplayName); // "(as before)", first in the list
            Assert.Same(caption.Fonts[0], caption.SelectedFont);
            Assert.Equal(("11", false), (caption.FontSizeText, caption.Bold)); // semi-bold shows as not bold
            Assert.Equal(("工作", (TextLook?)null), caption.ToCaption());

            var widget = TextIconViewModel.ForWidget("Clock", "14:05", null, new TextLookDefaults(32, FontWeights.Bold, new FontFamily("Segoe UI")));
            Assert.Equal(string.Format(Strings.TextIcon_TitleWidget, "Clock"), widget.Title);
            Assert.Equal((false, true, false), (widget.ShowsSides, widget.IsTextReadOnly, widget.ShowsHint));
            Assert.Equal((Strings.TextIcon_Sample, "14:05", true), (widget.TextLabel, widget.Text, widget.Bold));
            Assert.Null(widget.ToLook());

            var icon = new TextIconViewModel(new TextIcon("A"), "Notes", picture: null, canOverImage: true);
            Assert.Equal((Strings.TextIcon_Title, true, true), (icon.Title, icon.ShowsSides, icon.CanOverImage));

            foreach (var (editor, mode) in new[] { (icon, "icon"), (caption, "caption"), (widget, "widget") })
            {
                var window = new TextIconWindow { DataContext = editor };
                try
                {
                    window.Show();
                    window.UpdateLayout();
                    Assert.Equal(editor.Title, window.Title);
                    Assert.Equal(mode != "widget", window.Sides.IsVisible);
                    Assert.Equal(mode == "widget", window.TextBox.IsReadOnly);
                    Assert.Equal(mode == "icon", window.OverImageBlock.IsVisible);
                    Assert.Equal(mode == "icon", window.Preview.IsVisible);
                    Assert.Equal(mode == "caption", window.CaptionPreview.IsVisible);
                    Assert.Equal(mode == "widget", window.WidgetPreview.IsVisible);
                    Assert.Equal(mode == "caption" ? Strings.TextIcon_PlateColour : Strings.TextIcon_TileColour, window.TileColourLabel.Text);
                }
                finally
                {
                    window.Close();
                }
            }
        });
    }

    [Fact]
    public void A_caption_look_holds_only_what_differs_and_a_symbol_caption_is_drawn_in_the_symbol_font()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        wpf.Invoke(() =>
        {
            var editor = TextIconViewModel.ForCaption("工作", null, CaptionDefaults);
            var font = editor.Fonts[1];
            editor.SelectedFont = font;
            editor.FontSizeText = "16";
            editor.Bold = true;
            editor.Color = "#ff0000";
            editor.TileColor = "#2D4A6E";
            editor.OffsetY = -5;
            Assert.Equal(("工作", new TextLook { Font = font.Source, Size = 16, Bold = true, Color = "#FF0000", TileColor = "#2D4A6E", OffsetY = -5 }), editor.ToCaption());

            editor.FontSizeText = "11"; // the caption's own size and weight again: not written
            editor.Bold = false;
            Assert.Equal(new TextLook { Font = font.Source, Color = "#FF0000", TileColor = "#2D4A6E", OffsetY = -5 }, editor.ToCaption().Look);

            editor.Text = string.Empty; // no caption: OK may still be pressed, and the line stays without one
            Assert.True(editor.CanSave);
            Assert.Equal(string.Empty, editor.ToCaption().Text);

            editor.IsSymbol = true;
            var symbol = editor.Symbols[0];
            editor.SelectedSymbol = symbol;
            var (text, look) = editor.ToCaption();
            Assert.Equal((symbol.Text, SymbolCatalog.FontFamily), (text, look!.Font));

            var reopened = TextIconViewModel.ForCaption(text, look, CaptionDefaults);
            Assert.True(reopened.IsSymbol);
            Assert.Equal(symbol.Text, reopened.SelectedSymbol!.Text);
            Assert.Equal("#FF0000", reopened.Color);

            // What came through sync is shown as the showable check allows, so the box shows what OK saves.
            Assert.Equal("ABC  D", TextIconViewModel.ForCaption("A\u202EB\u0007C\u00A0 D", null, CaptionDefaults).Text);

            // A font the look came with that this PC lacks stays a choice, so OK keeps it.
            var missing = TextIconViewModel.ForCaption("x", new TextLook { Font = "No Such Font 0715" }, CaptionDefaults);
            Assert.Equal(string.Format(Strings.TextIcon_MissingFont, "No Such Font 0715"), missing.SelectedFont!.DisplayName);
            Assert.Equal("No Such Font 0715", missing.ToCaption().Look!.Font);
        });
    }

    [Fact]
    public void The_row_lines_text_opens_the_caption_window_and_saves_its_look_with_the_text()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        wpf.Invoke(() =>
        {
            var model = new LauncherModel();
            var line = new SeparatorItem { Kind = SeparatorItem.RowLine, Name = "工作" };
            model.Add(line);
            using var bar = new TestBar(_directory, model);
            var item = bar.ViewModel.RootItems.Items[0];

            bar.Dialogs.CaptionAnswer = new CaptionAnswer("工作", Full);
            bar.ViewModel.EditSeparatorText(item);
            var saved = (SeparatorItem)model.Items[0];
            Assert.Equal(("工作", (TextLook?)Full), (saved.Name, saved.CaptionLook));
            Assert.Equal(("工作", (TextLook?)null), bar.Dialogs.OfferedCaption);
            Assert.True(item.HasLookedRowLineText);
            Assert.False(item.HasPlainRowLineText);

            bar.Dialogs.CaptionAnswer = null; // Cancel
            bar.ViewModel.EditItem(item); // a double-click opens the same window
            Assert.Equal(2, bar.Dialogs.CaptionQuestions);
            Assert.Equal(Full, saved.CaptionLook);

            bar.Dialogs.CaptionAnswer = new CaptionAnswer(string.Empty, Full); // no caption, and its look goes with it
            bar.ViewModel.EditSeparatorText(item);
            Assert.Equal((string.Empty, (TextLook?)null), (saved.Name, saved.CaptionLook));
            Assert.False(item.HasRowLineText);
        });
    }

    [Fact]
    public void A_widgets_look_is_saved_from_its_menu_and_drawn_without_making_its_gauge_anew()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        wpf.Invoke(() =>
        {
            var clock = new LookTile("clock-like", GaugeStyle.Number, "14:05");
            var plain = new LookTile("no-look", GaugeStyle.Number, "1", hasTextLook: false);
            var model = new LauncherModel();
            model.Add(clock.CreateItem());
            model.Add(plain.CreateItem());
            using var bar = new TestBar(_directory, model, handlers: [clock, plain]);
            var item = bar.ViewModel.RootItems.Items[0];
            Assert.True(item.CanEditTextLook);
            Assert.False(bar.ViewModel.RootItems.Items[1].CanEditTextLook);
            var gauge = item.Gauge!;

            var look = new TextLook { Color = "#00FF00", OffsetY = 5 };
            bar.Dialogs.LookAnswer = new LookAnswer(look);
            bar.ViewModel.EditTextLook(item);

            Assert.Equal(look, ((SpecialIconItem)model.Items[0]).TextLook);
            Assert.Same(gauge, item.Gauge); // drawn at once, the same gauge: a command tile would not run again
            Assert.Equal(look, gauge.Look!.Look);
            var offered = bar.Dialogs.OfferedLook!.Value;
            Assert.Equal(("clock-like", "14:05", gauge.LabelFontSize, true), (offered.Name, offered.Sample, offered.Defaults.Size, offered.Defaults.Bold));

            bar.Dialogs.LookAnswer = new LookAnswer(null); // back to the text as before
            bar.ViewModel.EditTextLook(item);
            Assert.Null(((SpecialIconItem)model.Items[0]).TextLook);
            Assert.Null(gauge.Look);
        });
    }

    [Fact]
    public void A_look_on_the_clock_and_a_ring_is_drawn_and_without_one_they_are_drawn_as_before_pixel_for_pixel()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var clock = new LookTile("clock-like", GaugeStyle.Number, "14:05");
        var ring = new LookTile("ring", GaugeStyle.Ring, "37%");
        var model = new LauncherModel();
        model.Add(clock.CreateItem());
        model.Add(ring.CreateItem());
        TestBar? bar = null;
        Window? window = null;
        IconGridControl? grid = null;
        try
        {
            wpf.Invoke(() =>
            {
                bar = new TestBar(_directory, model, handlers: [clock, ring]);
                grid = new IconGridControl { DataContext = bar.ViewModel.RootItems };
                window = new Window { Width = 300, Height = 160, Content = grid };
                window.Show();
            });
            wpf.Settle();
            byte[]? before = null;
            wpf.Invoke(() =>
            {
                window!.UpdateLayout();
                // Each tile's template holds the three styles, one shown: the texts are told apart by what they show.
                var number = Descendants(window).OfType<TextBlock>().Single(block => block.Name == "NumberText" && block.Text == "14:05");
                var ringText = Descendants(window).OfType<TextBlock>().Single(block => block.Name == "RingText" && block.Text == "37%");
                var fit = (Viewbox)VisualTreeHelper.GetParent(number);

                // As the template drew them before 0.15.0.
                Assert.Equal((clock.Last!.LabelFontSize, FontWeights.Bold), (number.FontSize, number.FontWeight));
                Assert.Same(clock.Last.LevelBrush, number.Foreground);
                Assert.Equal((26.0, FontWeights.SemiBold), (ringText.FontSize, ringText.FontWeight));
                Assert.Same(Application.Current.FindResource("Theme.Foreground"), ringText.Foreground);
                Assert.True(Descendants(window).OfType<Border>().Where(b => b.Name == "GaugePlate").All(b => !b.IsVisible));
                Assert.Equal("NumberFit", fit.Name);
                Assert.Equal(Transform.Identity, fit.RenderTransform);
                before = Render(grid!);

                bar!.Dialogs.LookAnswer = new LookAnswer(new TextLook { Font = "Arial", Size = 30, Bold = false, Color = "#FFEE00", TileColor = "#2D4A6E", OffsetY = -10 });
                bar.ViewModel.EditTextLook(bar.ViewModel.RootItems.Items[0]);
                bar.Dialogs.LookAnswer = new LookAnswer(new TextLook { Size = 72, Color = "#202060" });
                bar.ViewModel.EditTextLook(bar.ViewModel.RootItems.Items[1]);
                window.UpdateLayout();

                Assert.StartsWith("Arial, ", number.FontFamily.Source, StringComparison.Ordinal);
                Assert.Equal((30.0, FontWeights.Normal), (number.FontSize, number.FontWeight));
                Assert.Equal(Color.FromRgb(0xFF, 0xEE, 0x00), ColourOf(number.Foreground));
                var plate = Descendants(window).OfType<Border>().First(b => b.Name == "GaugePlate" && b.IsVisible);
                Assert.Equal(Color.FromRgb(0x2D, 0x4A, 0x6E), ColourOf(plate.Background));
                Assert.Equal(-10, Assert.IsType<TranslateTransform>(fit.RenderTransform).Y);
                Assert.Null(number.Effect); // on a plate the text needs no edge

                Assert.True(ringText.FontSize < 72, $"the ring's value at {ringText.FontSize}"); // made smaller to fit inside the ring
                ringText.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Assert.True(ringText.DesiredSize.Width <= GaugeState.RingTextWidth + 1, $"{ringText.DesiredSize.Width} wide");
                Assert.Equal(Color.FromRgb(0x20, 0x20, 0x60), ColourOf(ringText.Foreground));
                Assert.NotEqual(before, Render(grid!));

                // Taken away: pixel for pixel as before.
                bar.Dialogs.LookAnswer = new LookAnswer(null);
                bar.ViewModel.EditTextLook(bar.ViewModel.RootItems.Items[0]);
                bar.ViewModel.EditTextLook(bar.ViewModel.RootItems.Items[1]);
                window.UpdateLayout();
                Assert.Equal(before, Render(grid!));
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

    [Fact]
    public void The_warning_and_alarm_colours_win_over_the_looks_colour_and_the_normal_state_takes_it()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var clock = new LookTile("clock-like", GaugeStyle.Number, "0:42");
        var model = new LauncherModel();
        var item = clock.CreateItem();
        item.TextLook = new TextLook { Color = "#00AAFF" };
        model.Add(item);
        TestBar? bar = null;
        Window? window = null;
        try
        {
            wpf.Invoke(() =>
            {
                bar = new TestBar(_directory, model, handlers: [clock]);
                window = new Window { Width = 200, Height = 120, Content = new IconGridControl { DataContext = bar.ViewModel.RootItems } };
                window.Show();
            });
            wpf.Settle();
            wpf.Invoke(() =>
            {
                var number = Descendants(window!).OfType<TextBlock>().Single(block => block.Name == "NumberText");
                var gauge = clock.Last!;
                var blue = Color.FromRgb(0x00, 0xAA, 0xFF);

                foreach (var (level, expected) in new (CommandLevel, Color?)[]
                {
                    (CommandLevel.Ok, blue), // a running timer's green is the normal state
                    (CommandLevel.Warning, Color.FromRgb(0xF5, 0xA6, 0x23)),
                    (CommandLevel.Failed, Color.FromRgb(0xE5, 0x48, 0x4D)),
                    (CommandLevel.Unknown, blue), // an idle clock's grey too
                })
                {
                    gauge.Level = level;
                    window!.UpdateLayout();
                    Assert.Equal(expected, ColourOf(number.Foreground));
                    Assert.Equal(level is CommandLevel.Warning or CommandLevel.Failed, gauge.IsAlarm);
                }
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

    [Fact]
    public void A_row_lines_caption_with_a_look_is_drawn_with_it_and_without_one_as_before_pixel_for_pixel()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var model = new LauncherModel();
        model.Add(new SeparatorItem { Kind = SeparatorItem.RowLine, Name = "工作" });
        model.Add(new ShortcutItem { Name = "Tool", Location = @"C:\tool.exe" });
        TestBar? bar = null;
        Window? window = null;
        IconGridControl? grid = null;
        try
        {
            wpf.Invoke(() =>
            {
                bar = new TestBar(_directory, model);
                grid = new IconGridControl { DataContext = bar.ViewModel.RootItems };
                window = new Window { Width = 320, Height = 200, Content = grid };
                window.Show();
            });
            wpf.Settle();
            wpf.Invoke(() =>
            {
                window!.UpdateLayout();
                var looked = Named<Viewbox>(window, "LookedCaption");
                Assert.False(looked.IsVisible); // the caption as before: the template's own text
                var before = Render(grid!);
                var item = bar!.ViewModel.RootItems.Items[0];

                bar.Dialogs.CaptionAnswer = new CaptionAnswer("工作", Full with { OffsetY = 0 });
                bar.ViewModel.EditSeparatorText(item);
                window.UpdateLayout();
                Assert.True(looked.IsVisible);
                var text = Descendants(looked).OfType<LookedText>().Single();
                var inner = text.TextElementForTests;
                Assert.StartsWith("Arial, ", inner.FontFamily.Source, StringComparison.Ordinal);
                Assert.Equal((16.0, FontWeights.Bold), (inner.FontSize, inner.FontWeight));
                Assert.Equal(Color.FromRgb(0xFF, 0x00, 0x00), ColourOf(inner.Foreground));
                Assert.Equal(Color.FromRgb(0x2D, 0x4A, 0x6E), ColourOf(text.PlateForTests.Background));
                Assert.Equal(new Thickness(TextOverPicture.PlatePadding), text.PlateForTests.Padding);
                var strip = (FrameworkElement)VisualTreeHelper.GetParent(looked);
                var centred = looked.TranslatePoint(default, strip).Y;

                bar.Dialogs.CaptionAnswer = new CaptionAnswer("工作", Full with { OffsetY = 20 });
                bar.ViewModel.EditSeparatorText(item);
                window.UpdateLayout();
                Assert.Equal(centred + (strip.ActualHeight * 0.2), looked.TranslatePoint(default, strip).Y, 1); // a fifth of the strip down

                bar.Dialogs.CaptionAnswer = new CaptionAnswer("工作", null);
                bar.ViewModel.EditSeparatorText(item);
                window.UpdateLayout();
                Assert.False(looked.IsVisible);
                Assert.Equal(before, Render(grid!));
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

    [Fact]
    public void Looked_text_keeps_the_see_through_edge_in_the_tone_of_its_colour_and_none_on_a_plate()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        wpf.Invoke(() =>
        {
            var theme = new DropShadowEffect();
            var underLight = new DropShadowEffect();
            var underDark = new DropShadowEffect();
            var text = new LookedText { Text = "工作" };
            var host = new Border { Child = text };
            host.Resources[MainWindow.TextEdgeKey] = theme;
            host.Resources[MainWindow.LightTextEdgeKey] = underLight;
            host.Resources[MainWindow.DarkTextEdgeKey] = underDark;
            TextElement.SetFontFamily(host, new FontFamily("Segoe UI"));
            host.Measure(new Size(200, 50));

            Assert.Same(theme, text.TextElementForTests.Effect); // the theme's colour: the theme's edge, as the caption had
            text.Look = new TextLook { Color = "#FFEE00" };
            Assert.Same(underLight, text.TextElementForTests.Effect);
            text.Look = new TextLook { Color = "#202060" };
            Assert.Same(underDark, text.TextElementForTests.Effect);
            text.Look = new TextLook { TileColor = "#FFFFFF" };
            Assert.Null(text.TextElementForTests.Effect);
            Assert.Equal(ThemeManager.ToColor(ThemeCatalog.TextOnTile("#FFFFFF")), ColourOf(text.TextElementForTests.Foreground)); // the plate's contrast colour

            text.Look = new TextLook { Font = "Arial", OffsetY = 10 };
            text.ShiftBase = 100;
            Assert.Equal("Arial, Segoe UI", text.TextElementForTests.FontFamily.Source);
            Assert.Equal(10, Assert.IsType<TranslateTransform>(text.RenderTransform).Y);
        });
    }
}
