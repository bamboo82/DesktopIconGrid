using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Services;
using DesktopIconGrid.Views;

namespace DesktopIconGrid.App.Tests;

public class AppearanceTests
{
    [Fact]
    public void High_contrast_palette_is_built_from_system_colours_in_the_theme_format()
    {
        var palette = ThemeManager.HighContrast();

        foreach (var hex in new[] { palette.Background, palette.Border, palette.Foreground, palette.Hover, palette.HoverBorder, palette.Pressed, palette.Grip, palette.Accent, palette.DropTarget })
        {
            Assert.Matches("^#[0-9A-F]{6}$|^#[0-9A-F]{8}$", hex);
            _ = (Color)ColorConverter.ConvertFromString(hex); // parseable by WPF
        }

        Assert.Equal(ThemeManager.HighContrastId, palette.Id);
        Assert.False(ThemeCatalog.IsKnown(palette.Id)); // never stored in settings
    }

    [Fact]
    public void Bar_background_is_a_tint_only_while_a_backdrop_shows_through()
    {
        var resources = new ResourceDictionary();

        ThemeManager.Apply(ThemeCatalog.Classic, resources, translucentBar: true, tintOpacity: 0.4);
        var tinted = Assert.IsType<SolidColorBrush>(resources[ThemeManager.BarBackgroundKey]);
        Assert.Equal(0.4, tinted.Opacity);
        Assert.Equal(ThemeManager.ToColor(ThemeCatalog.Classic.Background), tinted.Color);
        Assert.Equal(1.0, Assert.IsType<SolidColorBrush>(resources[ThemeManager.BackgroundKey]).Opacity);
        Assert.Equal(1.0, Assert.IsType<SolidColorBrush>(resources[ThemeManager.PopupBackgroundKey]).Opacity); // solid until its own backdrop is known

        ThemeManager.SetPopupBackground(ThemeCatalog.Classic, translucent: true, tintOpacity: 0.4, resources);
        Assert.Equal(0.4, Assert.IsType<SolidColorBrush>(resources[ThemeManager.PopupBackgroundKey]).Opacity);

        ThemeManager.Apply(ThemeCatalog.Classic, resources);
        Assert.Equal(1.0, Assert.IsType<SolidColorBrush>(resources[ThemeManager.BarBackgroundKey]).Opacity);
        Assert.True(((Freezable)resources[ThemeManager.BarBackgroundKey]).IsFrozen);
    }

    [Fact]
    public void A_backdrop_is_never_claimed_without_a_window() =>
        Assert.False(WindowBackdrop.Apply(IntPtr.Zero, LauncherSettings.BackdropMica, darkMode: true));

    [Fact]
    public void Tray_icon_size_follows_the_system_dpi()
    {
        var size = TrayIcon.SmallIconSize();

        Assert.InRange(size, 16, 64);
    }

    [Fact]
    public void Dialog_brushes_follow_the_fluent_setting_and_the_app_mode()
    {
        var resources = new ResourceDictionary();

        DialogTheme.Configure(useFluent: false, resources);
        Assert.False(DialogTheme.IsDark); // classic dialogs are always light
        Assert.Equal(Color.FromRgb(0x6E, 0x6E, 0x6E), Assert.IsType<SolidColorBrush>(resources[DialogTheme.HintBrushKey]).Color);

        DialogTheme.Configure(useFluent: true, resources);
        var hint = Assert.IsType<SolidColorBrush>(resources[DialogTheme.HintBrushKey]);
        Assert.Equal(DialogTheme.IsDark ? Color.FromRgb(0xA8, 0xA8, 0xA8) : Color.FromRgb(0x6E, 0x6E, 0x6E), hint.Color);
        Assert.True(hint.IsFrozen);
        Assert.NotNull(resources[DialogTheme.WarningBrushKey]);
        Assert.NotNull(resources[DialogTheme.SwatchBorderBrushKey]);
    }

    [Fact]
    public void Keyboard_activation_is_told_from_the_input_device()
    {
        Assert.False(IconGridControl.IsKeyboardActivation(null));

        // Input devices live on an STA thread with an InputManager.
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                Assert.True(IconGridControl.IsKeyboardActivation(Keyboard.PrimaryDevice));
                Assert.False(IconGridControl.IsKeyboardActivation(Mouse.PrimaryDevice));
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(failure);
    }
}
