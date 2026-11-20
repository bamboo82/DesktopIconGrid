using System.Windows;
using System.Windows.Controls;

namespace DesktopIconGrid.Views;

/// <summary>
/// A question with buttons named for what they do (0.13.0 WP1), drawn as the app's other dialogs are (the Fluent look, the
/// Chinese font), which Windows' message box is not. The default button is the one Enter presses and the one that has the
/// focus; closing the window, or Escape, answers with the cancel button, or the default one when there is none.
/// </summary>
public partial class ChoiceWindow : Window
{
    private readonly int _whenClosed;

    public ChoiceWindow(string title, string message, IReadOnlyList<string> buttons, int defaultButton, int? cancelButton)
    {
        ArgumentNullException.ThrowIfNull(buttons);
        ArgumentOutOfRangeException.ThrowIfZero(buttons.Count);
        ArgumentOutOfRangeException.ThrowIfNegative(defaultButton);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(defaultButton, buttons.Count);
        InitializeComponent();
        DialogTheme.Apply(this);
        Title = title;
        MessageText.Text = message;
        _whenClosed = cancelButton ?? defaultButton;
        Choice = _whenClosed;
        for (var index = 0; index < buttons.Count; index++)
        {
            var answer = index;
            var button = new Button
            {
                Content = new TextBlock { Text = buttons[index] }, // a TextBlock: an underscore in a name is shown, not an access key
                Style = (Style)FindResource("DialogButton"),
                IsDefault = index == defaultButton,
                IsCancel = index == cancelButton,
                Margin = new Thickness(8, 4, 0, 0),
            };
            button.Click += (_, _) =>
            {
                Choice = answer;
                DialogResult = true;
            };
            Buttons.Children.Add(button);
            if (index == defaultButton)
            {
                Loaded += (_, _) => button.Focus();
            }
        }
    }

    /// <summary>The button pressed, by its place in the list.</summary>
    public int Choice { get; private set; }

    /// <summary>Asks <paramref name="message"/> and returns the button pressed, by its place in <paramref name="buttons"/>.</summary>
    public static int Ask(Window? owner, string title, string message, IReadOnlyList<string> buttons, int defaultButton, int? cancelButton)
    {
        var window = new ChoiceWindow(title, message, buttons, defaultButton, cancelButton);
        if (owner is { IsLoaded: true })
        {
            window.Owner = owner;
        }
        else
        {
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            window.ShowInTaskbar = true; // with no bar yet (the question at start), it must be found on the taskbar
        }

        return window.ShowDialog() == true ? window.Choice : window._whenClosed;
    }
}
