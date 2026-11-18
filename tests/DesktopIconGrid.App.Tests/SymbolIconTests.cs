using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using DesktopIconGrid.Core;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Core.SpecialIcons;
using DesktopIconGrid.Resources;
using DesktopIconGrid.Services;
using DesktopIconGrid.SpecialIcons;
using DesktopIconGrid.ViewModels;
using DesktopIconGrid.Views;

namespace DesktopIconGrid.App.Tests;

/// <summary>0.12.0 WP3 (G4): symbols on this PC's fonts, drawn by the one tile control everywhere, and the picker.</summary>
[Collection(WpfWindowsCollection.Name)]
public sealed class SymbolIconTests(WpfThread wpf) : IDisposable
{
    private static readonly SymbolEntry Mail = SymbolCatalog.All.Single(symbol => symbol.Name == "Mail");

    private readonly string _directory = Directory.CreateTempSubdirectory("dig-symbols-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void Every_symbol_of_the_catalogue_is_in_Segoe_MDL2_Assets()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        Assert.NotNull(TextIconFonts.Find("Segoe MDL2 Assets"));
        var missing = SymbolCatalog.All
            .Where(symbol => !SymbolGlyphs.Has("Segoe MDL2 Assets", symbol.CodePoint))
            .Select(symbol => $"{symbol.CodePoint:X4} {symbol.Name}");
        Assert.Empty(missing);
        Assert.All(SymbolCatalog.All, symbol => Assert.True(SymbolGlyphs.Has(symbol.CodePoint)));
    }

