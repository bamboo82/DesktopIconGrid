using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DesktopIconGrid.Views;

namespace DesktopIconGrid.App.Tests;

/// <summary>0.13.0 WP3: a hint, and other text in a dialog that explains, is drawn in the dialog's own text colour, not grey.</summary>
[Collection(WpfWindowsCollection.Name)]
public sealed class HintTextTests(WpfThread wpf)
{
    private static Color ColourOf(Brush brush) => Assert.IsAssignableFrom<SolidColorBrush>(brush).Color;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_hint_takes_the_dialogs_text_colour_in_the_classic_and_the_fluent_look(bool fluent)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        wpf.Invoke(() =>
        {
            var was = DialogTheme.UseFluent;
            DialogTheme.Configure(fluent);
            var label = new TextBlock { Text = "A label" };
            var hint = new TextBlock { Text = "A hint", Style = (Style)Application.Current.FindResource("DialogHint") };
            var window = new Window
            {
                Width = 320,
                Height = 200,
                ShowInTaskbar = false,
                ShowActivated = false,
                Content = new StackPanel { Children = { label, hint } },
            };
            DialogTheme.Apply(window);
            try
            {
                window.Show();
                window.UpdateLayout();

                Assert.Equal(ColourOf(label.Foreground), ColourOf(hint.Foreground));
                Assert.NotEqual(ColourOf((Brush)Application.Current.FindResource(DialogTheme.HintBrushKey)), ColourOf(hint.Foreground));
            }
            finally
            {
                window.Close();
                DialogTheme.Configure(was);
            }
        });
    }

    [Fact]
    public void The_approval_windows_line_about_scrolling_is_in_the_text_colour()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        wpf.Invoke(() =>
        {
            var window = new CommandApprovalWindow("Approve", "What this runs:", string.Join('\n', Enumerable.Repeat("a long line", 200)), "Approve");
            try
            {
                window.Show();
                window.UpdateLayout();

                Assert.Equal(ColourOf(window.Intro.Foreground), ColourOf(window.ScrollHint.Foreground));
            }
            finally
            {
                window.Close();
            }
        });
    }
}
