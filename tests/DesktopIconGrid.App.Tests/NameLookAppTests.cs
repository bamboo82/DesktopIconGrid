using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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

/// <summary>0.16.0 WP4, the owner's request: a widget's name has a 文字外觀 of its own.</summary>
[Collection(WpfWindowsCollection.Name)]
public sealed class NameLookAppTests(WpfThread wpf) : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("dig-name-look-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    /// <summary>A widget whose value is fixed, in the number or the bar style, whose looks can be set.</summary>
    private sealed class LookTile(string kind, GaugeStyle style, string label) : ISpecialIconHandler
    {
        public GaugeState? Last { get; private set; }

        public string Kind => kind;

        public string DisplayName => kind;

        public string Description => kind;

        public bool HasTextLook => true;

        public SpecialIconItem CreateItem() => new() { Name = kind, Kind = kind };

        public GaugeState CreateState(SpecialIconItem item) => Last = new GaugeState(kind.ToUpperInvariant(), style, 101, 101, label) { LabelText = label };

        public LaunchResult Activate(SpecialIconItem item, ActivationModifiers modifiers) => LaunchResult.Started;

        public bool ShowProperties(SpecialIconItem item, bool isNew) => false;
    }

    private static readonly TextLook Name = new() { Font = "Georgia", Size = 14, Bold = true, Color = "#FFEE00", TileColor = "#2D4A6E", OffsetY = 10 };

    private static TextLookDefaults ValueDefaults => new(42, FontWeights.Bold, new FontFamily("Segoe UI"));

    private static TextLookDefaults NameDefaults => new(11, FontWeights.Normal, new FontFamily("Segoe UI"));

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

    private static Color ColourOf(Brush? brush) => Assert.IsType<SolidColorBrush>(brush).Color;

    /// <summary>The nearest element of type <typeparamref name="T"/> above <paramref name="element"/> (a Viewbox puts a visual between).</summary>
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

    private static Color Hex(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    [Fact]
    public void The_window_has_a_value_part_and_a_name_part_each_with_its_own_look_and_OK_saves_both()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        wpf.Invoke(() =>
        {
            var parts = new WidgetLookParts(
                TextIconViewModel.ForWidget("Clock", "14:05", new TextLook { OffsetY = 5 }, ValueDefaults),
                TextIconViewModel.ForWidgetName("Clock", null, NameDefaults),
                isClock: true);
            Assert.Equal((Strings.TextLook_PartTime, Strings.TextLook_PartName), (parts.ValuePartLabel, parts.NamePartLabel));
            Assert.Equal(Strings.TextLook_PartValue, new WidgetLookParts(parts.Value, parts.Name, isClock: false).ValuePartLabel);
            var name = parts.Name;
            Assert.Equal((parts.Value.Title, Strings.TextLook_NameLabel, Strings.TextIcon_PlateColour), (name.Title, name.TextLabel, name.TileColorLabel));
            Assert.Equal(Strings.TextIcon_Font[^1], name.TextLabel[^1]); // the colon the rows under it have (review 43 V2)
            Assert.NotEqual(Strings.TextLook_PartName[^1], name.TextLabel[^1]); // the part's own button has none
            Assert.Equal((false, true, false, "Clock"), (name.ShowsSides, name.IsTextReadOnly, name.ShowsHint, name.Text));
            Assert.Equal(("11", false), (name.FontSizeText, name.Bold)); // the bar's name as it is drawn
            Assert.Null(name.ToLook());

            var window = new TextIconWindow { DataContext = parts.Value, Parts = parts };
            try
            {
                window.Show();
                window.UpdateLayout();
                Assert.True(window.PartChoice.IsVisible);
                Assert.False(window.Sides.IsVisible);
                Assert.True(window.ValuePart.IsChecked);
                Assert.True(window.WidgetPreview.IsVisible && !window.NamePreview.IsVisible);

                parts.Value.Color = "#00FF00";
                window.NamePart.IsChecked = true; // the name part: the window's data is it now
                window.UpdateLayout();
                Assert.Same(parts.Name, window.DataContext);
                Assert.True(window.NamePreview.IsVisible && !window.WidgetPreview.IsVisible);
                Assert.Equal(string.Empty, window.ColourBox.Text); // the name's own colour, not the value's
                name.FontSizeText = "14";
                name.Bold = true;
                name.OffsetY = 10;
                Assert.Equal(14, window.NamePreviewText.TextElementForTests.FontSize);

                window.ValuePart.IsChecked = true; // back: what was changed in either part is kept
                window.UpdateLayout();
                Assert.Same(parts.Value, window.DataContext);
                Assert.Equal("#00FF00", window.ColourBox.Text);
                parts.Value.ResetOffset(); // a reset in one part leaves the other
                Assert.Equal((0, 10), (parts.Value.OffsetY, name.OffsetY));

                name.Color = "nope"; // a part not shown that cannot be saved holds OK back
                Assert.False(parts.CanSave);
                Assert.False(window.OkButton.IsEnabled);
                name.Color = "#FFEE00";
                Assert.True(window.OkButton.IsEnabled);

                var answer = parts.ToAnswer();
                Assert.Equal(new TextLook { Color = "#00FF00" }, answer.Look);
                Assert.Equal(new TextLook { Size = 14, Bold = true, Color = "#FFEE00", OffsetY = 10 }, answer.NameLook);
            }
            finally
            {
                window.Close();
            }

            // A text icon, a caption and a widget's window alone have no parts.
            var caption = new TextIconWindow { DataContext = TextIconViewModel.ForCaption("工作", null, NameDefaults) };
            try
            {
                caption.Show();
                caption.UpdateLayout();
                Assert.False(caption.PartChoice.IsVisible);
                Assert.True(caption.Sides.IsVisible);
            }
            finally
            {
                caption.Close();
            }
        });
    }

    [Fact]
    public void Both_looks_are_saved_from_the_menu_drawn_at_once_and_a_name_look_taken_away_draws_the_name_as_before()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        wpf.Invoke(() =>
        {
            var clock = new LookTile(SpecialIconKinds.Clock, GaugeStyle.Number, "14:05");
            var model = new LauncherModel();
            model.Add(clock.CreateItem());
            using var bar = new TestBar(_directory, model, handlers: [clock]);
            var item = bar.ViewModel.RootItems.Items[0];
            var gauge = item.Gauge!;
            Assert.Null(item.NameLook);
            Assert.False(item.HasLookedName);
            Assert.Same(gauge.Look, gauge.CaptionLook);

            var value = new TextLook { Color = "#00FF00" };
            bar.Dialogs.LookAnswer = new LookAnswer(value, Name);
            bar.ViewModel.EditTextLook(item);

            var saved = (SpecialIconItem)model.Items[0];
            Assert.Equal((value, Name), (saved.TextLook, saved.NameLook));
            Assert.Equal(Name, item.NameLook);
            Assert.True(item.HasLookedName);
            Assert.Same(gauge, item.Gauge); // the same gauge, drawn at once
            Assert.Same(gauge.NameLook, gauge.CaptionLook);
            Assert.Equal(new TextLook { Font = "Georgia", Bold = true, Color = "#FFEE00" }, gauge.NameLook!.Look); // not its size, plate or place
            var offered = bar.Dialogs.OfferedNameLook!.Value;
            Assert.Equal((null, bar.ViewModel.NameFontSize, FontWeights.Normal, true), (offered.NameLook, offered.NameDefaults.Size, offered.NameDefaults.Weight, offered.IsClock));

            bar.Dialogs.LookAnswer = new LookAnswer(value, null); // the name as before, the value's look kept
            bar.ViewModel.EditTextLook(item);
            Assert.Equal((value, (TextLook?)null), (((SpecialIconItem)model.Items[0]).TextLook, ((SpecialIconItem)model.Items[0]).NameLook));
            Assert.False(item.HasLookedName);
            Assert.Null(gauge.NameLook);
            Assert.Same(gauge.Look, gauge.CaptionLook);
            Assert.Equal(value, bar.Dialogs.OfferedLook!.Value.Look);
            Assert.Equal(Name, bar.Dialogs.OfferedNameLook!.Value.NameLook); // the window opened with it
        });
    }

    [Fact]
    public void The_name_line_takes_each_field_of_its_look_and_without_one_is_the_plain_line_it_was()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var tile = new LookTile("cpu-like", GaugeStyle.Number, "37%");
        var model = new LauncherModel();
        model.Settings.ShowLabels = true;
        model.Add(tile.CreateItem());
        TestBar? bar = null;
        Window? window = null;
        try
        {
            wpf.Invoke(() =>
            {
                bar = new TestBar(_directory, model, handlers: [tile]);
                window = new Window { Width = 240, Height = 160, Content = new IconGridControl { DataContext = bar.ViewModel.RootItems } };
                window.Show();
            });
            wpf.Settle();
            wpf.Invoke(() =>
            {
                var item = bar!.ViewModel.RootItems.Items[0];
                TextBlock Plain() => Descendants(window!).OfType<TextBlock>().Single(block => block.Text == "cpu-like" && block.Parent is Grid);
                LookedText? Looked() => Descendants(window!).OfType<LookedText>().FirstOrDefault(element => element.Name == "LookedNameText");

                Assert.True(Plain().IsVisible);
                Assert.Null(Looked()); // nothing of the looked name is made without a look

                bar.Dialogs.LookAnswer = new LookAnswer(null, Name);
                bar.ViewModel.EditTextLook(item);
                window!.UpdateLayout();

                Assert.False(Plain().IsVisible);
                var looked = Looked()!;
                Assert.True(looked.IsVisible);
                var text = looked.TextElementForTests;
                Assert.Equal("cpu-like", text.Text);
                Assert.StartsWith("Georgia, ", text.FontFamily.Source, StringComparison.Ordinal); // the bar's font after it
                Assert.Equal((14.0, FontWeights.Bold), (text.FontSize, text.FontWeight));
                Assert.Equal(Hex("#FFEE00"), ColourOf(text.Foreground));
                Assert.Equal(Hex("#2D4A6E"), ColourOf(looked.PlateForTests.Background));
                var shift = Assert.IsType<TranslateTransform>(looked.RenderTransform);
                Assert.Equal(10 * bar.ViewModel.NameFontSize * LookedText.LineFactor / 100, shift.Y, 6); // 10 % of the name's line
                Assert.Equal(ItemViewModel.NameLookWidth(bar.ViewModel.CellWidth, bar.ViewModel.NameFontSize, Name), looked.MaxWidth, 6);
                Assert.Equal((bar.ViewModel.CellWidth - 4) * 14 / bar.ViewModel.NameFontSize, looked.MaxWidth, 6);
                Assert.Equal("LookedName", Ancestor<Viewbox>(looked).Name); // made smaller, never wider, where it does not fit

                // What the look leaves out is the bar's name's: its size and weight, the theme's colour.
                bar.Dialogs.LookAnswer = new LookAnswer(null, new TextLook { Color = "#FF0000" });
                bar.ViewModel.EditTextLook(item);
                window.UpdateLayout();
                text = Looked()!.TextElementForTests;
                Assert.Equal((bar.ViewModel.NameFontSize, FontWeights.Normal), (text.FontSize, text.FontWeight));
                Assert.Equal(ItemViewModel.NameLookWidth(bar.ViewModel.CellWidth, bar.ViewModel.NameFontSize, null), Looked()!.MaxWidth, 6);

                bar.Dialogs.LookAnswer = new LookAnswer(null, null);
                bar.ViewModel.EditTextLook(item);
                window.UpdateLayout();
                Assert.True(Plain().IsVisible);
                Assert.True(Looked() is null || !Looked()!.IsVisible);
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
    public void The_caption_inside_the_tile_takes_the_names_font_colour_and_bold_and_without_a_name_look_the_values()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var tile = new LookTile("bar-like", GaugeStyle.Bar, "37%");
        var model = new LauncherModel();
        model.Add(tile.CreateItem());
        TestBar? bar = null;
        Window? window = null;
        try
        {
            wpf.Invoke(() =>
            {
                bar = new TestBar(_directory, model, handlers: [tile]);
                window = new Window { Width = 240, Height = 160, Content = new IconGridControl { DataContext = bar.ViewModel.RootItems } };
                window.Show();
            });
            wpf.Settle();
            wpf.Invoke(() =>
            {
                var item = bar!.ViewModel.RootItems.Items[0];
                TextBlock Caption() => Descendants(window!).OfType<TextBlock>().Single(block => block.Name == "BarCaption");
                var plainFamily = Caption().FontFamily;
                var plainColour = ColourOf(Caption().Foreground);
                Assert.Equal((24.0, FontWeights.SemiBold), (Caption().FontSize, Caption().FontWeight));

                // The value's font and colour reach the caption without a name look, as in 0.15.0.
                bar.Dialogs.LookAnswer = new LookAnswer(new TextLook { Font = "Arial", Color = "#FF0000", Bold = false }, null);
                bar.ViewModel.EditTextLook(item);
                window!.UpdateLayout();
                Assert.StartsWith("Arial", Caption().FontFamily.Source, StringComparison.Ordinal);
                Assert.Equal(Hex("#FF0000"), ColourOf(Caption().Foreground));
                Assert.Equal(FontWeights.SemiBold, Caption().FontWeight); // the value's weight does not

                // With a name look its font, colour and bold, in place of the value's; its size, plate and place stay the tile's.
                bar.Dialogs.LookAnswer = new LookAnswer(new TextLook { Font = "Arial", Color = "#FF0000", Bold = false }, new TextLook { Font = "Georgia", Color = "#00AA00", Bold = true, Size = 30, TileColor = "#123456", OffsetY = 15 });
                bar.ViewModel.EditTextLook(item);
                window.UpdateLayout();
                Assert.StartsWith("Georgia", Caption().FontFamily.Source, StringComparison.Ordinal);
                Assert.Equal(Hex("#00AA00"), ColourOf(Caption().Foreground));
                Assert.Equal((24.0, FontWeights.Bold), (Caption().FontSize, Caption().FontWeight));
                Assert.True(Caption().RenderTransform is null || Caption().RenderTransform.Value.IsIdentity);

                // Field by field (review 43 V1): what the name look leaves out is as it was without one, the value's font and
                // colour, the tile's weight.
                void Answer(TextLook value, TextLook nameLook)
                {
                    bar.Dialogs.LookAnswer = new LookAnswer(value, nameLook);
                    bar.ViewModel.EditTextLook(item);
                    window.UpdateLayout();
                }

                var red = new TextLook { Font = "Arial", Color = "#FF0000" };
                Answer(red, new TextLook { Size = 20, TileColor = "#123456", OffsetY = 10 }); // a size, a plate, a place: as without
                Assert.StartsWith("Arial, ", Caption().FontFamily.Source, StringComparison.Ordinal);
                Assert.Equal(Hex("#FF0000"), ColourOf(Caption().Foreground));
                Assert.Equal((24.0, FontWeights.SemiBold), (Caption().FontSize, Caption().FontWeight));
                Assert.True(Caption().RenderTransform is null || Caption().RenderTransform.Value.IsIdentity);

                Answer(red, new TextLook { Color = "#00AA00" }); // its colour, the value's font
                Assert.StartsWith("Arial, ", Caption().FontFamily.Source, StringComparison.Ordinal);
                Assert.Equal(Hex("#00AA00"), ColourOf(Caption().Foreground));
                Assert.Equal(FontWeights.SemiBold, Caption().FontWeight);

                Answer(red, new TextLook { Font = "Georgia" }); // its font, the value's colour
                Assert.StartsWith("Georgia, ", Caption().FontFamily.Source, StringComparison.Ordinal);
                Assert.Equal(Hex("#FF0000"), ColourOf(Caption().Foreground));

                Answer(new TextLook { Bold = true }, new TextLook { Color = "#00AA00" }); // the value's bold never reaches it
                Assert.Equal(FontWeights.SemiBold, Caption().FontWeight);
                Assert.Equal(plainFamily, Caption().FontFamily);

                Answer(new TextLook { Size = 30 }, new TextLook { Bold = true }); // its bold, the tile's font and colour
                Assert.Equal(FontWeights.Bold, Caption().FontWeight);
                Assert.Equal(plainFamily, Caption().FontFamily);
                Assert.Equal(plainColour, ColourOf(Caption().Foreground));

                // On the value look's plate, without a colour from either: the plate's text colour and no edge, as before.
                Answer(new TextLook { TileColor = "#2D4A6E" }, new TextLook { Font = "Georgia" });
                Assert.StartsWith("Georgia, ", Caption().FontFamily.Source, StringComparison.Ordinal);
                Assert.Equal(Hex(ThemeCatalog.TextOnTile("#2D4A6E")), ColourOf(Caption().Foreground));
                Assert.Null(Caption().Effect);
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
    public void The_caption_inside_takes_each_of_font_colour_and_bold_from_the_name_look_where_it_sets_it_and_else_as_before()
    {
        // Review 43 V1, the rule alone.
        var value = new TextLook { Font = "Arial", Size = 30, Bold = true, Color = "#FF0000", TileColor = "#2D4A6E", OffsetY = 5 };
        Assert.Equal(new TextLook { Font = "Arial", Color = "#FF0000", TileColor = "#2D4A6E" }, ItemViewModel.CaptionLookOf(new TextLook { Size = 20, TileColor = "#123456", OffsetY = -5 }, value));
        Assert.Equal(new TextLook { Font = "Georgia", Color = "#FF0000", TileColor = "#2D4A6E", Bold = false }, ItemViewModel.CaptionLookOf(new TextLook { Font = "Georgia", Bold = false }, value));
        Assert.Equal(new TextLook { Font = "Arial", Color = "#00AA00", TileColor = "#2D4A6E" }, ItemViewModel.CaptionLookOf(new TextLook { Color = "#00AA00" }, value));
        Assert.Equal(new TextLook { Bold = true }, ItemViewModel.CaptionLookOf(new TextLook { Bold = true }, null));
        Assert.Null(ItemViewModel.CaptionLookOf(new TextLook { Size = 20 }, null).Normalized()); // nothing at all: as without
    }

    [Fact]
    public void A_widgets_own_window_carries_both_looks_to_OK()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        wpf.Invoke(() =>
        {
            var item = new SpecialIconItem { Kind = SpecialIconKinds.Clock, Name = "Clock", TextLook = new TextLook { Color = "#00FF00" }, NameLook = Name };
            var viewModel = new ClockTileViewModel(item);
            Assert.Equal(Name, viewModel.NameLook);

            viewModel.NameLook = new TextLook { Color = "#0000FF" }; // as 文字外觀… here left it
            var saved = (SpecialIconItem)item.Clone();
            viewModel.Apply(saved);
            Assert.Equal((new TextLook { Color = "#00FF00" }, new TextLook { Color = "#0000FF" }), (saved.TextLook, saved.NameLook));
        });
    }
}