    [Fact]
    public void The_two_family_chain_draws_a_symbol_in_the_first_symbol_font_this_PC_has()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        wpf.Invoke(() =>
        {
            // CI's Windows Server has Segoe MDL2 Assets and may lack Segoe Fluent Icons, the first of the chain.
            var installed = SymbolGlyphs.Families.First(family => TextIconFonts.Find(family) is not null);
            var chain = Outline(TextIconFonts.Resolve(SymbolCatalog.FontFamily), Mail.Text);

            Assert.False(chain.IsEmpty);
            Assert.Equal(Outline(new FontFamily(installed), Mail.Text), chain);
        });
    }

    [Fact]
    public void A_fitted_symbol_grows_with_the_tile_and_text_drawn_at_its_size_does_not()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        wpf.Invoke(() =>
        {
            var symbol = SymbolCatalog.Icon(Mail);
            var small = Drawn(symbol, 32);
            var large = Drawn(symbol, 64);
            Assert.True(large.Width > small.Width * 1.8, $"{large.Width} at 64 px, {small.Width} at 32 px");
            Assert.True(large.Width <= 64 * (1 - (2 * TextIconTile.FitMargin)) + 0.5, $"{large.Width} wide inside its margins");

            // The Send to PC emoji of 0.11.0 stayed 22 px on a 64 px tile; the built-in glyph fits now.
            Assert.True(Drawn(BuiltInTextIcons.SendToDevice, 64).Width > Drawn(BuiltInTextIcons.SendToDevice, 32).Width * 1.8);

            var plain = new TextIcon("記", "Segoe UI", 20);
            Assert.Equal(Drawn(plain, 32).Width, Drawn(plain, 64).Width, 0.5);
        });
    }

    [Fact]
    public void A_tile_colour_takes_the_place_of_the_hover_fill_and_border_and_the_text_turns_white_or_black()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        wpf.Invoke(() =>
        {
            var tile = new TextIconTile { Icon = SymbolCatalog.Icon(Mail, background: "#2D4A6E"), TileSize = 64 };
            Assert.Equal(ColorOf("#2D4A6E"), ((SolidColorBrush)tile.TileBorder.Background).Color);
            Assert.Equal(new Thickness(0), tile.TileBorder.BorderThickness);
            Assert.Null(tile.TileBorder.BorderBrush);
            Assert.Equal(new CornerRadius(12), tile.TileBorder.CornerRadius); // 6 at 32 px
            Assert.Equal(Colors.White, ((SolidColorBrush)tile.TextElement.Foreground).Color);

            tile.Icon = SymbolCatalog.Icon(Mail, background: "#F3F3F3");
            Assert.Equal(ColorOf("#202020"), ((SolidColorBrush)tile.TextElement.Foreground).Color);

            tile.Icon = SymbolCatalog.Icon(Mail, color: "#FF0000", background: "#F3F3F3"); // a colour of its own wins
            Assert.Equal(ColorOf("#FF0000"), ((SolidColorBrush)tile.TextElement.Foreground).Color);

            // No tile colour (the review session's design, A2): a symbol has no frame at all, in the theme's text colour...
            tile.Icon = SymbolCatalog.Icon(Mail);
            Assert.Null(tile.TileBorder.Background);
            Assert.Null(tile.TileBorder.BorderBrush);
            Assert.Equal(new Thickness(0), tile.TileBorder.BorderThickness);
            Assert.Same(Application.Current.FindResource("Theme.Foreground"), tile.TextElement.Foreground);

            // ...and text drawn at its size keeps the frame text icons always had.
            tile.Icon = new TextIcon("記", "Segoe UI", 20);
            Assert.Equal(new Thickness(1), tile.TileBorder.BorderThickness);
            Assert.Equal(new CornerRadius(TextIconTile.CornerAt32), tile.TileBorder.CornerRadius);
            Assert.Same(Application.Current.FindResource("Theme.Hover"), tile.TileBorder.Background);
            Assert.Same(Application.Current.FindResource("Theme.HoverBorder"), tile.TileBorder.BorderBrush);
        });
    }

    [Fact]
    public void On_the_ten_swatches_the_custom_themes_rule_puts_the_text_on_the_side_of_the_designs_rule()
    {
        // A2 asks for black when (0.2126 R + 0.7152 G + 0.0722 B) / 255 > 0.6, else white, or for the theme's own rule
        // where it agrees on the ten swatches. ThemeCatalog.ForegroundOn (0.299, 0.587, 0.114; near-black #202020) does.
        Assert.Equal(10, SettingsViewModel.ColorPresets.Count);
        foreach (var swatch in SettingsViewModel.ColorPresets)
        {
            var color = ColorOf(swatch);
            var light = ((0.2126 * color.R) + (0.7152 * color.G) + (0.0722 * color.B)) / 255 > 0.6;
            Assert.Equal(light ? "#202020" : "#FFFFFF", ThemeCatalog.ForegroundOn(swatch));
        }
    }

    [Fact]
    public void A_symbol_the_fonts_of_this_PC_lack_is_drawn_as_the_placeholder_icon()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        wpf.Invoke(() =>
        {
            Assert.False(new TextIconTile { Icon = SymbolCatalog.Icon(Mail) }.ShowsPlaceholder);

            var real = TextIconTile.HasGlyph;
            TextIconTile.HasGlyph = _ => false; // an older Windows 10 without the code point
            try
            {
                Assert.True(new TextIconTile { Icon = SymbolCatalog.Icon(Mail) }.ShowsPlaceholder);
                Assert.False(new TextIconTile { Icon = new TextIcon(Mail.Text) }.ShowsPlaceholder); // text, not a symbol: drawn as it is
            }
            finally
            {
                TextIconTile.HasGlyph = real;
            }
        });
    }

    [Fact]
    public void The_bar_and_the_Properties_preview_draw_a_symbol_with_the_same_control()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var symbol = SymbolCatalog.Icon(Mail, background: "#2D4A6E");
        var model = new LauncherModel();
        model.Add(new ShortcutItem { Name = "Mail", Location = @"C:\mail.exe", TextIcon = symbol });
        TestBar? bar = null;
        Window? window = null;
        try
        {
            wpf.Invoke(() =>
            {
                bar = new TestBar(_directory, model);
                window = new Window { Width = 400, Height = 200, Content = new IconGridControl { DataContext = bar.ViewModel.RootItems } };
                window.Show();
            });
            wpf.Settle();
            wpf.Invoke(() =>
            {
                window!.UpdateLayout();
                var onBar = Descendants(window).OfType<TextIconTile>().Single();
                Assert.Equal(symbol, onBar.Icon);
                Assert.Equal(bar!.ViewModel.IconSize, onBar.TileSize);
                Assert.NotNull(onBar.FitBox);

                var properties = new ItemPropertiesWindow { DataContext = new ItemPropertiesViewModel(model.Items[0], isNew: false, new NoIcons()) };
                try
                {
                    properties.Show();
                    Settle(properties);
                    Assert.Equal(symbol, properties.TextIconPreview.Icon);
                    Assert.True(properties.TextIconPreview.IsVisible);
                    Assert.NotNull(properties.TextIconPreview.FitBox);
                }
                finally
                {
                    properties.Close();
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
    public void Searching_mail_finds_the_mail_symbol_and_hides_the_groups_without_a_match()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        wpf.Invoke(() =>
        {
            var viewModel = new TextIconViewModel(null, "Mail") { IsSymbol = true };
            Assert.Equal(Enum.GetValues<SymbolGroup>().Length, viewModel.SymbolsView.Groups.Count);

            viewModel.SearchText = "mail";
            var groups = viewModel.SymbolsView.Groups.Cast<CollectionViewGroup>().ToList();
            Assert.Equal([Strings.Symbol_GroupCommunication], groups.Select(group => (string)group.Name));
            Assert.Equal(["Mail", "Mail open"], groups.SelectMany(group => group.Items).Cast<SymbolChoice>().Select(choice => choice.Name));
            Assert.False(viewModel.HasNoMatch);

            viewModel.SearchText = "zzz";
            Assert.True(viewModel.HasNoMatch);
            Assert.Empty(viewModel.SymbolsView.Groups);
        });
    }

    [Fact]
    public void Switching_sides_keeps_both_and_OK_saves_the_side_shown()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        wpf.Invoke(() =>
        {
            var viewModel = new TextIconViewModel(new TextIcon("記", "Segoe UI", 20, true, "#112233"), "Notes");
            Assert.True(viewModel.IsText);
            viewModel.TileColor = "#2d4a6e";

            viewModel.IsSymbol = true;
            Assert.False(viewModel.CanSave); // no symbol chosen yet
            Assert.Null(viewModel.PreviewIcon);
            viewModel.SelectedSymbol = viewModel.Symbols.Single(choice => choice.Name == "Mail");
            Assert.Equal(SymbolCatalog.Icon(Mail, "#112233", "#2D4A6E"), viewModel.ToTextIcon());
            Assert.Equal(viewModel.ToTextIcon(), viewModel.PreviewIcon);

            viewModel.IsText = true;
            Assert.Equal(new TextIcon("記", "Segoe UI", 20, true, "#112233", Fit: false, Background: "#2D4A6E"), viewModel.ToTextIcon());

            viewModel.IsSymbol = true;
            Assert.Equal("Mail", viewModel.SelectedSymbol!.Name);

            // The grid lets go of a choice the search hides; the choice stays.
            viewModel.SearchText = "zzz";
            viewModel.SelectedSymbol = null;
            Assert.Equal("Mail", viewModel.SelectedSymbol!.Name);
            Assert.True(viewModel.CanSave);

            viewModel.TileColor = "blue";
            Assert.False(viewModel.CanSave);
            viewModel.ClearTileColorCommand.Execute(null);
            Assert.Equal(string.Empty, viewModel.TileColor);
            viewModel.SelectTileColorCommand.Execute("#5B6B3A");
            Assert.Equal("#5B6B3A", viewModel.ToTextIcon()!.Background);
        });
    }

    [Fact]
    public void A_symbol_opens_on_Symbol_with_it_chosen_and_fitted_text_on_Text_keeping_its_fit()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        wpf.Invoke(() =>
        {
            var symbol = new TextIconViewModel(SymbolCatalog.Icon(Mail, background: "#2D4A6E"), "Mail");
            Assert.True(symbol.IsSymbol);
            Assert.Equal("Mail", symbol.SelectedSymbol!.Name);
            Assert.Equal("#2D4A6E", symbol.TileColor);

            var send = new TextIconViewModel(BuiltInTextIcons.SendToDevice, "Send to PC");
            Assert.Equal(("Send", BuiltInTextIcons.SendToDevice), (send.SelectedSymbol!.Name, send.ToTextIcon()));

            // Text that fits but is no symbol (the Text side has no control for fit) keeps it.
            var fitted = new TextIcon("記", "Segoe UI", 20, Fit: true);
            var text = new TextIconViewModel(fitted, "Notes");
            Assert.True(text.IsText);
            Assert.Equal(fitted, text.ToTextIcon());
        });
    }

    [Fact]
    public void A_Voice_icon_shows_the_microphone_symbol_and_one_with_the_emoji_of_0_11_0_is_drawn_with_it_and_kept()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        // A3: a Voice icon without a picture showed the placeholder.
        var voice = new VoiceHandler(() => { }, () => null);
        Assert.Equal(BuiltInTextIcons.Voice, voice.CreateItem().TextIcon);
        Assert.Equal("\uE720", BuiltInTextIcons.Voice.Text);

        var emoji = new TextIcon("\U0001F3A4", "Segoe UI Emoji", 22);
        var model = new LauncherModel();
        var withoutPicture = new SpecialIconItem { Name = "Voice", Kind = SpecialIconKinds.Voice };
        var withEmoji = new SpecialIconItem { Name = "Say it", Kind = SpecialIconKinds.Voice, TextIcon = emoji };
        model.Add(withoutPicture);
        model.Add(withEmoji);
        TestBar? bar = null;
        wpf.Invoke(() =>
        {
            bar = new TestBar(_directory, model, handlers: [voice]);
            try
            {
                Assert.Equal([BuiltInTextIcons.Voice, BuiltInTextIcons.Voice], bar.ViewModel.RootItems.Items.Select(item => item.DrawnTextIcon));
                Assert.Equal(emoji, withEmoji.TextIcon);
                Assert.Null(withoutPicture.TextIcon);
            }
            finally
            {
                bar.Dispose();
            }
        });
    }

    [Fact]
    public void The_window_opens_a_symbol_on_Symbol_scrolled_to_its_cell_twelve_to_a_row()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        wpf.Invoke(() =>
        {
            var last = SymbolCatalog.All[^1];
            var viewModel = new TextIconViewModel(SymbolCatalog.Icon(last), last.Name) { PreviewSize = 48 };
            var window = new TextIconWindow { DataContext = viewModel };
            try
            {
                window.Show();
                Settle(window);
                Assert.Equal(Visibility.Collapsed, window.TextFields.Visibility);
                Assert.Equal(Visibility.Visible, window.SymbolFields.Visibility);
                Assert.Same(viewModel.SelectedSymbol, window.SymbolList.SelectedItem);
                Assert.Equal(last.Text, window.Preview.Icon!.Text);
                Assert.Equal((48.0, 48.0), (window.Preview.TileSize, window.Preview.ActualWidth)); // the bar's icon size (A2)

                // Scrolled into view, and named for a screen reader.
                var cell = Assert.IsType<ListBoxItem>(window.SymbolList.ItemContainerGenerator.ContainerFromItem(viewModel.SelectedSymbol));
                var scroller = Descendants(window.SymbolList).OfType<ScrollViewer>().First();
                var top = cell.TranslatePoint(new Point(0, 0), scroller).Y;
                Assert.InRange(top, 0, scroller.ViewportHeight - cell.ActualHeight + 0.5);
                Assert.Equal(last.Name, AutomationProperties.GetName(cell));
                Assert.Equal(last.Name, cell.ToolTip);

                // Twelve cells of 36 to a row: the thirteenth of the first group starts the second row.
                var common = viewModel.Symbols.Where(choice => choice.Entry.Group == SymbolGroup.Common).ToList();
                var first = Container(window, common[0]).TranslatePoint(new Point(0, 0), window.SymbolList);
                var twelfth = Container(window, common[11]).TranslatePoint(new Point(0, 0), window.SymbolList);
                var thirteenth = Container(window, common[12]).TranslatePoint(new Point(0, 0), window.SymbolList);
                Assert.Equal(first.X + (11 * 36), twelfth.X, 0.5);
                Assert.Equal(first.X, thirteenth.X, 0.5);
                Assert.Equal(first.Y + 36, thirteenth.Y, 0.5);

                viewModel.SearchText = "zzz";
                Settle(window);
                Assert.Equal(Visibility.Visible, window.NoSymbol.Visibility);
                viewModel.SearchText = string.Empty;
                Settle(window);
                Assert.Equal(Visibility.Collapsed, window.NoSymbol.Visibility);
                Assert.Same(viewModel.SelectedSymbol, window.SymbolList.SelectedItem);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static ListBoxItem Container(TextIconWindow window, SymbolChoice choice) =>
        Assert.IsType<ListBoxItem>(window.SymbolList.ItemContainerGenerator.ContainerFromItem(choice));

    private static Color ColorOf(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    /// <summary>The outline of <paramref name="text"/> in <paramref name="family"/>: equal outlines are the same glyph.</summary>
    private static Rect Outline(FontFamily family, string text) =>
        new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), 40, Brushes.Black, 1.0)
            .BuildGeometry(new Point()).Bounds;

    /// <summary>Where the text of <paramref name="icon"/> is drawn on a tile of <paramref name="size"/> laid out on its own.</summary>
    private static Rect Drawn(TextIcon icon, double size)
    {
        var tile = new TextIconTile { Icon = icon, TileSize = size, Width = size, Height = size };
        tile.Measure(new Size(size, size));
        tile.Arrange(new Rect(0, 0, size, size));
        tile.UpdateLayout();
        var text = tile.TextElement;
        return text.TransformToAncestor(tile).TransformBounds(new Rect(text.RenderSize));
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

    /// <summary>Lets the window lay out and run its Loaded handlers.</summary>
    private static void Settle(Window window)
    {
        window.UpdateLayout();
        var frame = new DispatcherFrame();
        window.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () => frame.Continue = false);
        Dispatcher.PushFrame(frame);
        window.UpdateLayout();
    }
}
