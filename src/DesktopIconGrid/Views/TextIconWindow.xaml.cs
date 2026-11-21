using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DesktopIconGrid.ViewModels;

namespace DesktopIconGrid.Views;

public partial class TextIconWindow : Window
{
    public TextIconWindow()
    {
        InitializeComponent();
        DialogTheme.Apply(this);
        Loaded += (_, _) => ShowChosenSymbol();
    }

    private TextIconViewModel? ViewModel => DataContext as TextIconViewModel;

    /// <summary>The text's colour from the colour picker (0.15.0 WP4); the preview follows it, and Cancel puts back what the box had.</summary>
    private void PickColour_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        var before = viewModel.Color;
        var chosen = ColourPickerWindow.Pick(this, before.Length > 0 && viewModel.IsColorValid ? before : null, colour => viewModel.Color = colour, viewModel.TileColors);
        viewModel.Color = chosen ?? before;
    }

    /// <summary>The tile colour from the colour picker (0.15.0 WP4), as the text's colour.</summary>
    private void PickTileColour_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        var before = viewModel.TileColor;
        var chosen = ColourPickerWindow.Pick(this, before.Length > 0 && viewModel.IsTileColorValid ? before : null, colour => viewModel.TileColor = colour, viewModel.TileColors);
        viewModel.TileColor = chosen ?? before;
    }

    /// <summary>An icon that is a symbol opens on Symbol, its symbol scrolled into view and the search box ready.</summary>
    private void ShowChosenSymbol()
    {
        if (ViewModel is not { IsSymbol: true } viewModel)
        {
            return;
        }

        if (viewModel.SelectedSymbol is { } symbol)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => ScrollToCell(symbol));
        }

        SearchBox.Focus();
    }

    /// <summary>
    /// Scrolls the grid so the cell of <paramref name="item"/> is in the middle of it, or as near as the end allows.
    /// <c>ScrollIntoView</c> left a symbol of the last group out of view when the window opened (CI run 182), so this
    /// sets the offset from where the cell is, after layout. The grid scrolls by pixels (<c>CanContentScroll</c> off),
    /// which also keeps the wheel from jumping a whole group at a time.
    /// </summary>
    private void ScrollToCell(object item)
    {
        SymbolList.UpdateLayout();
        if (SymbolList.ItemContainerGenerator.ContainerFromItem(item) is not FrameworkElement cell || Scroller(SymbolList) is not { } scroller)
        {
            return;
        }

        var top = cell.TranslatePoint(new Point(0, 0), scroller).Y;
        if (top < 0 || top + cell.ActualHeight > scroller.ViewportHeight)
        {
            scroller.ScrollToVerticalOffset(Math.Max(0, scroller.VerticalOffset + top - ((scroller.ViewportHeight - cell.ActualHeight) / 2)));
            scroller.UpdateLayout();
        }
    }

    /// <summary>The scroll viewer in a control's template, whatever the theme names it.</summary>
    private static ScrollViewer? Scroller(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if ((child as ScrollViewer ?? Scroller(child)) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>
    /// Down from the search box chooses the first symbol listed and goes to it; Enter with exactly one symbol listed
    /// chooses it and closes as OK does. Before, Down only went to a cell, so "mail", Down, Enter kept the symbol chosen
    /// earlier (release check S1). With more symbols listed, Enter is OK for the one chosen.
    /// </summary>
    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (SymbolList.Items.Count == 0)
        {
            return;
        }

        if (e.Key == Key.Down)
        {
            var first = SymbolList.Items[0];
            SymbolList.SelectedItem = first;
            SymbolList.ScrollIntoView(first);
            SymbolList.UpdateLayout();
            (SymbolList.ItemContainerGenerator.ContainerFromItem(first) as ListBoxItem)?.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && SymbolList.Items.Count == 1)
        {
            SymbolList.SelectedItem = SymbolList.Items[0];
            e.Handled = true;
            if (ViewModel?.CanSave == true)
            {
                DialogResult = true;
            }
        }
    }

    /// <summary>A double-click with the left button takes the symbol, as OK does (not the right one: release check S4).</summary>
    private void Symbol_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && ViewModel?.CanSave == true)
        {
            DialogResult = true;
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
