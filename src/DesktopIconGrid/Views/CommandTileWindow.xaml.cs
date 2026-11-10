using System.IO;
using System.Windows;
using DesktopIconGrid.Resources;
using DesktopIconGrid.ViewModels;
using Microsoft.Win32;

namespace DesktopIconGrid.Views;

public partial class CommandTileWindow : Window
{
    public CommandTileWindow()
    {
        InitializeComponent();
        DialogTheme.Apply(this);
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void BrowseProgram_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not CommandTileViewModel viewModel)
        {
            return;
        }

        var current = Environment.ExpandEnvironmentVariables(viewModel.Program.Trim());
        var dialog = new OpenFileDialog
        {
            Title = Strings.Props_BrowseFile,
            Filter = Strings.Common_AllFilesFilter,
            CheckFileExists = true,
        };
        if (Path.IsPathFullyQualified(current) && Directory.Exists(Path.GetDirectoryName(current)))
        {
            dialog.InitialDirectory = Path.GetDirectoryName(current);
        }

        if (dialog.ShowDialog(this) == true)
        {
            viewModel.Program = dialog.FileName;
            ProgramBox.Focus();
        }
    }
}
