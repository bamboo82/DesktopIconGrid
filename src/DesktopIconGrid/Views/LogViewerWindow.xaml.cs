using System.Windows;
using DesktopIconGrid.Services;
using DesktopIconGrid.ViewModels;

namespace DesktopIconGrid.Views;

/// <summary>Live view of one shortcut's captured output.</summary>
public partial class LogViewerWindow : Window
{
    private ProcessLogSession? _session;

    public LogViewerWindow()
    {
        InitializeComponent();
        DialogTheme.Apply(this);
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is not LogViewerViewModel { Session: { } session })
        {
            return;
        }

        _session = session;
        Output.Text = string.Join(Environment.NewLine, session.Snapshot());
        if (Output.Text.Length > 0)
        {
            Output.AppendText(Environment.NewLine);
        }

        Output.ScrollToEnd();
        session.LineAdded += OnLineAdded;
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        if (_session is not null)
        {
            _session.LineAdded -= OnLineAdded;
            _session = null;
        }
    }

    private void OnLineAdded(object? sender, string line)
    {
        var atEnd = Output.VerticalOffset >= Output.ExtentHeight - Output.ViewportHeight - 2;
        Output.AppendText(line + Environment.NewLine);
        if (atEnd)
        {
            Output.ScrollToEnd();
        }
    }
}
