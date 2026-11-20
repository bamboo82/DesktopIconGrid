using System.Globalization;
using System.Windows;
using System.Windows.Media;
using DesktopIconGrid.Core;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Resources;
using DesktopIconGrid.Services;
using DesktopIconGrid.ViewModels;
using DesktopIconGrid.Views;

namespace DesktopIconGrid.App.Tests;

/// <summary>
/// 0.13.0 WP4: the font and the size of the app's text. In the WPF collection: the font settings are the app's, shared by
/// every window, and other tests there read them.
/// </summary>
[Collection(WpfWindowsCollection.Name)]
public sealed class TextSizeTests(WpfThread wpf)
{
    [Fact]
    public void The_font_list_starts_with_Windows_own_keeps_a_missing_one_and_Save_writes_the_choice()
    {
        var viewModel = new SettingsViewModel(new LauncherSettings { UiFont = "Gone Font", TextScale = 120 });
        viewModel.UseFamilies(() => [new FontFamily("Times New Roman"), new FontFamily("Arial"), new FontFamily("Arial")]);

        Assert.Equal(new UiFontOption(null, string.Format(CultureInfo.CurrentCulture, Strings.Settings_UiFontDefault, SystemFonts.MessageFontFamily.Source)), viewModel.UiFontOptions[0]);
        Assert.Equal(new UiFontOption("Gone Font", string.Format(CultureInfo.CurrentCulture, Strings.Settings_UiFontNotHere, "Gone Font")), viewModel.UiFontOptions[1]);
        Assert.Equal(["Arial", "Times New Roman"], viewModel.UiFontOptions.Skip(2).Select(option => option.Id)); // by name, once
        Assert.Equal("Gone Font", viewModel.SelectedUiFont.Id); // kept: the setting syncs, and another PC may have it
        Assert.Equal([90, 100, 110, 120, 125, 150], viewModel.TextScaleOptions.Select(option => option.Percent)); // 120 from the layout, kept
        Assert.Equal(120, viewModel.SelectedTextScale.Percent);
        Assert.Equal(1.2 / UiFonts.TextFactor, viewModel.SampleScale, 3);
        Assert.Equal(("Gone Font", (int?)120), (viewModel.ToSettings().UiFont, viewModel.ToSettings().TextScale));

        viewModel.SelectedUiFont = viewModel.UiFontOptions.Single(option => option.Id == "Arial");
        Assert.StartsWith("Arial, ", viewModel.SampleFontFamily.Source);
        viewModel.SelectedUiFont = viewModel.UiFontOptions[0];
        viewModel.SelectedTextScale = viewModel.TextScaleOptions.Single(option => option.Percent == 100);
        Assert.Equal(((string?)null, (int?)null), (viewModel.ToSettings().UiFont, viewModel.ToSettings().TextScale)); // the defaults, written as nothing
    }

    [Fact]
    public void The_chosen_font_leads_every_chain_and_the_size_goes_to_menus_tooltips_and_messages()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        wpf.Invoke(() =>
        {
            var resources = new ResourceDictionary();
            try
            {
                UiFonts.Configure(null, "Arial", 1.5, resources);

                Assert.Equal($"Arial, {SystemFonts.MessageFontFamily.Source}", ((FontFamily)resources[UiFonts.FamilyKey]).Source);
                Assert.Equal($"Arial, {SystemFonts.MenuFontFamily.Source}", ((FontFamily)resources[SystemFonts.MenuFontFamilyKey]).Source);
                Assert.Equal($"Arial, {SystemFonts.StatusFontFamily.Source}", ((FontFamily)resources[SystemFonts.StatusFontFamilyKey]).Source);
                Assert.Equal(SystemFonts.MenuFontSize * 1.5, (double)resources[SystemFonts.MenuFontSizeKey], 3);
                Assert.Equal(SystemFonts.StatusFontSize * 1.5, (double)resources[SystemFonts.StatusFontSizeKey], 3);
                Assert.Equal(SystemFonts.MessageFontSize * 1.5, (double)resources[SystemFonts.MessageFontSizeKey], 3);
                Assert.StartsWith("Consolas", ((FontFamily)resources[UiFonts.CodeFamilyKey]).Source); // code keeps Consolas first
                Assert.Equal(1.5, UiFonts.TextFactor);

                UiFonts.Configure(null, resources); // Windows' font at Windows' size: nothing left set, as before
                Assert.Equal(SystemFonts.MessageFontFamily.Source, ((FontFamily)resources[UiFonts.FamilyKey]).Source);
                Assert.False(resources.Contains(SystemFonts.MenuFontFamilyKey));
                Assert.False(resources.Contains(SystemFonts.MenuFontSizeKey));
                Assert.Equal(1, UiFonts.TextFactor);
            }
            finally
            {
                UiFonts.Configure(null, resources);
            }
        });
    }

    [Fact]
    public void A_dialog_is_scaled_whole_once_and_its_set_sizes_grow_with_it()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        wpf.Invoke(() =>
        {
            var window = new Window { Width = 400, Height = 300, MinWidth = 100, MaxHeight = 800, Content = new System.Windows.Controls.Grid() };
            DialogTheme.ScaleText(window, 1.5);
            DialogTheme.ScaleText(window, 1.5); // once: a second call finds it scaled

            Assert.Equal((600.0, 450.0, 150.0, 1200.0), (window.Width, window.Height, window.MinWidth, window.MaxHeight));
            Assert.True(double.IsPositiveInfinity(window.MaxWidth));
            Assert.Equal(1.5, ((FrameworkElement)window.Content).LayoutTransform.Value.M11);

            var unscaled = new Window { Width = 400, Content = new System.Windows.Controls.Grid() };
            DialogTheme.ScaleText(unscaled, 1);
            Assert.Equal(400, unscaled.Width);
            Assert.True(((FrameworkElement)unscaled.Content).LayoutTransform.Value.IsIdentity);
        });
    }

    [Fact]
    public void On_the_bar_the_names_and_their_line_follow_the_size_and_the_icons_keep_theirs()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var folder = System.IO.Directory.CreateTempSubdirectory("dig-text-").FullName;
        try
        {
            var model = new LauncherModel();
            model.Settings.ShowLabels = true;
            model.Settings.TextScale = 150;
            using var bar = new TestBar(folder, model);
            var settings = bar.ViewModel.Model.Settings;

            Assert.Equal(16.5, bar.ViewModel.NameFontSize, 3);
            Assert.Equal(24, bar.ViewModel.NameHeight);
            Assert.Equal(18, bar.ViewModel.EmptyHintFontSize, 3);
            Assert.Equal(settings.IconSize + (2 * settings.CellPadding) + 24 + 2, bar.ViewModel.CellHeight);
            Assert.Equal(settings.IconSize, bar.ViewModel.IconSize);

            // Review 33 R3: the width follows too, so a name that fits at 100 % fits at 150 %; at 90 % it keeps 100 %'s.
            var atHundred = settings.IconSize + (2 * settings.CellPadding) + 24;
            Assert.Equal(Math.Ceiling(atHundred * 1.5), bar.ViewModel.CellWidth);
            settings.TextScale = 90;
            Assert.Equal(atHundred, bar.ViewModel.CellWidth);
            settings.ShowLabels = false;
            settings.TextScale = 150;
            Assert.Equal(settings.IconSize + (2 * settings.CellPadding), bar.ViewModel.CellWidth); // no names: the icon's width
        }
        finally
        {
            System.IO.Directory.Delete(folder, recursive: true);
        }
    }
}
