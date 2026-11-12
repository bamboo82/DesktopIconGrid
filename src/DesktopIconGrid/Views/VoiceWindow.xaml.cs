using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using DesktopIconGrid.Core.Voice;
using DesktopIconGrid.Resources;
using DesktopIconGrid.Services;

namespace DesktopIconGrid.Views;

/// <summary>
/// What voice launch shows: a small window above everything, near the bottom of the main screen, that
/// never takes the keyboard from the program in use. It says that the bar is listening (with Cancel), then what it
/// starts or why it starts nothing, and goes away by itself; when the name was not heard clearly it offers the
/// candidates as buttons. One window, made once and hidden between uses.
/// </summary>
public partial class VoiceWindow : Window, IVoiceView
{
    /// <summary>How long a last word stays.</summary>
    private static readonly TimeSpan SayFor = TimeSpan.FromSeconds(3);

    /// <summary>How long a problem stays: it has more to read.</summary>
    private static readonly TimeSpan SayLongFor = TimeSpan.FromSeconds(8);

    /// <summary>How long the choices wait for a click.</summary>
    private static readonly TimeSpan ChooseFor = TimeSpan.FromSeconds(20);

    private readonly DispatcherTimer _timer;
    private Action? _cancel;
    private TaskCompletionSource<VoiceTarget?>? _choice;
    private bool _closing;

    public VoiceWindow()
    {
        InitializeComponent();
        DialogTheme.Apply(this);
        _timer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher);
        _timer.Tick += (_, _) => Dismiss();
    }

    public void Listening(string language, Action cancel)
    {
        Reset();
        _cancel = cancel;
        MessageText.Text = Strings.Voice_Listening;
        DetailText.Text = string.Format(Strings.Voice_ListeningIn, language);
        DetailText.Visibility = Visibility.Visible;
        Progress.Visibility = Visibility.Visible;
        CancelButton.Visibility = Visibility.Visible;
        Present();
    }

    public void Say(string message)
    {
        Reset();
        MessageText.Text = message;
        Present();
        _timer.Interval = message.Length > 60 ? SayLongFor : SayFor;
        _timer.Start();
    }

    public void Done()
    {
        Reset();
        Hide();
    }

    public Task<VoiceTarget?> ChooseAsync(IReadOnlyList<VoiceTarget> targets)
    {
        Reset();
        _choice = new TaskCompletionSource<VoiceTarget?>();
        MessageText.Text = Strings.Voice_Choose;
        Choices.ItemsSource = targets;
        Choices.Visibility = Visibility.Visible;
        CancelButton.Visibility = Visibility.Visible;
        Present();
        _timer.Interval = ChooseFor;
        _timer.Start();
        return _choice.Task;
    }

    /// <summary>Closes the window for good when the app ends.</summary>
    public void CloseForGood()
    {
        _closing = true;
        Reset();
        Close();
    }

    private void Reset()
    {
        _timer.Stop();
        _cancel = null;
        _choice?.TrySetResult(null); // a new listening while the choices were open: none was picked
        _choice = null;
        DetailText.Visibility = Visibility.Collapsed;
        Progress.Visibility = Visibility.Collapsed;
        Choices.Visibility = Visibility.Collapsed;
        Choices.ItemsSource = null;
        CancelButton.Visibility = Visibility.Collapsed;
    }

    /// <summary>Shows the window without activating it, centred near the bottom of the main screen's work area.</summary>
    private void Present()
    {
        if (!IsVisible)
        {
            Show();
        }

        UpdateLayout();
        var area = SystemParameters.WorkArea;
        Left = area.Left + Math.Max(0, (area.Width - ActualWidth) / 2);
        Top = area.Top + Math.Max(0, area.Height - ActualHeight - 80);
    }

    private void Dismiss()
    {
        var cancel = _cancel;
        Reset();
        Hide();
        cancel?.Invoke();
    }

    private void Choice_Click(object sender, RoutedEventArgs e)
    {
        var picked = (sender as FrameworkElement)?.Tag as VoiceTarget;
        var choice = _choice;
        _choice = null;
        Reset();
        Hide();
        choice?.TrySetResult(picked);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Dismiss();

    /// <summary>The title bar's close button hides the window, as Cancel does; it is closed only with the app.</summary>
    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!_closing)
        {
            e.Cancel = true;
            Dismiss();
        }
    }
}
