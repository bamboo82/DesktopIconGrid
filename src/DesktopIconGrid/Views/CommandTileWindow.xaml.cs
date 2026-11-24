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

    /// <summary>
    /// 文字外觀…: the widget's own text in the text icon window's Widget mode, with what its tile shows now and the name
    /// typed here; kept until OK here writes it with the rest (0.15.0 WP7b).
    /// </summary>
    private void TextLook_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is CommandTileViewModel viewModel
            && Services.TextLookWindows.Widget(this, viewModel.ItemId, viewModel.Name.Trim(), viewModel.TextLook, viewModel.NameLook, isClock: false) is { } answer)
        {
            viewModel.TextLook = answer.Look;
            viewModel.NameLook = answer.NameLook; // both parts (0.16.0 WP4)
        }
    }

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
