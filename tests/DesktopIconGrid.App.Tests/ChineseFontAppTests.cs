using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using DesktopIconGrid.Core.Fonts;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Resources;
using DesktopIconGrid.Services;
using DesktopIconGrid.ViewModels;
using DesktopIconGrid.Views;

namespace DesktopIconGrid.App.Tests;

/// <summary>
/// 0.12.0 WP5 (T2–T4): Chinese text in Taipei Sans TC Light, here a two-glyph test font of the same shape (a
/// typographic family with a Light face of weight 300), and the Settings row that installs it.
/// </summary>
[Collection(WpfWindowsCollection.Name)]
public sealed class ChineseFontAppTests(WpfThread wpf)
{
    private static string TestFont => Path.Combine(AppContext.BaseDirectory, "Assets", "DigTestCjk-Light.ttf");

    /// <summary>Each glyph run of what <paramref name="text"/> draws: its characters and the font file they come from.</summary>
    private static List<(string Characters, GlyphTypeface Font)> Runs(TextBlock text)
    {
        var runs = new List<(string, GlyphTypeface)>();
        Collect(VisualTreeHelper.GetDrawing(text));
        return runs;

        void Collect(Drawing? drawing)
        {
            switch (drawing)
            {
                case DrawingGroup group:
                    foreach (var child in group.Children)
                    {
                        Collect(child);
                    }

                    break;
                case GlyphRunDrawing glyphs:
                    runs.Add((new string([.. glyphs.GlyphRun.Characters ?? []]), glyphs.GlyphRun.GlyphTypeface));
                    break;
            }
        }
    }

    private static GlyphTypeface FontOf(TextBlock text, char character) =>
        Assert.Single(Runs(text), run => run.Characters.Contains(character, StringComparison.Ordinal)).Font;

    private static bool IsTestFont(GlyphTypeface font) => font.FontUri.LocalPath.EndsWith("DigTestCjk-Light.ttf", StringComparison.OrdinalIgnoreCase);

    private static void Settle(Window window)
    {
        window.UpdateLayout();
        var frame = new DispatcherFrame();
        window.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () => frame.Continue = false);
        Dispatcher.PushFrame(frame);
        window.UpdateLayout();
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

