using System.Windows;
using DesktopIconGrid.ViewModels;

namespace DesktopIconGrid.Views;

public partial class NameTableWindow : Window
{
    public NameTableWindow()
    {
        InitializeComponent();
        DialogTheme.Apply(this);
    }

    private NameTableViewModel? ViewModel => DataContext as NameTableViewModel;

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        Grid.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Row, true);
        DialogResult = true;
    }

    private void UseOriginal_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        Grid.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Row, true);
        foreach (var row in Grid.SelectedItems.OfType<NameRowViewModel>())
        {
            row.GivenName = row.OriginalName;
        }
    }
}
