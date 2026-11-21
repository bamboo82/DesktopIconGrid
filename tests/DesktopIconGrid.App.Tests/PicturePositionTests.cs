using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.ViewModels;
using DesktopIconGrid.Views;

namespace DesktopIconGrid.App.Tests;

/// <summary>0.15.0 WP6: 調整位置…, where the background picture lies on the bar and how far it is zoomed.</summary>
[Collection(WpfWindowsCollection.Name)]
public sealed class PicturePositionTests(WpfThread wpf)
{
    private static readonly BackgroundImage Image = new() { File = "0123456789abcdef0123456789abcdef.jpg", Strength = 70 };

    /// <summary>A 1600 × 900 picture behind a bar four times as wide as it is high.</summary>
    private static PicturePositionViewModel Position(BackgroundImage? image = null) => new(1600, 900, new Size(400, 100), image ?? Image);

    private static void Same(Rect expected, Rect actual)
    {
        Assert.Equal(expected.X, actual.X, 6);
        Assert.Equal(expected.Y, actual.Y, 6);
        Assert.Equal(expected.Width, actual.Width, 6);
        Assert.Equal(expected.Height, actual.Height, 6);
    }

    [Fact]
    public void The_frame_is_the_fits_rectangle_in_the_previews_units_and_shrinks_as_the_zoom_grows()
    {
        var position = Position();
        Assert.Equal(0.35, position.PreviewScale, 6); // 560 / 1600 is the tighter of the two
        Assert.Equal((560.0, 315.0), (position.PreviewWidth, position.PreviewHeight));

        var (x, y, width, height) = BackgroundFit.SourceRect(1600, 900, 400, 100, 0.5, 0.5, 100);
        Same(new Rect(x * 0.35, y * 0.35, width * 0.35, height * 0.35), position.Frame);
        Same(new Rect(0, 87.5, 560, 140), position.Frame);

        position.Zoom = 200;
        Same(new Rect(140, 122.5, 280, 70), position.Frame);
    }

    [Fact]
    public void A_click_in_a_corner_puts_the_focus_there_and_the_frame_stops_at_the_edge()
    {
        var position = Position();
        position.Zoom = 200;

        position.FocusAt(new Point(560, 315));
        Assert.Equal((1.0, 1.0), (position.FocusX, position.FocusY));
        Assert.Equal(560, position.Frame.Right, 6);
        Assert.Equal(315, position.Frame.Bottom, 6);
        Assert.Equal(new Point(560, 315), position.Cross);

        position.FocusAt(new Point(-50, 9999)); // held to the picture
        Assert.Equal((0.0, 1.0), (position.FocusX, position.FocusY));
    }

    [Fact]
    public void The_keys_move_the_focus_by_one_or_five_percent_and_zoom_by_ten_and_centre_puts_it_back()
    {
        var position = Position(Image with { FocusX = 0.5, FocusY = 0.5, Zoom = 150 });

        position.MoveFocus(0.01, 0);
        position.MoveFocus(0, -0.05);
        Assert.Equal(0.51, position.FocusX, 6);
        Assert.Equal(0.45, position.FocusY, 6);

        position.ZoomBy(1);
        Assert.Equal(160, position.Zoom);
        position.ZoomBy(-100);
        Assert.Equal(100, position.Zoom);
        position.ZoomBy(100);
        Assert.Equal(400, position.Zoom);

        position.Centre();
        Assert.Equal((0.5, 0.5, 100), (position.FocusX, position.FocusY, position.Zoom));
    }

    [Fact]
    public void Ok_gives_the_picture_its_new_focus_and_zoom_and_keeps_the_rest()
    {
        var position = Position();
        position.FocusAt(new Point(140, 78.75));
        position.Zoom = 250;

        var adjusted = position.ApplyTo(Image);

        Assert.Equal(Image with { FocusX = 0.25, FocusY = 0.25, Zoom = 250 }, adjusted);
    }

    [Theory]
    [InlineData(560, 4, 480, 120)] // a wide bar: as tall as allowed, then as wide as its proportions make it
    [InlineData(560, 10, 560, 56)]
    [InlineData(560, 0.25, 30, 120)] // an upright bar: an upright strip
    [InlineData(560, 40, 560, 28)] // very flat: never lower than a row of text
    public void The_strip_has_the_bars_proportions_within_its_limits(double preview, double aspect, double width, double height)
    {
        Assert.Equal(new Size(width, height), PicturePositionWindow.StripSize(preview, aspect));
    }

    [Fact]
    public void The_window_puts_the_frame_and_cross_where_the_focus_is_and_shows_sample_names_on_the_strip()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        wpf.Invoke(() =>
        {
            var pixels = new byte[160 * 90 * 4];
            Array.Fill(pixels, (byte)0x80);
            var picture = BitmapSource.Create(160, 90, 96, 96, PixelFormats.Bgra32, null, pixels, 160 * 4);
            picture.Freeze();
            var position = new PicturePositionViewModel(160, 90, new Size(400, 100), Image);
            var window = new PicturePositionWindow(position, picture, new PicturePositionWindow.StripLook("#73806E", "#FFFFFF", 80));
            try
            {
                position.Zoom = 200;
                position.FocusAt(new Point(0, 0));
                Assert.Equal(position.Frame.X, Canvas.GetLeft(window.FrameInner), 6);
                Assert.Equal(position.Frame.Width, window.FrameInner.Width, 6);
                Assert.NotNull(window.Dim.Data);
                Assert.Equal(4, window.StripIcons.Children.Count);
                Assert.Equal(0.7 * 0.8, window.StripPicture.Opacity, 6); // the strength times the background's opacity
                Assert.Equal(TextFormattingMode.Display, TextOptions.GetTextFormattingMode(window)); // a dialog like the others
            }
            finally
            {
                window.Close();
            }
        });
    }
}
