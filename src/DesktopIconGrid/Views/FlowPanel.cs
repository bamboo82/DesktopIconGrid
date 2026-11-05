using System.Windows;
using System.Windows.Controls;
using DesktopIconGrid.Core.Models;

namespace DesktopIconGrid.Views;

/// <summary>
/// The icon grid's panel: cells of <see cref="ItemWidth"/> × <see cref="ItemHeight"/> in reading order that wrap
/// like a wrap panel, with dividers and captions (<see cref="SlotProperty"/>) narrower than a cell. The layout itself
/// is <see cref="FlowLayout"/>; this panel measures the children and places them where it says.
/// </summary>
public sealed class FlowPanel : Panel
{
    public static readonly DependencyProperty ItemWidthProperty = DependencyProperty.Register(
        nameof(ItemWidth), typeof(double), typeof(FlowPanel), new FrameworkPropertyMetadata(48.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty ItemHeightProperty = DependencyProperty.Register(
        nameof(ItemHeight), typeof(double), typeof(FlowPanel), new FrameworkPropertyMetadata(48.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>How much room a child takes; set on the item containers from the item's view model.</summary>
    public static readonly DependencyProperty SlotProperty = DependencyProperty.RegisterAttached(
        "Slot", typeof(FlowSlot), typeof(FlowPanel),
        new FrameworkPropertyMetadata(FlowSlot.Cell, FrameworkPropertyMetadataOptions.AffectsParentMeasure | FrameworkPropertyMetadataOptions.AffectsParentArrange));

    public double ItemWidth
    {
        get => (double)GetValue(ItemWidthProperty);
        set => SetValue(ItemWidthProperty, value);
    }

    public double ItemHeight
    {
        get => (double)GetValue(ItemHeightProperty);
        set => SetValue(ItemHeightProperty, value);
    }

    public static FlowSlot GetSlot(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (FlowSlot)element.GetValue(SlotProperty);
    }

    public static void SetSlot(DependencyObject element, FlowSlot value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(SlotProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var entries = new List<FlowEntry>(InternalChildren.Count);
        foreach (UIElement child in InternalChildren)
        {
            var slot = GetSlot(child);
            child.Measure(slot == FlowSlot.Cell ? new Size(ItemWidth, ItemHeight) : new Size(double.PositiveInfinity, ItemHeight));
            entries.Add(new FlowEntry(slot, child.DesiredSize.Width, child.DesiredSize.Height));
        }

        var (width, height) = FlowLayout.Extent(FlowLayout.Arrange(entries, availableSize.Width, ItemWidth, ItemHeight));
        return new Size(double.IsInfinity(availableSize.Width) ? width : Math.Min(width, availableSize.Width), height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var entries = new List<FlowEntry>(InternalChildren.Count);
        foreach (UIElement child in InternalChildren)
        {
            entries.Add(new FlowEntry(GetSlot(child), child.DesiredSize.Width, child.DesiredSize.Height));
        }

        var boxes = FlowLayout.Arrange(entries, finalSize.Width, ItemWidth, ItemHeight);
        for (var i = 0; i < boxes.Count; i++)
        {
            var box = boxes[i];
            InternalChildren[i].Arrange(new Rect(box.X, box.Y, box.Width, box.Height));
        }

        return finalSize;
    }
}
