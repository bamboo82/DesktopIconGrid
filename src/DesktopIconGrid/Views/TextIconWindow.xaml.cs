using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
            SymbolList.ScrollIntoView(symbol);
        }

        SearchBox.Focus();
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
