using System.ComponentModel;
using System.Threading;
using System.Windows;

namespace DesktopIconGrid.Views;

/// <summary>
/// A small "working…" window with a Cancel button for an awaited operation: cancelling signals the
/// <see cref="CancellationTokenSource"/>, and the caller closes the window with <see cref="Finish"/>.
/// </summary>
public partial class BusyWindow : Window
{
    private readonly CancellationTokenSource _cancellation;
    private bool _finished;

    public BusyWindow(CancellationTokenSource cancellation)
    {
        _cancellation = cancellation;
        InitializeComponent();
        DialogTheme.Apply(this);
    }

    /// <summary>Shows the window (not modal) over <paramref name="owner"/>, which is disabled until <see cref="Finish"/>.</summary>
    public static BusyWindow Open(Window owner, string title, string message, CancellationTokenSource cancellation)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var window = new BusyWindow(cancellation) { Title = title, Owner = owner };
        window.MessageText.Text = message;
        owner.IsEnabled = false;
        window.Show();
        return window;
    }

    /// <summary>Closes the window and re-enables the owner.</summary>
    public void Finish()
    {
        if (_finished)
        {
            return;
        }

        _finished = true;
        if (Owner is { } owner)
        {
            owner.IsEnabled = true;
        }

        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        CancelButton.IsEnabled = false;
        MessageText.Text = "Cancelling…";
        _cancellation.Cancel();
    }

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        if (!_finished)
        {
            // Closing with Esc or the title bar is a cancel; the caller closes the window when the work ends.
            e.Cancel = true;
            Cancel_Click(sender, new RoutedEventArgs());
        }
    }
}
