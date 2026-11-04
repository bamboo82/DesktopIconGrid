using System.Windows;

namespace DesktopIconGrid.Views;

/// <summary>A one-line prompt: title, message, text box, OK/Cancel.</summary>
public partial class InputWindow : Window
{
    public InputWindow()
    {
        InitializeComponent();
        DialogTheme.Apply(this);
    }

    public string Value => ValueBox.Text;

    /// <summary>Shows the prompt and returns the entered text, or <see langword="null"/> when cancelled.</summary>
    public static string? Show(Window? owner, string title, string prompt, string? initialValue = null, string? hint = null)
    {
        var window = new InputWindow { Title = title, Owner = owner };
        window.PromptText.Text = prompt;
        window.ValueBox.Text = initialValue ?? string.Empty;
        window.ValueBox.SelectAll();
        if (!string.IsNullOrEmpty(hint))
        {
            window.HintText.Text = hint;
            window.HintText.Visibility = Visibility.Visible;
        }

        return window.ShowDialog() == true ? window.Value : null;
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
