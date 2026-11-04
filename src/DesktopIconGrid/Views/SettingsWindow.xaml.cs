using System.Windows;

namespace DesktopIconGrid.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
    }

    private void Save_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
