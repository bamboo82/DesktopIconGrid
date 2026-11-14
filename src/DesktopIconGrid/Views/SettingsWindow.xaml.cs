using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
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
        WorkAreaFit.Attach(this);
    }

    private SettingsViewModel? ViewModel => DataContext as SettingsViewModel;

    /// <summary>The five pages, in the order of the tabs.</summary>
    internal IReadOnlyList<ScrollViewer> PageList => [.. Pages.Children.OfType<ScrollViewer>()];

    /// <summary>The page of the chosen tab, which scrolls when the screen is too short for it.</summary>
    internal ScrollViewer Body => PageList[Math.Max(0, Tabs.SelectedIndex)];

    /// <summary>Shows the chosen tab's page; the others stay in the layout, hidden, so the page area keeps its height.</summary>
    private void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, Tabs))
        {
            return;
        }

        var pages = PageList;
        for (var i = 0; i < pages.Count; i++)
        {
            pages[i].Visibility = i == Tabs.SelectedIndex ? Visibility.Visible : Visibility.Hidden;
        }
    }

    /// <summary>
    /// Ctrl+Tab and Ctrl+Shift+Tab change the tab from anywhere in the window and put the focus on the first control of
    /// the new page. (The arrow keys on the tab strip change it too and leave the focus there, so they can go on.)
    /// </summary>
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Tab || (Keyboard.Modifiers & ModifierKeys.Control) == 0 || (Keyboard.Modifiers & ModifierKeys.Alt) != 0)
        {
            return;
        }

        var count = Tabs.Items.Count;
        var step = (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? count - 1 : 1;
        Tabs.SelectedIndex = (Math.Max(0, Tabs.SelectedIndex) + step) % count;
        e.Handled = true;
        var page = Body;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () => page.MoveFocus(new TraversalRequest(FocusNavigationDirection.First)));
    }

    private void Save_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void ClearHotkey_Click(object sender, RoutedEventArgs e) => SetHotkey(HotkeyBox, string.Empty);

    private void ClearPointerHotkey_Click(object sender, RoutedEventArgs e) => SetHotkey(PointerHotkeyBox, string.Empty);

    private void ClearVoiceHotkey_Click(object sender, RoutedEventArgs e) => SetHotkey(VoiceHotkeyBox, string.Empty);

    /// <summary>Stores a captured combination in the hotkey that <paramref name="box"/> shows, and hides that box's warning.</summary>
    private void SetHotkey(TextBox box, string text)
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        if (viewModel.WindowKeyUsing(text) is { } action)
        {
            var warning = WarningOf(box);
            warning.Text = string.Format(Strings.Settings_HotkeyUsedInBar, text, action);
            warning.Visibility = Visibility.Visible;
            return; // RegisterHotKey would take it from the bar's window everywhere
        }

        if (ReferenceEquals(box, VoiceHotkeyBox))
        {
            viewModel.VoiceHotkey = text;
        }
        else if (ReferenceEquals(box, PointerHotkeyBox))
        {
            viewModel.ShowAtPointerHotkey = text;
        }
        else
        {
            viewModel.ToggleHotkey = text;
        }

        WarningOf(box).Visibility = Visibility.Collapsed;
    }

    private TextBlock WarningOf(TextBox box) =>
        ReferenceEquals(box, VoiceHotkeyBox) ? VoiceHotkeyWarning : ReferenceEquals(box, PointerHotkeyBox) ? PointerHotkeyHint : HotkeyHint;

    /// <summary>
    /// Captures a key of the bar's window in its box: Backspace turns it off, Tab and Escape keep their usual work, and a
    /// combination the window cannot use, or another key already has, is said under the list instead.
    /// </summary>
    private void WindowKeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: WindowKeyRow row } || ViewModel is not { } viewModel)
        {
            return;
        }

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.Tab or Key.Escape || HotkeyGesture.IsModifierKey(key))
        {
            e.Handled = key is not (Key.Tab or Key.Escape);
            return; // keyboard navigation and Cancel keep working; wait for the non-modifier key
        }

        e.Handled = true;
        string? refusal;
        if (key == Key.Back)
        {
            refusal = viewModel.SetWindowKey(row, string.Empty);
        }
        else
        {
            var gesture = new HotkeyGesture(HotkeyGesture.HeldModifiers(), key);
            refusal = gesture.IsAllowedInWindow ? viewModel.SetWindowKey(row, gesture.ToString()) : Strings.Settings_WindowKeyNotAllowed;
        }

        WindowKeyWarning.Text = refusal ?? string.Empty;
        WindowKeyWarning.Visibility = refusal is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void WindowKeyDefault_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: WindowKeyRow row } && ViewModel is { } viewModel)
        {
            var refusal = viewModel.SetWindowKey(row, row.DefaultText);
            WindowKeyWarning.Text = refusal ?? string.Empty;
            WindowKeyWarning.Visibility = refusal is null ? Visibility.Collapsed : Visibility.Visible;
        }
    }

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

        var gesture = new HotkeyGesture(HotkeyGesture.HeldModifiers(), key);
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
