using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DesktopIconGrid.Core.Launching;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Core.SpecialItems;
using DesktopIconGrid.SpecialIcons;
using DesktopIconGrid.ViewModels;

namespace DesktopIconGrid.Views;

/// <summary>
/// A flowing grid of icons bound to an <see cref="ItemListViewModel"/>. Used for the bar itself and for
/// group popups. Handles clicks, keyboard shortcuts, drag-to-reorder, drops from Explorer and context menus.
/// </summary>
/// <remarks>
/// Drop rules for files from Explorer: a <b>right</b>-button drag always places shortcuts at the drop position
/// (into a group when dropped on one). A <b>left</b>-button drag dropped on a shortcut opens the files with that
/// shortcut's program; dropped on a group it moves them in; dropped on empty space it places shortcuts.
/// </remarks>
public partial class IconGridControl : UserControl
{
    private const int ClickFromMouseWindowMs = 150;
    private static readonly TimeSpan HoldDelay = TimeSpan.FromMilliseconds(300);

    private Point _dragStart;
    private ItemViewModel? _dragCandidate;
    private Button? _dragButton;
    private long _lastMouseUpTicks;
    private ItemViewModel? _hoveredItem;
    private DragDropKeyStates _lastDragButtons;
    private readonly DispatcherTimer _holdTimer;
    private ItemViewModel? _holdCandidate;
    private ItemViewModel? _holdActive;
    private bool _suppressNextClick;

    public IconGridControl()
    {
        InitializeComponent();
        _holdTimer = new DispatcherTimer(HoldDelay, DispatcherPriority.Input, OnHoldTimerTick, Dispatcher);
        _holdTimer.Stop();
    }

    private ItemListViewModel? List => DataContext as ItemListViewModel;

    private enum DropAction
    {
        None,
        Insert,
        IntoGroup,
        OpenWith,
    }

    private readonly record struct DropTarget(DragDropEffects Effects, DropAction Action, int Index, ItemViewModel? Item)
    {
        public static readonly DropTarget None = new(DragDropEffects.None, DropAction.None, 0, null);
    }

    // ----- activation -----------------------------------------------------------------------------

    private static ItemViewModel? ItemOf(object sender) => (sender as FrameworkElement)?.DataContext as ItemViewModel;

