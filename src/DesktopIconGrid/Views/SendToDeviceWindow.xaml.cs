using System.Windows;

namespace DesktopIconGrid.Views;

public partial class SendToDeviceWindow : Window
{
    public SendToDeviceWindow()
    {
        InitializeComponent();
        DialogTheme.Apply(this);
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
