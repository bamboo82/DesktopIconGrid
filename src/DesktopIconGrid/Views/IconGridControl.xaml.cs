using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DesktopIconGrid.Core.Launching;
using DesktopIconGrid.Core.SpecialItems;
using DesktopIconGrid.ViewModels;

namespace DesktopIconGrid.Views;

/// <summary>
/// A flowing grid of icons bound to an <see cref="ItemListViewModel"/>. Used for the bar itself and for
/// group popups. Handles clicks, keyboard shortcuts, drag-to-reorder, drops from Explorer and context menus.
/// </summary>
public partial class IconGridControl : UserControl
{
    private const int ClickFromMouseWindowMs = 150;

    private Point _dragStart;
    private ItemViewModel? _dragCandidate;
    private Button? _dragButton;
    private long _lastMouseUpTicks;
    private ItemViewModel? _hoveredGroup;

    public IconGridControl()
    {
        InitializeComponent();
    }

    private ItemListViewModel? List => DataContext as ItemListViewModel;

    private readonly record struct DropTarget(DragDropEffects Effects, int Index, ItemViewModel? Group)
    {
        public static readonly DropTarget None = new(DragDropEffects.None, 0, null);
    }

    // ----- activation -----------------------------------------------------------------------------

    private static ItemViewModel? ItemOf(object sender) => (sender as FrameworkElement)?.DataContext as ItemViewModel;

    private void Item_Click(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is not { } item || List is null)
        {
            return;
        }

        var fromMouse = Environment.TickCount64 - _lastMouseUpTicks < ClickFromMouseWindowMs;
        if (fromMouse && item.IsShortcut && List.Root.DoubleClickToLaunch)
        {
            return; // handled by MouseDoubleClick in that mode
        }

