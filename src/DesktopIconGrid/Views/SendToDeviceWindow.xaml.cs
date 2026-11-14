using System.Windows;
using DesktopIconGrid.ViewModels;

namespace DesktopIconGrid.Views;

public partial class SendToDeviceWindow : Window
{
    public SendToDeviceWindow()
    {
        InitializeComponent();
        DialogTheme.Apply(this);

        // The list is looked for as the window shows, never before it, and off the UI thread, so the window paints at
        // once (review 27 S2); a look still under way when the window closes is given up.
        Loaded += (_, _) => ViewModel?.Look();
        Closed += (_, _) => ViewModel?.Cancel();
    }

    private SendToDeviceViewModel? ViewModel => DataContext as SendToDeviceViewModel;

    private void LookAgain_Click(object sender, RoutedEventArgs e) => ViewModel?.Look();

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
