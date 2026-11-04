using System.Windows;

namespace DesktopIconGrid.Views;

public partial class MonitorIconWindow : Window
{
    public MonitorIconWindow()
    {
        InitializeComponent();
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
