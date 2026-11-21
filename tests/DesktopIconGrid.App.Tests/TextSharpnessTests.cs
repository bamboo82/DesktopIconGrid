using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DesktopIconGrid.Converters;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Services;
using DesktopIconGrid.ViewModels;
using DesktopIconGrid.Views;

namespace DesktopIconGrid.App.Tests;

/// <summary>
/// 0.15.0 WP9, sharp text: whole pixels and ClearType in every window, menu and tooltip; text drawn large or under a scale
/// laid out as drawn; grey smoothing where the desktop shows through. In the WPF collection: the dialogs' text size is the
/// app's, and other tests there set it.
/// </summary>
[Collection(WpfWindowsCollection.Name)]
public sealed class TextSharpnessTests(WpfThread wpf)
{
    [Theory]
    [InlineData(11, TextFormattingMode.Display)]
    [InlineData(19.9, TextFormattingMode.Display)]
    [InlineData(20, TextFormattingMode.Ideal)]
    [InlineData(42, TextFormattingMode.Ideal)]
    public void Text_drawn_20_units_high_or_more_is_laid_out_as_drawn_and_smaller_text_on_whole_pixels(double size, TextFormattingMode expected)
    {
        Assert.Equal(expected, TextSharpness.FormattingFor(size));
    }

    [Fact]
    public void ClearType_on_an_opaque_background_grey_where_the_desktop_shows_through_and_Windows_own_choice_with_a_backdrop()
    {
        Assert.Equal(TextRenderingMode.ClearType, TextSharpness.RenderingFor(seeThrough: false, backdrop: false));
        Assert.Equal(TextRenderingMode.Grayscale, TextSharpness.RenderingFor(seeThrough: true, backdrop: false));
        Assert.Equal(TextRenderingMode.Auto, TextSharpness.RenderingFor(seeThrough: false, backdrop: true));
    }

    [Fact]
    public void Text_under_a_scale_is_laid_out_as_drawn()
    {
        Assert.Equal(TextFormattingMode.Display, TextSharpness.FormattingForScale(1));
        Assert.Equal(TextFormattingMode.Ideal, TextSharpness.FormattingForScale(1.25));
        Assert.Equal(TextFormattingMode.Ideal, TextSharpness.FormattingForScale(0.9));
    }

    [Fact]
    public void Every_window_class_calls_the_helper_itself_or_through_the_dialog_theme()
    {
        // So that a new window cannot be forgotten: each one's own code calls TextSharpness.Apply or DialogTheme.Apply,
        // and DialogTheme.Apply calls TextSharpness.Apply.
        var sharpness = typeof(TextSharpness).GetMethod(nameof(TextSharpness.Apply))!;
        var theme = typeof(DialogTheme).GetMethod(nameof(DialogTheme.Apply))!;
        var windows = typeof(TextSharpness).Assembly.GetTypes().Where(type => typeof(Window).IsAssignableFrom(type) && !type.IsAbstract).ToList();

        Assert.True(windows.Count >= 25, $"only {windows.Count} window classes found");
        Assert.Empty(windows.Where(type => !Calls(type.GetConstructors(All).Cast<MethodBase>().Concat(type.GetMethods(All)), sharpness, theme)).Select(type => type.Name));
        Assert.True(Calls([theme], sharpness), "DialogTheme.Apply does not call TextSharpness.Apply");
    }