    [Fact]
    public void The_family_is_named_from_the_file_and_referred_to_by_that_file()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        wpf.Invoke(() =>
        {
            var family = Assert.IsType<FontFamily>(UiFonts.FromFile(TestFont));
            Assert.StartsWith(new Uri(TestFont).AbsoluteUri + "#", family.Source, StringComparison.OrdinalIgnoreCase); // the file, not its folder
            Assert.Contains("DIG Test CJK", family.Source, StringComparison.Ordinal);
            Assert.Null(UiFonts.FromFile(Path.Combine(AppContext.BaseDirectory, "DesktopIconGrid.App.Tests.dll"))); // no font in it
        });
    }

    [Fact]
    public void Chinese_comes_from_the_font_and_Latin_from_the_windows_own_font_on_the_bar_and_in_a_Fluent_dialog()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        wpf.Invoke(() =>
        {
            UiFonts.Configure(TestFont);
            Window? bar = null;
            TextIconWindow? dialog = null;
            try
            {
                Assert.Equal(TestFont, UiFonts.ChineseFontFile);

                // The bar's windows take the font as MainWindow does.
                var label = new TextBlock { Text = "設定 Settings" };
                bar = new Window { Width = 300, Height = 100, Content = label };
                UiFonts.Apply(bar);
                bar.Show();
                Settle(bar);
                Assert.True(IsTestFont(FontOf(label, '設')));
                Assert.False(IsTestFont(FontOf(label, 'S')));
                Assert.Contains("Segoe UI", FontOf(label, 'S').FamilyNames.Values);

                // A dialog takes it through DialogTheme.Apply, Fluent or not.
                dialog = new TextIconWindow { DataContext = new TextIconViewModel(null, "Notes") };
                dialog.Show();
                Settle(dialog);
                var text = Descendants(dialog).OfType<TextBlock>().First(block => block.Text == Strings.TextIcon_Text);
                text.Text = "設定 Settings";
                Settle(dialog);
                Assert.True(IsTestFont(FontOf(text, '定')));
                Assert.False(IsTestFont(FontOf(text, 'S')));

                // Menus and tooltips are windows of their own: their font keys are set beside the window's.
                Assert.Contains(new Uri(TestFont).AbsoluteUri, ((FontFamily)Application.Current.Resources[SystemFonts.MenuFontFamilyKey]).Source, StringComparison.OrdinalIgnoreCase);
                Assert.Contains(new Uri(TestFont).AbsoluteUri, ((FontFamily)Application.Current.Resources[SystemFonts.StatusFontFamilyKey]).Source, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                bar?.Close();
                dialog?.Close();
                UiFonts.Configure(null);
            }
        });
    }

    [Fact]
    public void Without_the_font_or_with_the_Windows_choice_nothing_differs_from_before()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        wpf.Invoke(() =>
        {
            UiFonts.Configure(TestFont);
            UiFonts.Configure(null); // what "system", or no file, configures
            Assert.Null(UiFonts.ChineseFontFile);
            Assert.False(Application.Current.Resources.Contains(SystemFonts.MenuFontFamilyKey));
            Assert.False(Application.Current.Resources.Contains(SystemFonts.StatusFontFamilyKey));
            Assert.False(Application.Current.Resources.Contains(SystemFonts.MessageFontFamilyKey));
            Assert.Equal(SystemFonts.MessageFontFamily, Application.Current.Resources[UiFonts.FamilyKey]);

            var label = new TextBlock { Text = "設定" };
            var window = new Window { Width = 300, Height = 100, Content = label };
            UiFonts.Apply(window);
            try
            {
                window.Show();
                Settle(window);
                Assert.False(IsTestFont(FontOf(label, '設')));
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void Bold_Chinese_is_the_Light_face_without_a_simulated_bold()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        // What WPF does where the app asks for bold (the voice window's names) or SemiBold (headings) and only the Light
        // face is there: it draws Light, without emboldening it (CI run 185 showed it; the guess had been the opposite),
        // so nothing is smeared and the Chinese of those texts is Light while their Latin stays bold.
        wpf.Invoke(() =>
        {
            UiFonts.Configure(TestFont);
            var label = new TextBlock { Text = "設定", FontWeight = FontWeights.Bold };
            var window = new Window { Width = 300, Height = 100, Content = label };
            UiFonts.Apply(window);
            try
            {
                window.Show();
                Settle(window);
                var font = FontOf(label, '設');
                Assert.True(IsTestFont(font));
                Assert.Equal(StyleSimulations.None, font.StyleSimulations & StyleSimulations.BoldSimulation);
            }
            finally
            {
                window.Close();
                UiFonts.Configure(null);
            }
        });
    }

    /// <summary>Stands in for this PC's font and its install, answering as told.</summary>
    private sealed class FakeFonts : IChineseFontInstall
    {
        public string? Found { get; set; }

        public int Installs { get; private set; }

        public TaskCompletionSource<FontInstallResult> Result { get; private set; } = new();

        public IProgress<long>? Progress { get; private set; }

        public CancellationToken Cancel { get; private set; }

        public long Size => TaipeiSans.Light.Size;

        public string? Find() => Found;

        public Task<FontInstallResult> InstallAsync(IProgress<long> progress, CancellationToken cancel)
        {
            Installs++;
            Progress = progress;
            Cancel = cancel;
            Result = new TaskCompletionSource<FontInstallResult>();
            return Result.Task;
        }
    }

    [Fact]
    public void With_the_font_the_row_offers_it_and_the_Windows_font_and_no_button()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        wpf.Invoke(() =>
        {
            var viewModel = new SettingsViewModel(new LauncherSettings());
            viewModel.UseChineseFont(new FakeFonts { Found = TestFont });

            Assert.Equal([LauncherSettings.ChineseFontTaipei, LauncherSettings.ChineseFontSystem], viewModel.ChineseFontOptions.Select(option => option.Id));
            var expected = AppLanguage.IsSimplifiedChinese(CultureInfo.CurrentUICulture) ? LauncherSettings.ChineseFontSystem : LauncherSettings.ChineseFontTaipei;
            Assert.Equal(expected, viewModel.SelectedChineseFont.Id);
            Assert.False(viewModel.IsChineseFontMissing);
            Assert.Null(viewModel.ToSettings().ChineseFont); // not touched: the default stays the default

            viewModel.SelectedChineseFont = viewModel.ChineseFontOptions[1];
            Assert.Equal(LauncherSettings.ChineseFontSystem, viewModel.ToSettings().ChineseFont);

            var window = new SettingsWindow { DataContext = viewModel };
            try
            {
                window.Show();
                Settle(window);
                Assert.Equal(Visibility.Collapsed, window.InstallFontButton.Visibility);
                Assert.Equal(Visibility.Collapsed, window.ChineseFontHint.Visibility);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public async Task Without_it_the_button_asks_downloads_with_progress_and_installs()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var fonts = new FakeFonts();
        SettingsViewModel? viewModel = null;
        var asked = 0;
        var answer = false;
        wpf.Invoke(() =>
        {
            // A setting that came through sync on a PC without the font: shown as the Windows font, kept as it was.
            viewModel = new SettingsViewModel(new LauncherSettings { ChineseFont = LauncherSettings.ChineseFontTaipei });
            viewModel.UseChineseFont(fonts);
            viewModel.ConfirmInstall = () =>
            {
                asked++;
                return answer;
            };
            Assert.Equal([LauncherSettings.ChineseFontSystem], viewModel.ChineseFontOptions.Select(option => option.Id));
            Assert.True(viewModel.IsChineseFontMissing);
            Assert.True(viewModel.ShowsMissingHint);
            Assert.Equal(LauncherSettings.ChineseFontTaipei, viewModel.ToSettings().ChineseFont);

            var window = new SettingsWindow { DataContext = viewModel };
            try
            {
                window.Show();
                Settle(window);
                Assert.Equal(Visibility.Visible, window.InstallFontButton.Visibility);
                Assert.Equal(Visibility.Visible, window.ChineseFontHint.Visibility);
                Assert.Equal(Visibility.Collapsed, window.FontProgress.Visibility);
            }
            finally
            {
                window.Close();
            }
        });

        // Asked, and Cancel: nothing happens.
        Task? declined = null;
        wpf.Invoke(() => declined = viewModel!.InstallFontAsync());
        await declined!;
        Assert.Equal((1, 0), (asked, fonts.Installs));

        answer = true;
        Task? install = null;
        wpf.Invoke(() =>
        {
            install = viewModel!.InstallFontAsync();
            Assert.Equal(FontInstallState.Downloading, viewModel.InstallState);
            Assert.False(viewModel.ShowsMissingHint);
            Assert.False(viewModel.InstallFontCommand.CanExecute(null));
            fonts.Progress!.Report(7_400_000);
        });
        wpf.Settle();
        wpf.Invoke(() =>
        {
            Assert.Equal(string.Format(CultureInfo.CurrentCulture, Strings.FontInstall_Progress, (7.4).ToString("0.0", CultureInfo.CurrentCulture), (20.3).ToString("0.0", CultureInfo.CurrentCulture)), viewModel!.InstallProgressText);
            fonts.Result.SetResult(new FontInstallResult(FontInstallOutcome.Installed, @"C:\Users\me\AppData\Local\Microsoft\Windows\Fonts\TaipeiSansTCBeta-Light.ttf"));
        });
        await install!;

        wpf.Invoke(() =>
        {
            Assert.Equal(FontInstallState.Done, viewModel!.InstallState);
            Assert.Equal(Strings.FontInstall_Done, viewModel.InstallMessage);
            Assert.False(viewModel.IsInstallFailed);
            Assert.Equal([LauncherSettings.ChineseFontTaipei, LauncherSettings.ChineseFontSystem], viewModel.ChineseFontOptions.Select(option => option.Id));
            Assert.Equal(LauncherSettings.ChineseFontTaipei, viewModel.SelectedChineseFont.Id);
            Assert.False(viewModel.IsChineseFontMissing);
            Assert.Equal(LauncherSettings.ChineseFontTaipei, viewModel.ToSettings().ChineseFont); // applies at Save
        });
    }

    [Theory]
    [InlineData(FontInstallOutcome.DownloadFailed, "No such host is known.")]
    [InlineData(FontInstallOutcome.Mismatch, null)]
    [InlineData(FontInstallOutcome.InstallFailed, "Access is denied.")]
    [InlineData(FontInstallOutcome.OtherFileThere, null)]
    [InlineData(FontInstallOutcome.Cancelled, null)]
    public async Task Each_failure_is_said_in_the_row_with_the_way_by_hand_and_installs_nothing(FontInstallOutcome outcome, string? message)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var fonts = new FakeFonts();
        SettingsViewModel? viewModel = null;
        Task? install = null;
        wpf.Invoke(() =>
        {
            viewModel = new SettingsViewModel(new LauncherSettings());
            viewModel.UseChineseFont(fonts);
            viewModel.ConfirmInstall = () => true;
            install = viewModel.InstallFontAsync();
            fonts.Result.SetResult(new FontInstallResult(outcome, Message: message));
        });
        await install!;

        wpf.Invoke(() =>
        {
            var expected = outcome switch
            {
                FontInstallOutcome.DownloadFailed => string.Format(CultureInfo.CurrentCulture, Strings.FontInstall_DownloadFailed, message),
                FontInstallOutcome.Mismatch => Strings.FontInstall_Mismatch,
                FontInstallOutcome.InstallFailed => string.Format(CultureInfo.CurrentCulture, Strings.FontInstall_InstallFailed, message),
                FontInstallOutcome.OtherFileThere => string.Format(CultureInfo.CurrentCulture, Strings.FontInstall_OtherFileThere, TaipeiSans.FileName),
                _ => null,
            };
            Assert.Equal(expected, viewModel!.InstallMessage);
            Assert.Equal(outcome != FontInstallOutcome.Cancelled, viewModel.IsInstallFailed); // the link to the author's page
            Assert.Equal(outcome == FontInstallOutcome.Cancelled, viewModel.ShowsMissingHint);
            Assert.Equal([LauncherSettings.ChineseFontSystem], viewModel.ChineseFontOptions.Select(option => option.Id));
            Assert.Null(viewModel.ToSettings().ChineseFont);
            Assert.True(viewModel.InstallFontCommand.CanExecute(null)); // it can be tried again
            Assert.Equal("https://sites.google.com/view/jtfoundry/zh-tw/downloads", viewModel.ManualInstallPage.AbsoluteUri);

            if (outcome == FontInstallOutcome.Mismatch)
            {
                var window = new SettingsWindow { DataContext = viewModel };
                try
                {
                    window.Show();
                    Settle(window);
                    Assert.Equal(Visibility.Visible, window.FontInstallMessage.Visibility);
                    Assert.Equal(Visibility.Visible, window.ManualInstall.Visibility);
                    Assert.Same(Application.Current.FindResource("ErrorTextBrush"), window.FontInstallMessage.Foreground);
                }
                finally
                {
                    window.Close();
                }
            }
        });
    }

    [Fact]
    public async Task Closing_Settings_cancels_a_download_under_way()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var fonts = new FakeFonts();
        Task? install = null;
        wpf.Invoke(() =>
        {
            var viewModel = new SettingsViewModel(new LauncherSettings());
            viewModel.UseChineseFont(fonts);
            viewModel.ConfirmInstall = () => true;
            var window = new SettingsWindow { DataContext = viewModel };
            window.Show();
            Settle(window);
            install = viewModel.InstallFontAsync();
            window.Close();
            Assert.True(fonts.Cancel.IsCancellationRequested);
            fonts.Result.SetResult(new FontInstallResult(FontInstallOutcome.Cancelled));
        });
        await install!;
    }
}
