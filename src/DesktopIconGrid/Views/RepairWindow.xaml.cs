using System.ComponentModel;
using System.IO;
using System.Windows;
using DesktopIconGrid.Resources;
using DesktopIconGrid.ViewModels;
using Microsoft.Win32;

namespace DesktopIconGrid.Views;

/// <summary>Repair shortcut: where a missing program is now, and whether the fix is for this PC only.</summary>
public partial class RepairWindow : Window
{
    private readonly CancellationTokenSource _search = new();

    public RepairWindow()
    {
        InitializeComponent();
        DialogTheme.Apply(this);
    }

    private RepairViewModel? ViewModel => DataContext as RepairViewModel;

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel)
        {
            await viewModel.SearchAsync(_search.Token);
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e) => _search.Cancel();

    protected override void OnClosed(EventArgs e)
    {
        _search.Dispose();
        base.OnClosed(e);
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        if (viewModel.IsFolder)
        {
            var folders = new OpenFolderDialog { Title = Strings.Repair_Title, Multiselect = false };
            if (folders.ShowDialog(this) == true && !string.IsNullOrWhiteSpace(folders.FolderName))
            {
                viewModel.AddChosen(folders.FolderName);
            }

            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = Strings.Repair_Title,
            Filter = Strings.Common_AllFilesFilter,
            FileName = Path.GetFileName(viewModel.Location.TrimEnd('\\', '/')),
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        };
        if (dialog.ShowDialog(this) == true)
        {
            viewModel.AddChosen(dialog.FileName);
        }
    }

    private void Repair_Click(object sender, RoutedEventArgs e) => DialogResult = ViewModel?.CanRepair == true;
}
