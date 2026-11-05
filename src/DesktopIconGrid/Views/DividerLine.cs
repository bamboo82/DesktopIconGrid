using System.Windows;
using System.Windows.Media;

namespace DesktopIconGrid.Views;

/// <summary>
/// A one-pixel line along the longer side of its box, centred: upright between icons in a row, flat across a single
/// column (a bar docked to the left or right edge). Leaves <see cref="Inset"/> free at both ends.
/// </summary>
public sealed class DividerLine : FrameworkElement
{
    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(DividerLine), new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty InsetProperty = DependencyProperty.Register(
        nameof(Inset), typeof(double), typeof(DividerLine), new FrameworkPropertyMetadata(6.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public Brush Stroke
    {
        get => (Brush)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public double Inset
    {
        get => (double)GetValue(InsetProperty);
        set => SetValue(InsetProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        var width = ActualWidth;
        var height = ActualHeight;
        var thickness = 1 / Math.Max(VisualTreeHelper.GetDpi(this).DpiScaleX, 0.01); // one device pixel
        var inset = Inset;
        if (width >= height)
        {
            var y = Math.Round(height / 2);
            drawingContext.DrawRectangle(Stroke, null, new Rect(inset, y, Math.Max(0, width - 2 * inset), thickness));
        }
        else
        {
            var x = Math.Round(width / 2);
            drawingContext.DrawRectangle(Stroke, null, new Rect(x, inset, thickness, Math.Max(0, height - 2 * inset)));
        }
    }
}
