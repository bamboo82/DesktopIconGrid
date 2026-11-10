using System.Windows;

namespace DesktopIconGrid.Views;

public partial class ClockTileWindow : Window
{
    public ClockTileWindow()
    {
        InitializeComponent();
        DialogTheme.Apply(this);
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
