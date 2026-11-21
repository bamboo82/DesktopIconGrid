using System.Windows;
using DesktopIconGrid.ViewModels;

namespace DesktopIconGrid.Views;

public partial class MonitorIconWindow : Window
{
    public MonitorIconWindow()
    {
        InitializeComponent();
        DialogTheme.Apply(this);
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    /// <summary>
    /// 文字外觀…: the widget's own text in the text icon window's Widget mode, with what its tile shows now and the name
    /// typed here; kept until OK here writes it with the rest (0.15.0 WP7b).
    /// </summary>
    private void TextLook_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MonitorIconViewModel viewModel && Services.TextLookWindows.Widget(this, viewModel.ItemId, viewModel.Name.Trim(), viewModel.TextLook) is { } answer)
        {
            viewModel.TextLook = answer.Look;
        }
    }
}
