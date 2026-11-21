using System.Windows;
using System.Windows.Controls;
using DesktopIconGrid.Views;

namespace DesktopIconGrid.App.Tests;

/// <summary>Review 41 U3: a tooltip of the bar does not stay in front of a dialog that took the focus.</summary>
[Collection(WpfWindowsCollection.Name)]
public sealed class OpenToolTipsTests(WpfThread wpf)
{
    [Fact]
    public void A_window_closes_its_own_open_tooltips_and_leaves_another_windows_open()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        wpf.Invoke(() =>
        {
            OpenToolTips.Register();
            var barButton = new Button { Content = "Tool" };
            var otherButton = new Button { Content = "Other" };
            var bar = new Window { Width = 200, Height = 100, Content = barButton };
            var other = new Window { Width = 200, Height = 100, Content = otherButton, Left = 300 };
            try
            {
                bar.Show();
                other.Show();
                var barTip = new ToolTip { Content = "Tool", PlacementTarget = barButton };
                var otherTip = new ToolTip { Content = "Other", PlacementTarget = otherButton };
                barTip.IsOpen = true;
                otherTip.IsOpen = true;
                wpf.Settle();

                Assert.Equal(1, OpenToolTips.CloseFor(bar));
                Assert.False(barTip.IsOpen);
                Assert.True(otherTip.IsOpen);

                otherTip.IsOpen = false;
                Assert.Equal(0, OpenToolTips.CloseFor(other)); // a closed one is no longer followed
            }
            finally
            {
                other.Close();
                bar.Close();
            }
        });
    }
}
