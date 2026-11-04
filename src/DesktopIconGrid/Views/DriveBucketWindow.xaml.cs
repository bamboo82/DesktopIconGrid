using System.Windows;

namespace DesktopIconGrid.Views;

public partial class DriveBucketWindow : Window
{
    public DriveBucketWindow()
    {
        InitializeComponent();
    }

    private void Save_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
