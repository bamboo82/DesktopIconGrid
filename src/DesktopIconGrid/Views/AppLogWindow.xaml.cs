using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using DesktopIconGrid.Core.Launching;
using DesktopIconGrid.Infrastructure;
using DesktopIconGrid.ViewModels;
using Microsoft.Win32;

namespace DesktopIconGrid.Views;

/// <summary>Live, filterable view of the application log.</summary>
public partial class AppLogWindow : Window
{
    private AppLogViewModel? _viewModel;

    public AppLogWindow()
    {
        FocusFilterCommand = new RelayCommand(() =>
        {
            FilterBox.Focus();
            FilterBox.SelectAll();
        });
        SaveCommand = new RelayCommand(() => Save_Click(this, new RoutedEventArgs()));
        ScrollToEndCommand = new RelayCommand(ScrollToEnd);
        InitializeComponent();
        DataContextChanged += (_, _) => Attach();
    }

    public ICommand FocusFilterCommand { get; }

    public ICommand SaveCommand { get; }

    public ICommand ScrollToEndCommand { get; }

    private void Attach()
    {
        if (_viewModel is not null)
        {
            _viewModel.Appended -= OnAppended;
            _viewModel.CopyRequested -= OnCopyRequested;
        }

        _viewModel = DataContext as AppLogViewModel;
        if (_viewModel is not null)
        {
            _viewModel.Appended += OnAppended;
            _viewModel.CopyRequested += OnCopyRequested;
            ScrollToEnd();
        }
    }

    private void OnAppended(object? sender, EventArgs e) => ScrollToEnd();

    private void ScrollToEnd()
    {
        if (Entries.Items.Count > 0)
        {
            Entries.ScrollIntoView(Entries.Items[^1]);
        }
    }

    private void OnCopyRequested(object? sender, string text)
    {
        try
        {
            Clipboard.SetText(text);
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            MessageBox.Show(this, "The clipboard is in use by another program; try again.", "Copy", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void FilterBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _viewModel is not null)
        {
            _viewModel.FilterText = string.Empty;
            e.Handled = true;
        }
    }

    private void Entries_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel?.Selected is { } selected)
        {
            OnCopyRequested(this, selected.FullText);
        }
    }

    private void Entries_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
        {
            var lines = Entries.SelectedItems.OfType<LogEntryViewModel>().Select(r => r.FullText).ToList();
            if (lines.Count > 0)
            {
                OnCopyRequested(this, string.Join(Environment.NewLine, lines));
                e.Handled = true;
            }
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Save the visible log entries",
            FileName = _viewModel.SuggestedFileName(),
            Filter = "Text files|*.txt|Log files|*.log|All files|*.*",
            AddExtension = true,
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            _viewModel.SaveTo(dialog.FileName, visibleOnly: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, ex.Message, "Save log", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenFile_Click(object sender, RoutedEventArgs e)
    {
        var path = _viewModel?.FilePath;
        if (path is null || !File.Exists(path))
        {
            MessageBox.Show(this, "The log file has not been written yet.", "Log file", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var result = ProcessLauncher.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        result.Process?.Dispose();
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.Appended -= OnAppended;
            _viewModel.CopyRequested -= OnCopyRequested;
            _viewModel.Dispose();
            _viewModel = null;
        }
    }
}
