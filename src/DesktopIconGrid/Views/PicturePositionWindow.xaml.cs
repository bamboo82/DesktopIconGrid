using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Resources;
using DesktopIconGrid.Services;
using DesktopIconGrid.ViewModels;

namespace DesktopIconGrid.Views;

/// <summary>
/// 調整位置… (0.15.0 WP6): the whole picture, dimmed but for a bright frame over what the bar shows, a cross at the focus
/// point, a zoom slider, and under them a strip with the bar's look at its proportions: the colour at Settings'
/// transparency, the picture as fitted at its strength, and a few sample icons with names, so reading over it can be
/// judged. It changes Settings' pending picture at OK; the bar changes at Settings' Save.
/// </summary>
public partial class PicturePositionWindow : Window
{
    private const double StripMaxHeight = 120;
    private const double StripMinHeight = 28;
    private readonly BitmapSource _picture;
    private readonly StripLook _look;
    private Vector _grab;

    /// <summary>The strip's look: the theme's colour and text colour, and Settings' background opacity in percent.</summary>
    public sealed record StripLook(string Background, string Foreground, int Opacity);

    public PicturePositionWindow(PicturePositionViewModel viewModel, BitmapSource picture, StripLook look)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ViewModel = viewModel;
        _picture = picture;
        _look = look;
        DataContext = viewModel;
        InitializeComponent();
        DialogTheme.Apply(this);
        PictureImage.Source = picture;
        BuildStrip();
        viewModel.PropertyChanged += OnChanged;
        Place();
    }

    public PicturePositionViewModel ViewModel { get; }

    /// <summary>
    /// Shows the dialog over <paramref name="owner"/> for <paramref name="image"/> on a bar of <paramref name="bar"/>'s
    /// proportions; returns the picture with its new focus and zoom, or <see langword="null"/> for Cancel.
    /// </summary>
    public static BackgroundImage? Adjust(Window? owner, BitmapSource picture, Size bar, BackgroundImage image, StripLook look)
    {
        ArgumentNullException.ThrowIfNull(picture);
        var viewModel = new PicturePositionViewModel(picture.PixelWidth, picture.PixelHeight, bar, image);
        var window = new PicturePositionWindow(viewModel, picture, look) { Owner = owner };
        return window.ShowDialog() == true ? viewModel.ApplyTo(image) : null;
    }

    /// <summary>The strip's size: as wide as the preview, as tall as the bar's proportions make it, within its limits.</summary>
    internal static Size StripSize(double previewWidth, double barAspect)
    {
        var width = Math.Max(previewWidth, 320);
        var height = width / Math.Max(barAspect, 0.01);
        if (height > StripMaxHeight)
        {
            height = StripMaxHeight;
            width = height * barAspect;
        }

        return new Size(Math.Max(1, width), Math.Max(StripMinHeight, height));
    }

    private void OnChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PicturePositionViewModel.Frame))
        {
            Place();
        }
    }

    /// <summary>The dimming, the frame, the cross and the strip's picture where the focus and zoom put them.</summary>
    private void Place()
    {
        var frame = ViewModel.Frame;
        var whole = new Rect(0, 0, ViewModel.PreviewWidth, ViewModel.PreviewHeight);
        Dim.Data = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(whole), new RectangleGeometry(frame));
        PlaceRectangle(FrameInner, frame);
        PlaceRectangle(FrameOuter, Rect.Inflate(frame, 1, 1));
        var cross = ViewModel.Cross;
        CrossMark.Data = new GeometryGroup
        {
            Children =
            {
                new LineGeometry(new Point(cross.X - 6, cross.Y), new Point(cross.X + 6, cross.Y)),
                new LineGeometry(new Point(cross.X, cross.Y - 6), new Point(cross.X, cross.Y + 6)),
            },
        };

        var (x, y, width, height) = ViewModel.SourceRect;
        StripPicture.Fill = new ImageBrush(_picture)
        {
            ViewboxUnits = BrushMappingMode.RelativeToBoundingBox,
            Viewbox = new Rect(x / _picture.PixelWidth, y / _picture.PixelHeight, width / _picture.PixelWidth, height / _picture.PixelHeight),
            Stretch = Stretch.Fill,
        };
    }

    private static void PlaceRectangle(Rectangle rectangle, Rect rect)
    {
        Canvas.SetLeft(rectangle, rect.X);
        Canvas.SetTop(rectangle, rect.Y);
        rectangle.Width = Math.Max(0, rect.Width);
        rectangle.Height = Math.Max(0, rect.Height);
    }

    /// <summary>The strip at the bar's proportions, its colour at the background's opacity and the picture at its strength over it.</summary>
    private void BuildStrip()
    {
        var size = StripSize(ViewModel.PreviewWidth, ViewModel.BarAspect);
        Strip.Width = size.Width + 2;
        Strip.Height = size.Height + 2;
        Strip.BorderBrush = ThemeManager.ToBrush(_look.Foreground, 0.4);
        StripColour.Fill = ThemeManager.ToBrush(_look.Background, Math.Clamp(_look.Opacity, 0, 100) / 100.0);
        StripPicture.Opacity = ViewModel.Strength / 100.0 * Math.Clamp(_look.Opacity, 0, 100) / 100.0;

        var edge = MainWindow.TextEdgeFor(_look.Foreground); // over a picture the names have their edge at every opacity
        var tile = Math.Clamp(size.Height - 26, 16, 40);
        foreach (var name in Strings.Position_Samples.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var sample = new StackPanel { Margin = new Thickness(0, 0, 10, 0), HorizontalAlignment = HorizontalAlignment.Center };
            sample.Children.Add(new Border
            {
                Width = tile,
                Height = tile,
                CornerRadius = new CornerRadius(5),
                Background = ThemeManager.ToBrush(_look.Foreground, 0.85),
                HorizontalAlignment = HorizontalAlignment.Center,
                Child = new TextBlock
                {
                    Text = name[..1],
                    FontWeight = FontWeights.SemiBold,
                    Foreground = ThemeManager.ToBrush(_look.Background),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            });
            sample.Children.Add(new TextBlock
            {
                Text = name,
                FontSize = 11,
                Foreground = ThemeManager.ToBrush(_look.Foreground),
                Effect = edge,
                HorizontalAlignment = HorizontalAlignment.Center,
            });
            StripIcons.Children.Add(sample);
        }
    }

    private void Preview_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        Preview.Focus();
        Preview.CaptureMouse();
        var point = e.GetPosition(Preview);
        var frame = ViewModel.Frame;
        if (frame.Contains(point))
        {
            _grab = point - new Point(frame.X + (frame.Width / 2), frame.Y + (frame.Height / 2)); // the frame moves with the pointer
        }
        else
        {
            _grab = default; // a click elsewhere puts the focus there
            ViewModel.FocusAt(point);
        }

        e.Handled = true;
    }

    private void Preview_MouseMove(object sender, MouseEventArgs e)
    {
        if (Preview.IsMouseCaptured)
        {
            ViewModel.FocusAt(e.GetPosition(Preview) - _grab);
        }
    }

    private void Preview_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => Preview.ReleaseMouseCapture();

    private void Preview_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        ViewModel.ZoomBy(e.Delta > 0 ? 1 : -1);
        e.Handled = true;
    }

    private void Preview_KeyDown(object sender, KeyEventArgs e)
    {
        var step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 0.05 : 0.01;
        switch (e.Key)
        {
            case Key.Left:
                ViewModel.MoveFocus(-step, 0);
                break;
            case Key.Right:
                ViewModel.MoveFocus(step, 0);
                break;
            case Key.Up:
                ViewModel.MoveFocus(0, -step);
                break;
            case Key.Down:
                ViewModel.MoveFocus(0, step);
                break;
            case Key.OemPlus or Key.Add:
                ViewModel.ZoomBy(1);
                break;
            case Key.OemMinus or Key.Subtract:
                ViewModel.ZoomBy(-1);
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private void Centre_Click(object sender, RoutedEventArgs e) => ViewModel.Centre();

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    protected override void OnClosed(EventArgs e)
    {
        ViewModel.PropertyChanged -= OnChanged;
        base.OnClosed(e);
    }
}