        List.Root.Activate(item, sender);
        e.Handled = true;
    }

    private void Item_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || ItemOf(sender) is not { IsShortcut: true } item || List is null)
        {
            return;
        }

        if (List.Root.DoubleClickToLaunch)
        {
            List.Root.Activate(item, sender);
            e.Handled = true;
        }
    }

    private void Item_KeyDown(object sender, KeyEventArgs e)
    {
        if (ItemOf(sender) is not { } item)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Delete:
                item.RemoveCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.F2:
                item.PropertiesCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    // ----- drag source ----------------------------------------------------------------------------

    private void Item_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(this);
        _dragCandidate = ItemOf(sender);
        _dragButton = sender as Button;
    }

    private void Item_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _lastMouseUpTicks = Environment.TickCount64;
        _dragCandidate = null;
        _dragButton = null;
    }

    private void Item_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragCandidate is null || _dragButton is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var delta = e.GetPosition(this) - _dragStart;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var item = _dragCandidate;
        var button = _dragButton;
        _dragCandidate = null;
        _dragButton = null;

        var data = new DataObject(DragState.ItemFormat, item.Id.ToString());
        DragState.Current = item;
        try
        {
            button.ReleaseMouseCapture();
            DragDrop.DoDragDrop(button, data, DragDropEffects.Move);
        }
        finally
        {
            DragState.Current = null;
            ClearDropFeedback();
        }
    }

    // ----- drop target ----------------------------------------------------------------------------

    private void Surface_DragOver(object sender, DragEventArgs e)
    {
        var target = ResolveDropTarget(e);
        e.Effects = target.Effects;
        ShowDropFeedback(target);
        e.Handled = true;
    }

    private void Surface_DragLeave(object sender, DragEventArgs e)
    {
        ClearDropFeedback();
    }

    private void Surface_Drop(object sender, DragEventArgs e)
    {
        var target = ResolveDropTarget(e);
        ClearDropFeedback();
        e.Handled = true;

        if (target.Effects == DragDropEffects.None || List is null)
        {
            return;
        }

        var root = List.Root;
        if (e.Data.GetDataPresent(DragState.ItemFormat)
            && Guid.TryParse(e.Data.GetData(DragState.ItemFormat) as string, out var id))
        {
            if (target.Group is not null)
            {
                root.MoveIntoGroup(id, target.Group);
            }
            else
            {
                root.MoveItem(id, List, target.Index);
            }

            return;
        }

        var list = target.Group?.Children ?? List;
        var index = target.Group is null ? target.Index : int.MaxValue;

        if (e.Data.GetDataPresent(DataFormats.FileDrop) && e.Data.GetData(DataFormats.FileDrop) is string[] paths)
        {
            root.AddPaths(paths, list, index);
            return;
        }

        if (TryGetLink(e.Data, out var link))
        {
            root.AddLink(link, list, index);
        }
    }

    private DropTarget ResolveDropTarget(DragEventArgs e)
    {
        if (List is null)
        {
            return DropTarget.None;
        }

        var isInternal = e.Data.GetDataPresent(DragState.ItemFormat);
        var isExternal = !isInternal && (e.Data.GetDataPresent(DataFormats.FileDrop) || TryGetLink(e.Data, out _));
        if (!isInternal && !isExternal)
        {
            return DropTarget.None;
        }

        var dragged = isInternal ? DragState.Current : null;
        if (isInternal && dragged is null)
        {
            return DropTarget.None;
        }

        var effects = isInternal ? DragDropEffects.Move : DragDropEffects.Copy;
        var (index, group) = HitTest(e.GetPosition(ItemsHost), dragged);
        if (group is not null)
        {
            return new DropTarget(effects, index, group);
        }

        if (dragged is { IsGroup: true } && !List.IsRoot)
        {
            return DropTarget.None; // groups cannot be nested
        }

        return new DropTarget(effects, index, null);
    }

    /// <summary>
    /// Finds where a point maps in reading order: the index to insert before, or a group to drop into when
    /// the point is over the middle of a group icon.
    /// </summary>
    private (int Index, ItemViewModel? Group) HitTest(Point position, ItemViewModel? dragged)
    {
        var count = ItemsHost.Items.Count;
        var allowGroupTarget = dragged is null || !dragged.IsGroup;
        var insertIndex = count;

        for (var i = 0; i < count; i++)
        {
            if (ContainerAt(i) is not { } container)
            {
                continue;
            }

            var bounds = container.TransformToAncestor(ItemsHost).TransformBounds(new Rect(container.RenderSize));
            if (position.Y < bounds.Top)
            {
                insertIndex = i;
                break;
            }

            if (position.Y > bounds.Bottom)
            {
                insertIndex = i + 1;
                continue;
            }

            if (position.X < bounds.Left)
            {
                insertIndex = i;
                break;
            }

            if (position.X > bounds.Right)
            {
                insertIndex = i + 1;
                continue;
            }

            var item = ItemsHost.Items[i] as ItemViewModel;
            if (allowGroupTarget && item is { IsGroup: true } && !ReferenceEquals(item, dragged))
            {
                var zone = bounds.Width * 0.25;
                if (position.X >= bounds.Left + zone && position.X <= bounds.Right - zone)
                {
                    return (i, item);
                }
            }

            insertIndex = position.X < bounds.Left + bounds.Width / 2 ? i : i + 1;
            break;
        }

        return (insertIndex, null);
    }

    private FrameworkElement? ContainerAt(int index) =>
        ItemsHost.ItemContainerGenerator.ContainerFromIndex(index) as FrameworkElement;

    private void ShowDropFeedback(DropTarget target)
    {
        if (target.Effects == DragDropEffects.None)
        {
            ClearDropFeedback();
            return;
        }

        SetHoveredGroup(target.Group);
        if (target.Group is not null)
        {
            DropIndicator.Visibility = Visibility.Collapsed;
            return;
        }

        var count = ItemsHost.Items.Count;
        Rect anchor;
        var after = false;
        if (count == 0)
        {
            anchor = new Rect(ItemsHost.TranslatePoint(new Point(), Overlay), new Size(0, List?.Root.CellHeight ?? 48));
        }
        else
        {
            after = target.Index >= count;
            if (ContainerAt(Math.Min(target.Index, count - 1)) is not { } container)
            {
                DropIndicator.Visibility = Visibility.Collapsed;
                return;
            }

            anchor = container.TransformToVisual(Overlay).TransformBounds(new Rect(container.RenderSize));
        }

        Canvas.SetLeft(DropIndicator, after ? anchor.Right - 1 : anchor.Left - 2);
        Canvas.SetTop(DropIndicator, anchor.Top + 2);
        DropIndicator.Height = Math.Max(8, anchor.Height - 4);
        DropIndicator.Visibility = Visibility.Visible;
    }

    private void ClearDropFeedback()
    {
        DropIndicator.Visibility = Visibility.Collapsed;
        SetHoveredGroup(null);
    }

    private void SetHoveredGroup(ItemViewModel? group)
    {
        if (ReferenceEquals(_hoveredGroup, group))
        {
            return;
        }

        if (_hoveredGroup is not null)
        {
            _hoveredGroup.IsDropTarget = false;
        }

        _hoveredGroup = group;
        if (_hoveredGroup is not null)
        {
            _hoveredGroup.IsDropTarget = true;
        }
    }

    private static bool TryGetLink(IDataObject data, out string link)
    {
        link = string.Empty;
        foreach (var format in new[] { "UniformResourceLocatorW", "UniformResourceLocator", DataFormats.UnicodeText, DataFormats.Text })
        {
            if (!data.GetDataPresent(format))
            {
                continue;
            }

            string? text;
            try
            {
                text = data.GetData(format) switch
                {
                    string s => s,
                    MemoryStream stream => ReadUrlStream(stream, format.EndsWith('W')),
                    _ => null,
                };
            }
            catch (Exception ex) when (ex is COMException or InvalidOperationException)
            {
                continue;
            }

            text = text?.Trim();
            if (!string.IsNullOrEmpty(text) && !text.Contains('\n') && LaunchPlanner.IsUri(text))
            {
                link = text;
                return true;
            }
        }

        return false;
    }

    private static string ReadUrlStream(MemoryStream stream, bool unicode)
    {
        var bytes = stream.ToArray();
        var text = unicode ? Encoding.Unicode.GetString(bytes) : Encoding.Default.GetString(bytes);
        return text.TrimEnd('\0');
    }

    // ----- context menus --------------------------------------------------------------------------

    private void SurfaceMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is ContextMenu menu && List is { } list)
        {
            PopulateSpecialItems(menu, definition => list.Root.AddSpecialItem(definition, list, int.MaxValue));
        }
    }

    private void ItemMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is ContextMenu menu && menu.DataContext is ItemViewModel item)
        {
            PopulateSpecialItems(menu, definition => item.Root.AddSpecialItemAfter(definition, item));
        }
    }

    private void PopulateSpecialItems(ContextMenu menu, Action<SpecialItemDefinition> onPick)
    {
        var host = menu.Items.OfType<MenuItem>().FirstOrDefault(m => Equals(m.Tag, "special"));
        if (host is null || List is null)
        {
            return;
        }

        host.Items.Clear();
        var root = List.Root;
        var first = true;
        foreach (var section in root.SpecialItemSections)
        {
            if (!first)
            {
                host.Items.Add(new Separator());
            }

            first = false;
            foreach (var definition in section.Items)
            {
                var entry = new MenuItem
                {
                    Header = definition.Name.Replace("_", "__", StringComparison.Ordinal),
                    Icon = CreateMenuIcon(root, definition),
                };
                var captured = definition;
                entry.Click += (_, _) => onPick(captured);
                host.Items.Add(entry);
            }
        }

        host.IsEnabled = host.Items.Count > 0;
    }

    private static Image? CreateMenuIcon(LauncherViewModel root, SpecialItemDefinition definition)
    {
        var source = root.Icons.GetIcon(definition.IconPath ?? definition.Location, 16);
        return source is null
            ? null
            : new Image { Source = source, Width = 16, Height = 16, Stretch = Stretch.Uniform };
    }
}
