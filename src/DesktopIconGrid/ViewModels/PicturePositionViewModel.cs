using System.Windows;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Infrastructure;

namespace DesktopIconGrid.ViewModels;

/// <summary>
/// 調整位置… (0.15.0 WP6): where the background picture lies on the bar. The whole picture is shown fitted into about
/// <see cref="PreviewMaxWidth"/> × <see cref="PreviewMaxHeight"/>, with a frame over what the bar shows: the fit's
/// rectangle (<see cref="BackgroundFit"/>) in the preview's units, so the frame shrinks as the zoom grows. A click puts the
/// focus there, a drag of the frame moves it, the arrows move it by 1 % (Shift: 5 %), and the wheel, + and − zoom by 10.
/// </summary>
public sealed class PicturePositionViewModel : ObservableObject
{
    public const double PreviewMaxWidth = 560;
    public const double PreviewMaxHeight = 320;
    public const int ZoomStep = 10;

    private readonly double _pictureWidth;
    private readonly double _pictureHeight;
    private readonly Size _bar;
    private double _focusX;
    private double _focusY;
    private int _zoom;

    /// <param name="pictureWidth">The picture's width in its pixels; <paramref name="pictureHeight"/> its height.</param>
    /// <param name="pictureHeight">The picture's height in its pixels.</param>
    /// <param name="bar">The bar's picture area: only its proportions count.</param>
    /// <param name="image">The picture as Settings has it now.</param>
    public PicturePositionViewModel(double pictureWidth, double pictureHeight, Size bar, BackgroundImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        _pictureWidth = Math.Max(1, pictureWidth);
        _pictureHeight = Math.Max(1, pictureHeight);
        _bar = new Size(Math.Max(1, bar.Width), Math.Max(1, bar.Height));
        _focusX = image.FocusX;
        _focusY = image.FocusY;
        _zoom = image.Zoom;
        Strength = image.Strength;
        PreviewScale = Math.Min(PreviewMaxWidth / _pictureWidth, PreviewMaxHeight / _pictureHeight);
    }

    /// <summary>Preview units per picture pixel.</summary>
    public double PreviewScale { get; }

    public double PreviewWidth => _pictureWidth * PreviewScale;

    public double PreviewHeight => _pictureHeight * PreviewScale;

    /// <summary>The bar's proportions: width over height.</summary>
    public double BarAspect => _bar.Width / _bar.Height;

    public int Strength { get; }

    public double FocusX => _focusX;

    public double FocusY => _focusY;

    public int Zoom
    {
        get => _zoom;
        set => Set(_focusX, _focusY, value);
    }

    public string ZoomText => string.Create(System.Globalization.CultureInfo.CurrentCulture, $"{_zoom} %");

    public int MinZoom => BackgroundImage.MinZoom;

    public int MaxZoom => BackgroundImage.MaxZoom;

    /// <summary>What the bar shows, in the preview's units: the fit's rectangle, scaled.</summary>
    public Rect Frame
    {
        get
        {
            var (x, y, width, height) = SourceRect;
            return new Rect(x * PreviewScale, y * PreviewScale, width * PreviewScale, height * PreviewScale);
        }
    }

    /// <summary>What the bar shows, in the picture's pixels (<see cref="BackgroundFit.SourceRect"/>).</summary>
    public (double X, double Y, double Width, double Height) SourceRect =>
        BackgroundFit.SourceRect(_pictureWidth, _pictureHeight, _bar.Width, _bar.Height, _focusX, _focusY, _zoom);

    /// <summary>The focus point, in the preview's units: the cross.</summary>
    public Point Cross => new(_focusX * PreviewWidth, _focusY * PreviewHeight);

    /// <summary>The focus at <paramref name="point"/> of the preview: the frame centres on it, unless that leaves a gap.</summary>
    public void FocusAt(Point point) => Set(point.X / PreviewWidth, point.Y / PreviewHeight, _zoom);

    /// <summary>The focus moved by fractions of the picture (0.01 is 1 %).</summary>
    public void MoveFocus(double right, double down) => Set(_focusX + right, _focusY + down, _zoom);

    /// <summary>The zoom changed by <paramref name="steps"/> of <see cref="ZoomStep"/>.</summary>
    public void ZoomBy(int steps) => Set(_focusX, _focusY, _zoom + (steps * ZoomStep));

    /// <summary>置中: the focus at the middle, at zoom 100.</summary>
    public void Centre() => Set(BackgroundImage.DefaultFocus, BackgroundImage.DefaultFocus, BackgroundImage.DefaultZoom);

    /// <summary><paramref name="image"/> with this focus and zoom.</summary>
    public BackgroundImage ApplyTo(BackgroundImage image) => image with { FocusX = _focusX, FocusY = _focusY, Zoom = _zoom };

    private void Set(double focusX, double focusY, int zoom)
    {
        focusX = Math.Clamp(focusX, 0, 1);
        focusY = Math.Clamp(focusY, 0, 1);
        zoom = Math.Clamp(zoom, BackgroundImage.MinZoom, BackgroundImage.MaxZoom);
        if (focusX == _focusX && focusY == _focusY && zoom == _zoom)
        {
            return;
        }

        (_focusX, _focusY, _zoom) = (focusX, focusY, zoom);
        foreach (var name in new[] { nameof(FocusX), nameof(FocusY), nameof(Zoom), nameof(ZoomText), nameof(Frame), nameof(SourceRect), nameof(Cross) })
        {
            OnPropertyChanged(name);
        }
    }
}
