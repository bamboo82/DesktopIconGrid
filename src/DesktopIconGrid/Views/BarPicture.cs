using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Services;

namespace DesktopIconGrid.Views;

/// <summary>
/// The custom theme's background picture on the bar or the window at the pointer (0.15.0 WP5): over the theme's colour
/// and under everything else, covering <c>cover</c> by <see cref="BackgroundFit"/>, at its strength times the background
/// transparency (they fade together). One bitmap is kept, scaled for the current size; when the size changes it is made
/// again, not more often than every 100 ms while an edge is dragged.
/// </summary>
internal sealed class BarPicture
{
    private static readonly TimeSpan Throttle = TimeSpan.FromMilliseconds(100);

    private readonly FrameworkElement _cover;
    private readonly Rectangle _layer;
    private readonly DispatcherTimer _timer;
    private BitmapSource? _picture;
    private BackgroundImage? _image;
    private double _opacity = 1;
    private (int Width, int Height, BackgroundImage? Image) _drawn;

    public BarPicture(FrameworkElement cover, Rectangle layer)
    {
        _cover = cover;
        _layer = layer;
        _timer = new DispatcherTimer(Throttle, DispatcherPriority.Background, (_, _) =>
        {
            _timer!.Stop();
            Draw();
        }, cover.Dispatcher);
        _timer.Stop();
        cover.SizeChanged += (_, _) =>
        {
            if (IsShown && !_timer.IsEnabled)
            {
                _timer.Start();
            }
        };
    }

    /// <summary>Whether a picture is drawn: one is chosen, this PC has it and Windows is not in High Contrast.</summary>
    public bool IsShown => _picture is not null && _image is not null;

    /// <summary>The bitmap drawn now, for tests.</summary>
    internal BitmapSource? Drawn => (_layer.Fill as ImageBrush)?.ImageSource as BitmapSource;

    /// <summary>Shows <paramref name="picture"/> as <paramref name="image"/> sets it, or nothing when either is none.</summary>
    public void Show(BitmapSource? picture, BackgroundImage? image, double opacity)
    {
        _picture = picture;
        _image = picture is null ? null : image;
        _opacity = Math.Clamp(opacity, 0, 1);
        if (!IsShown)
        {
            _timer.Stop();
            _layer.Fill = null;
            _layer.Visibility = Visibility.Collapsed;
            _drawn = default;
            return;
        }

        Draw();
    }

    private void Draw()
    {
        if (_picture is not { } picture || _image is not { } image)
        {
            return;
        }

        var dpi = VisualTreeHelper.GetDpi(_cover);
        var width = (int)Math.Ceiling(_cover.ActualWidth * dpi.DpiScaleX);
        var height = (int)Math.Ceiling(_cover.ActualHeight * dpi.DpiScaleY);
        if (width <= 0 || height <= 0)
        {
            return; // not laid out yet: its first size change draws it
        }

        if (_drawn != (width, height, image))
        {
            var brush = new ImageBrush(BackgroundPictures.Scaled(picture, width, height, image)) { Stretch = Stretch.Fill };
            brush.Freeze();
            _layer.Fill = brush;
            _drawn = (width, height, image);
        }

        _layer.Opacity = image.Strength / 100.0 * _opacity;
        _layer.Visibility = Visibility.Visible;
    }
}
