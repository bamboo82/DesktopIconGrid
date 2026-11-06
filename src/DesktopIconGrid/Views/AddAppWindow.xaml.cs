using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using DesktopIconGrid.Core.Imports;
using DesktopIconGrid.ViewModels;

namespace DesktopIconGrid.Views;

/// <summary>Add apps: every installed app from the Start menu's list, ticked to be added to the bar.</summary>
public partial class AddAppWindow : Window
{
    private readonly CancellationTokenSource _closing = new();

    public AddAppWindow()
    {
        InitializeComponent();
        DialogTheme.Apply(this);
    }

    /// <summary>Lists the apps; set before the window is shown.</summary>
    public Func<Task<IReadOnlyList<InstalledApp>>>? Load { get; set; }

    /// <summary>Fetches one app's icon; set before the window is shown.</summary>
    public Func<string, Task<ImageSource?>>? IconOf { get; set; }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        FilterBox.Focus();
        if (DataContext is AddAppViewModel viewModel && Load is { } load)
        {
            await viewModel.LoadAsync(load, IconOf, _closing.Token);
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e) => _closing.Cancel();

    protected override void OnClosed(EventArgs e)
    {
        _closing.Dispose();
        base.OnClosed(e);
    }

    private void Add_Click(object sender, RoutedEventArgs e) => DialogResult = DataContext is AddAppViewModel { CanAdd: true };
}