    private const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    /// <summary>Whether one of <paramref name="methods"/> calls one of <paramref name="targets"/> (a <c>call</c> with its token).</summary>
    private static bool Calls(IEnumerable<MethodBase> methods, params MethodInfo[] targets)
    {
        var tokens = targets.Select(target => target.MetadataToken).ToHashSet();
        foreach (var method in methods)
        {
            var il = method.GetMethodBody()?.GetILAsByteArray();
            for (var i = 0; il is not null && i + 4 < il.Length; i++)
            {
                if (il[i] == 0x28 && tokens.Contains(BitConverter.ToInt32(il, i + 1)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    [Fact]
    public void A_dialog_has_whole_pixel_ClearType_text_at_100_percent_and_text_as_drawn_when_scaled()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        wpf.Invoke(() =>
        {
            try
            {
                UiFonts.Configure(null);
                var plain = new Window { Content = new TextBlock { Text = "Settings" } };
                DialogTheme.Apply(plain);
                Assert.Equal(TextFormattingMode.Display, TextOptions.GetTextFormattingMode(plain));
                Assert.Equal(TextRenderingMode.ClearType, TextOptions.GetTextRenderingMode(plain));
                Assert.True(plain.UseLayoutRounding);
                Assert.True(plain.SnapsToDevicePixels);
                Assert.Equal(TextFormattingMode.Display, TextOptions.GetTextFormattingMode((TextBlock)plain.Content)); // inherited

                UiFonts.Configure(null, null, 1.5); // the dialogs at 150 %: the content is scaled as one
                var scaled = new Window { Content = new TextBlock { Text = "Settings" } };
                DialogTheme.Apply(scaled);
                Assert.False(((FrameworkElement)scaled.Content).LayoutTransform.Value.IsIdentity);
                Assert.Equal(TextFormattingMode.Ideal, TextOptions.GetTextFormattingMode(scaled));
                Assert.Equal(TextRenderingMode.ClearType, TextOptions.GetTextRenderingMode(scaled));
                plain.Close();
                scaled.Close();
            }
            finally
            {
                UiFonts.Configure(null);
            }
        });
    }

    [Fact]
    public void A_menu_and_a_tooltip_get_whole_pixel_ClearType_text_when_they_open()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        ContextMenu? menu = null;
        ToolTip? tip = null;
        TextBlock? name = null;
        Window? host = null;
        try
        {
            wpf.Invoke(() =>
            {
                TextSharpness.RegisterMenusAndTooltips();
                TextSharpness.RegisterMenusAndTooltips(); // a second call changes nothing
                menu = new ContextMenu();
                menu.Items.Add(new MenuItem { Header = "Settings" });
                tip = new ToolTip { Content = "Close" };
                name = new TextBlock { Text = "Shortcut", ContextMenu = menu, ToolTip = tip };
                host = new Window { Width = 300, Height = 100, Content = name };
                host.Show();
                menu.PlacementTarget = name;
                menu.IsOpen = true;
            });
            wpf.Settle(); // the menu has opened
            wpf.Invoke(() =>
            {
                Assert.Equal(TextFormattingMode.Display, TextOptions.GetTextFormattingMode(menu!));
                Assert.Equal(TextRenderingMode.ClearType, TextOptions.GetTextRenderingMode(menu!));
                Assert.True(menu!.UseLayoutRounding);
                menu.IsOpen = false;
                tip!.PlacementTarget = name;
                tip.IsOpen = true;
            });
            wpf.Settle();
            wpf.Invoke(() =>
            {
                Assert.Equal(TextFormattingMode.Display, TextOptions.GetTextFormattingMode(tip!));
                Assert.Equal(TextRenderingMode.ClearType, TextOptions.GetTextRenderingMode(tip!));
            });
        }
        finally
        {
            wpf.Invoke(() =>
            {
                if (menu is not null)
                {
                    menu.IsOpen = false;
                }

                if (tip is not null)
                {
                    tip.IsOpen = false;
                }

                host?.Close();
            });
        }
    }

    [Theory]
    [InlineData(26.0, 32, TextFormattingMode.Display)] // a ring's label at 32 px: about 8 units high
    [InlineData(26.0, 96, TextFormattingMode.Ideal)] // about 25
    [InlineData(22.0, 64, TextFormattingMode.Display)] // a caption at 64 px: 14
    [InlineData(42.0, 48, TextFormattingMode.Ideal)] // the clock's time at 48 px: 20.2
    public void A_gauges_text_is_judged_by_its_size_in_the_box_scaled_to_the_icon(double fontSize, int iconSize, TextFormattingMode expected)
    {
        var converter = new ScaledTextFormattingConverter();
        Assert.Equal(expected, converter.Convert([fontSize, iconSize], typeof(TextFormattingMode), "100", CultureInfo.InvariantCulture));
    }

    [Fact]
    public void A_text_tile_is_judged_by_how_high_its_text_is_drawn()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        wpf.Invoke(() =>
        {
            TextFormattingMode Fitted(double tile)
            {
                var drawn = new TextIconTile { TileSize = tile, Icon = new TextIcon("A", Fit: true) };
                return TextOptions.GetTextFormattingMode((TextBlock)drawn.FitBox!.Child);
            }

            TextFormattingMode Sized(double fontSize) =>
                TextOptions.GetTextFormattingMode(Descendant<TextBlock>(new TextIconTile { TileSize = 64, Icon = new TextIcon("Hi", FontSize: fontSize) }));

            Assert.Equal(TextFormattingMode.Ideal, Fitted(32)); // fitted to about 22 units
            Assert.Equal(TextFormattingMode.Display, Fitted(24)); // about 16
            Assert.Equal(TextFormattingMode.Display, Sized(12));
            Assert.Equal(TextFormattingMode.Ideal, Sized(24));
        });
    }

    private static T Descendant<T>(DependencyObject root)
        where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is T found)
            {
                return found;
            }

            if (child is not null && TryDescendant<T>(child) is { } deeper)
            {
                return deeper;
            }
        }

        throw new InvalidOperationException($"no {typeof(T).Name} under {root}");
    }

    private static T? TryDescendant<T>(DependencyObject root)
        where T : DependencyObject
    {
        try
        {
            return Descendant<T>(root);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    [Fact]
    public void The_sample_lines_in_Settings_are_on_whole_pixels_only_when_nothing_scales_them()
    {
        wpf.Invoke(() =>
        {
            UiFonts.Configure(null);
            var viewModel = new SettingsViewModel(new LauncherSettings());
            Assert.Equal(TextFormattingMode.Display, viewModel.SampleFormatting);
            Assert.Equal(TextFormattingMode.Display, viewModel.DialogSampleFormatting);

            viewModel.SelectedTextScale = viewModel.TextScaleOptions.Single(option => option.Percent == 125);
            Assert.Equal(TextFormattingMode.Ideal, viewModel.SampleFormatting);
            Assert.Equal(TextFormattingMode.Ideal, viewModel.DialogSampleFormatting); // the dialogs take the bar's size while it is theirs
        });
    }
}
