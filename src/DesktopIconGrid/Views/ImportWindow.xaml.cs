using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using DesktopIconGrid.Core.Imports;
using DesktopIconGrid.ViewModels;

namespace DesktopIconGrid.Views;

/// <summary>Import: shortcuts and sessions other programs keep on this PC, ticked to be added to the bar.</summary>
public partial class ImportWindow : Window
{
    private readonly CancellationTokenSource _closing = new();

    public ImportWindow()
    {
        InitializeComponent();
        DialogTheme.Apply(this);
    }

    /// <summary>Finds what can be imported; set before the window is shown.</summary>
    public Func<Task<IReadOnlyList<ImportCandidate>>>? Load { get; set; }

    /// <summary>Fetches one location's icon; set before the window is shown.</summary>
    public Func<string, Task<ImageSource?>>? IconOf { get; set; }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        List.Focus();
        if (DataContext is ImportViewModel viewModel && Load is { } load)
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

    private void Import_Click(object sender, RoutedEventArgs e) => DialogResult = DataContext is ImportViewModel { CanImport: true };
}