    private void Item_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressNextClick)
        {
            _suppressNextClick = false;
            e.Handled = true;
            return;
        }

        if (ItemOf(sender) is not { } item || List is null)
        {
            return;
        }

        var fromMouse = Environment.TickCount64 - _lastMouseUpTicks < ClickFromMouseWindowMs;
        if (fromMouse && !item.IsGroup && List.Root.DoubleClickToLaunch)
        {
            return; // handled by MouseDoubleClick in that mode
        }

        List.Root.Activate(item, sender, CurrentModifiers());
        e.Handled = true;
    }

    private void Item_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || ItemOf(sender) is not { IsGroup: false } item || List is null)
        {
            return;
        }

        if (List.Root.DoubleClickToLaunch)
        {
            List.Root.Activate(item, sender, CurrentModifiers());
            e.Handled = true;
        }
    }

    private static ActivationModifiers CurrentModifiers()
    {
        var modifiers = ActivationModifiers.None;
        var keys = Keyboard.Modifiers;
        if (keys.HasFlag(ModifierKeys.Control))
        {
            modifiers |= ActivationModifiers.Control;
        }

        if (keys.HasFlag(ModifierKeys.Shift))
        {
            modifiers |= ActivationModifiers.Shift;
        }

        if (keys.HasFlag(ModifierKeys.Alt))
        {
            modifiers |= ActivationModifiers.Alt;
        }

        return modifiers;
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
        _suppressNextClick = false;
        _dragStart = e.GetPosition(this);
        _dragCandidate = ItemOf(sender);
        _dragButton = sender as Button;

        // Press-and-hold icons (e.g. the Sound Replayer) start their hold after a short delay.
        _holdCandidate = _dragCandidate is { Model: SpecialIconItem special } candidate && candidate.SpecialHandler.SupportsPressAndHold(special)
            ? candidate
            : null;
        _holdTimer.Stop();
        if (_holdCandidate is not null)
        {
            _holdTimer.Start();
        }
    }

    private void Item_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _lastMouseUpTicks = Environment.TickCount64;
        _dragCandidate = null;
        _dragButton = null;
        _holdTimer.Stop();
        _holdCandidate = null;

        if (_holdActive is { Model: SpecialIconItem special } active)
        {
            _holdActive = null;
            _suppressNextClick = true; // the release ends the hold; it is not a click
            active.SpecialHandler.HoldEnded(special);
        }
    }

    private void OnHoldTimerTick(object? sender, EventArgs e)
    {
        _holdTimer.Stop();
        var candidate = _holdCandidate;
        _holdCandidate = null;
        if (candidate is not { Model: SpecialIconItem special } || Mouse.LeftButton != MouseButtonState.Pressed || !ReferenceEquals(_dragCandidate, candidate))
        {
            return;
        }

        _dragCandidate = null; // holding, not dragging
        _dragButton = null;
        if (candidate.SpecialHandler.HoldStarted(special))
        {
            _holdActive = candidate;
        }
    }

    /// <summary>A dialog, Alt+Tab or another window can take the capture mid-press; end any hold so the grid never gets stuck.</summary>
    private void Item_LostMouseCapture(object sender, MouseEventArgs e)
    {
        _holdTimer.Stop();
        _holdCandidate = null;
        _dragCandidate = null;
        _dragButton = null;
        if (_holdActive is { Model: SpecialIconItem special } active)
        {
            _holdActive = null;
            active.SpecialHandler.HoldEnded(special);
        }
    }

    private void Item_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_holdActive is not null)
        {
            return; // a hold is in progress; moving does not start a drag
        }

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
        _holdTimer.Stop();
        _holdCandidate = null;

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
        _lastDragButtons = DragDropKeyStates.None;
        e.Handled = true;

        if (target.Action == DropAction.None || List is null)
        {
            return;
        }

        // Read the data now, but act after the OLE drop call has returned: dialogs opened from inside the
        // Drop callback would keep Explorer (the drag source) frozen until they are dismissed.
        var list = List;
        Guid? draggedId = e.Data.GetDataPresent(DragState.ItemFormat)
            && Guid.TryParse(e.Data.GetData(DragState.ItemFormat) as string, out var id)
            ? id
            : null;
        var paths = draggedId is null ? GetDroppedPaths(e.Data) : [];

        Dispatcher.BeginInvoke(DispatcherPriority.Input, () => CompleteDrop(list, target, draggedId, paths));
    }

    private static void CompleteDrop(ItemListViewModel list, DropTarget target, Guid? draggedId, IReadOnlyList<string> paths)
    {
        var root = list.Root;
        if (draggedId is { } id)
        {
            if (target.Action == DropAction.IntoGroup && target.Item is not null)
            {
                root.MoveIntoGroup(id, target.Item);
            }
            else
            {
                root.MoveItem(id, list, target.Index);
            }

            return;
        }

        if (paths.Count == 0)
        {
            return;
        }

        switch (target.Action)
        {
            case DropAction.OpenWith when target.Item is not null:
                root.OpenWith(target.Item, paths);
                break;
            case DropAction.IntoGroup when target.Item?.Children is { } groupList:
                root.AddPaths(paths, groupList, int.MaxValue);
                break;
            default:
                root.AddPaths(paths, list, target.Index);
                break;
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

        // Which button drives the drag. At drop time the buttons may already read as released, so remember them.
        var buttons = e.KeyStates & (DragDropKeyStates.LeftMouseButton | DragDropKeyStates.RightMouseButton);
        if (buttons != DragDropKeyStates.None)
        {
            _lastDragButtons = buttons;
        }
        else
        {
            buttons = _lastDragButtons;
        }

        var rightButton = buttons.HasFlag(DragDropKeyStates.RightMouseButton);
        var (index, over) = HitTest(e.GetPosition(ItemsHost), dragged);

        if (isInternal)
        {
            if (over is { IsGroup: true } && dragged is { IsGroup: false })
            {
                return new DropTarget(DragDropEffects.Move, DropAction.IntoGroup, index, over);
            }

            if (dragged is { IsGroup: true } && !List.IsRoot)
            {
                return DropTarget.None; // groups cannot be nested
            }

            return new DropTarget(DragDropEffects.Move, DropAction.Insert, index, null);
        }

        if (over is { IsGroup: true })
        {
            return new DropTarget(DragDropEffects.Copy, DropAction.IntoGroup, index, over);
        }

        if (!rightButton && over is { AcceptsDroppedFiles: true })
        {
            return new DropTarget(DragDropEffects.Copy, DropAction.OpenWith, index, over);
        }

        return new DropTarget(DragDropEffects.Copy, DropAction.Insert, index, null);
    }

    /// <summary>
    /// Maps a point to the index to insert before (reading order) and, when the point is over the middle of an
    /// icon, that icon.
    /// </summary>
    private (int Index, ItemViewModel? Over) HitTest(Point position, ItemViewModel? dragged)
    {
        var count = ItemsHost.Items.Count;
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

            ItemViewModel? over = null;
            if (ItemsHost.Items[i] is ItemViewModel item && !ReferenceEquals(item, dragged))
            {
                var zone = bounds.Width * 0.25;
                if (position.X >= bounds.Left + zone && position.X <= bounds.Right - zone)
                {
                    over = item;
                }
            }

            insertIndex = position.X < bounds.Left + bounds.Width / 2 ? i : i + 1;
            return (insertIndex, over);
        }

        return (insertIndex, null);
    }

    private FrameworkElement? ContainerAt(int index) =>
        ItemsHost.ItemContainerGenerator.ContainerFromIndex(index) as FrameworkElement;

    private void ShowDropFeedback(DropTarget target)
    {
        if (target.Action == DropAction.None)
        {
            ClearDropFeedback();
            return;
        }

        if (target.Action is DropAction.IntoGroup or DropAction.OpenWith)
        {
            SetHoveredItem(target.Item);
            DropIndicator.Visibility = Visibility.Collapsed;
            return;
        }

        SetHoveredItem(null);

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
        SetHoveredItem(null);
    }

    private void SetHoveredItem(ItemViewModel? item)
    {
        if (ReferenceEquals(_hoveredItem, item))
        {
            return;
        }

        if (_hoveredItem is not null)
        {
            _hoveredItem.IsDropTarget = false;
        }

        _hoveredItem = item;
        if (_hoveredItem is not null)
        {
            _hoveredItem.IsDropTarget = true;
        }
    }

    private static IReadOnlyList<string> GetDroppedPaths(IDataObject data)
    {
        if (data.GetDataPresent(DataFormats.FileDrop) && data.GetData(DataFormats.FileDrop) is string[] files)
        {
            return files.Where(f => !string.IsNullOrWhiteSpace(f)).ToList();
        }

        return TryGetLink(data, out var link) ? [link] : [];
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
            PopulateWindowsItems(menu, definition => list.Root.AddSpecialItem(definition, list, int.MaxValue));
            PopulateSpecialIcons(menu, handler => list.Root.AddSpecialIcon(handler, list, int.MaxValue));
        }
    }

    private void ItemMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is ContextMenu menu && menu.DataContext is ItemViewModel item)
        {
            PopulateWindowsItems(menu, definition => item.Root.AddSpecialItemAfter(definition, item));
            PopulateSpecialIcons(menu, handler => item.Root.AddSpecialIconAfter(handler, item));
            PopulateSpecialActions(menu, item);
        }
    }

    /// <summary>Inserts the special icon's own entries (e.g. "Browse…") above "Remove…".</summary>
    private static void PopulateSpecialActions(ContextMenu menu, ItemViewModel item)
    {
        foreach (var stale in menu.Items.OfType<FrameworkElement>().Where(i => Equals(i.Tag, "special-action")).ToList())
        {
            menu.Items.Remove(stale);
        }

        var anchor = menu.Items.OfType<MenuItem>().FirstOrDefault(m => Equals(m.Tag, "special-actions-anchor"));
        if (anchor is null || item.Model is not SpecialIconItem special)
        {
            return;
        }

        var actions = item.SpecialHandler.GetActions(special);
        if (actions.Count == 0)
        {
            return;
        }

        var index = menu.Items.IndexOf(anchor);
        foreach (var action in actions)
        {
            var entry = new MenuItem { Header = action.Header.Replace("_", "__", StringComparison.Ordinal), IsEnabled = action.IsEnabled, Tag = "special-action" };
            var execute = action.Execute;
            entry.Click += (_, _) => execute();
            menu.Items.Insert(index++, entry);
        }

        menu.Items.Insert(index, new Separator { Tag = "special-action" });
    }

    private void PopulateSpecialIcons(ContextMenu menu, Action<ISpecialIconHandler> onPick)
    {
        var host = menu.Items.OfType<MenuItem>().FirstOrDefault(m => Equals(m.Tag, "special-icons"));
        if (host is null || List is null)
        {
            return;
        }

        host.Items.Clear();
        foreach (var handler in List.Root.SpecialIcons.Handlers)
        {
            var entry = new MenuItem
            {
                Header = handler.DisplayName.Replace("_", "__", StringComparison.Ordinal),
                ToolTip = handler.Description,
            };
            var captured = handler;
            entry.Click += (_, _) => onPick(captured);
            host.Items.Add(entry);
        }

        host.IsEnabled = host.Items.Count > 0;
    }

    private void PopulateWindowsItems(ContextMenu menu, Action<SpecialItemDefinition> onPick)
    {
        var host = menu.Items.OfType<MenuItem>().FirstOrDefault(m => Equals(m.Tag, "windows-items"));
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
