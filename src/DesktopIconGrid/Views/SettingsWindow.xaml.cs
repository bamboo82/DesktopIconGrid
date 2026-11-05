using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DesktopIconGrid.Resources;
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

    private void ClearHotkey_Click(object sender, RoutedEventArgs e) => SetHotkey(HotkeyBox, string.Empty);

    private void ClearPointerHotkey_Click(object sender, RoutedEventArgs e) => SetHotkey(PointerHotkeyBox, string.Empty);

    /// <summary>Stores a captured combination in the hotkey that <paramref name="box"/> shows, and hides that box's warning.</summary>
    private void SetHotkey(TextBox box, string text)
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        if (ReferenceEquals(box, PointerHotkeyBox))
        {
            viewModel.ShowAtPointerHotkey = text;
        }
        else
        {
            viewModel.ToggleHotkey = text;
        }

        WarningOf(box).Visibility = Visibility.Collapsed;
    }

    private TextBlock WarningOf(TextBox box) => ReferenceEquals(box, PointerHotkeyBox) ? PointerHotkeyHint : HotkeyHint;

    /// <summary>Captures the pressed combination instead of typing into the box (either hotkey box).</summary>
    private void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox box)
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
            SetHotkey(box, string.Empty);
            return;
        }

        var gesture = new HotkeyGesture(Keyboard.Modifiers, key);
        if (!gesture.HasUsableModifier)
        {
            var warning = WarningOf(box);
            warning.Text = Strings.Settings_HotkeyNeedsModifier;
            warning.Visibility = Visibility.Visible;
            return;
        }

        SetHotkey(box, gesture.ToString());
    }
}
