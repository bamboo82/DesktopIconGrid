using System.Windows;

namespace DesktopIconGrid.Views;

public partial class TextIconWindow : Window
{
    public TextIconWindow()
    {
        InitializeComponent();
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
