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

    /// <summary>Down from the search box goes into the grid: to the chosen symbol, else the first one listed.</summary>
    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Down || SymbolList.Items.Count == 0)
        {
            return;
        }

        var target = SymbolList.SelectedItem ?? SymbolList.Items[0];
        SymbolList.ScrollIntoView(target);
        SymbolList.UpdateLayout();
        if (SymbolList.ItemContainerGenerator.ContainerFromItem(target) is ListBoxItem cell)
        {
            cell.Focus();
            e.Handled = true;
        }
    }

    private void Symbol_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel?.CanSave == true)
        {
            DialogResult = true;
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
