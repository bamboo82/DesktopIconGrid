using System.Windows;

namespace DesktopIconGrid.Views;

public partial class MonitorIconWindow : Window
{
    public MonitorIconWindow()
    {
        InitializeComponent();
        DialogTheme.Apply(this);
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
