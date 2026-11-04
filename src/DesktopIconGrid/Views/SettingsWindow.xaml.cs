using System.Windows;
using System.Windows.Input;
using DesktopIconGrid.Services;
using DesktopIconGrid.ViewModels;

namespace DesktopIconGrid.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        DialogTheme.Apply(this);
    }

    private SettingsViewModel? ViewModel => DataContext as SettingsViewModel;

    private void Save_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void ClearHotkey_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel)
        {
            viewModel.ToggleHotkey = string.Empty;
            HotkeyHint.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>Captures the pressed combination instead of typing into the box.</summary>
    private void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Tab)
        {
            return; // keep keyboard navigation working
        }

        e.Handled = true;
        if (HotkeyGesture.IsModifierKey(key))
        {
            return; // wait for the non-modifier key
        }

        if (key == Key.Escape || key == Key.Back || key == Key.Delete)
        {
            viewModel.ToggleHotkey = string.Empty;
            HotkeyHint.Visibility = Visibility.Collapsed;
            return;
        }

        var gesture = new HotkeyGesture(Keyboard.Modifiers, key);
        if (!gesture.HasUsableModifier)
        {
            HotkeyHint.Text = "Combine the key with Ctrl, Alt or Win.";
            HotkeyHint.Visibility = Visibility.Visible;
            return;
        }

        viewModel.ToggleHotkey = gesture.ToString();
        HotkeyHint.Visibility = Visibility.Collapsed;
    }
}
