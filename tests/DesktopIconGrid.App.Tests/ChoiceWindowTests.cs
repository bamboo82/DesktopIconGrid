using System.Windows;
using System.Windows.Controls;
using DesktopIconGrid.Views;

namespace DesktopIconGrid.App.Tests;

/// <summary>0.13.0 WP1: the question whose buttons say what they do.</summary>
[Collection(WpfWindowsCollection.Name)]
public sealed class ChoiceWindowTests(WpfThread wpf)
{
    [Fact]
    public void The_buttons_are_named_the_default_has_the_focus_and_closing_answers_with_the_cancel_one()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        wpf.Invoke(() =>
        {
            var window = new ChoiceWindow("Data location", "Copy the bar's data?", ["Copy", "Don't copy", "Cancel"], defaultButton: 1, cancelButton: 2)
            {
                ShowActivated = false,
                ShowInTaskbar = false,
            };
            try
            {
                window.Show();
                window.UpdateLayout();
                var buttons = window.Buttons.Children.OfType<Button>().ToList();

                Assert.Equal(["Copy", "Don't copy", "Cancel"], buttons.Select(button => ((TextBlock)button.Content).Text));
                Assert.Equal([false, true, false], buttons.Select(button => button.IsDefault));
                Assert.Equal([false, false, true], buttons.Select(button => button.IsCancel));
                Assert.Equal(2, window.Choice); // not answered yet: as if closed
                Assert.Equal("Copy the bar's data?", window.MessageText.Text);
            }
            finally
            {
                window.Close();
            }

            var noCancel = new ChoiceWindow("Data location", "Use this PC's bar?", ["Import", "Start empty"], defaultButton: 1, cancelButton: null);
            Assert.Equal(1, noCancel.Choice); // closed: the default, Start empty
            Assert.Throws<ArgumentOutOfRangeException>(() => new ChoiceWindow("t", "m", ["only"], defaultButton: 1, cancelButton: null));
        });
    }
}
