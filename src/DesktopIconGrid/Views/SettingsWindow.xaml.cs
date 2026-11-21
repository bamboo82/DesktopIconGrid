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
        DataContextChanged += (_, _) =>
        {
            if (ViewModel is { } viewModel)
            {
                viewModel.ConfirmInstall ??= AskToInstallFont;
            }
        };
        Closed += (_, _) => ViewModel?.CancelInstall(); // Cancel, and Save too: a download does not outlive the window
    }

    /// <summary>
    /// The question before the font is downloaded (0.12.0 WP5): the app's own confirmation window, whose default is
    /// Cancel, with where it comes from, what it must be, and where it goes.
    /// </summary>
    private bool AskToInstallFont()
    {
        var source = Core.Fonts.TaipeiSans.Light;
        var details = string.Format(
            System.Globalization.CultureInfo.CurrentCulture,
            Strings.FontInstall_Details,
            source.Url,
            source.FileName,
            source.Size,
            source.Sha256,
            ChineseFontService.UserFontsDirectory);
        return new CommandApprovalWindow(Strings.FontInstall_Title, Strings.FontInstall_Text, details, Strings.FontInstall_Install, Strings.FontInstall_DetailsName) { Owner = this }.ShowDialog() == true;
    }

    /// <summary>The author's page, where the font can be installed by hand.</summary>
    private void ManualInstall_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel)
        {
            Core.Launching.ProcessLauncher.Start(new System.Diagnostics.ProcessStartInfo(viewModel.ManualInstallPage.AbsoluteUri) { UseShellExecute = true });
        }
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

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } checking && !checking.TryCheckReceiveFolder())
        {
            Tabs.SelectedIndex = PageList.ToList().IndexOf(SendToPage); // the receive folder cannot be used: the reason is beside it
            return;
        }

        if (ViewModel is { } viewModel && !PlanDataMove(viewModel))
        {
            return; // a folder that cannot be used (said under the choice), or Cancel: the window stays open
        }

        DialogResult = true;
    }

    /// <summary>The receive folder from the folder picker (0.14.0 WP5); checked at Save.</summary>
    private void BrowseReceiveFolder_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = Strings.Settings_ReceiveFolderPickTitle,
            Multiselect = false,
            InitialDirectory = DialogService.LocalFolderToOpen(Core.Sync.ReceiveFolder.Expand(viewModel.ReceiveFolderText)),
        };
        if (dialog.ShowDialog(this) == true && !string.IsNullOrWhiteSpace(dialog.FolderName))
        {
            viewModel.ReceiveFolderText = dialog.FolderName;
        }
    }

    private void ResetReceiveFolder_Click(object sender, RoutedEventArgs e) => ViewModel?.ResetReceiveFolder();

    private void OpenReceiveFolder_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel && Core.Sync.ReceiveFolder.Expand(viewModel.ReceiveFolderText) is { } folder && System.IO.Directory.Exists(folder))
        {
            Core.Launching.ProcessLauncher.Start(new System.Diagnostics.ProcessStartInfo(folder) { UseShellExecute = true }).Process?.Dispose();
        }
    }

    /// <summary>The Sync window over this one; then the tab says how sync is set up now (0.14.0 WP5).</summary>
    private void OpenSync_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { OpenSync: { } open } viewModel && open() is { } now)
        {
            viewModel.RefreshFromSync(now);
        }
    }

    /// <summary>
    /// A new place for the data (0.13.0 WP1): checked (it can be made and written), then, when there is a bar to copy and
    /// the folder is another, the one question: copy the data there (Copy), only change the place (Don't copy, the
    /// default), or Cancel, which changes nothing and leaves the dialog open. The bar carries it out after the dialog.
    /// </summary>
    private bool PlanDataMove(SettingsViewModel viewModel)
    {
        if (!viewModel.TryPlanDataMove(Core.Storage.DataLocation.CanUse, out var move))
        {
            Tabs.SelectedIndex = PageList.ToList().IndexOf(PortablePage); // where the reason is said
            return false;
        }

        if (move is null || !viewModel.OffersCopy(move))
        {
            viewModel.PlannedMove = move;
            return true;
        }

        var answer = ChoiceWindow.Ask(
            this,
            Strings.Data_Title,
            string.Format(Strings.Data_CopyQuestion, viewModel.DataNow, move.Folder),
            [Strings.Data_Copy, Strings.Data_DontCopy, Strings.Common_Cancel],
            defaultButton: 1,
            cancelButton: 2);
        if (answer == 2)
        {
            return false;
        }

        viewModel.PlannedMove = move with { Copy = answer == 0 };
        return true;
    }

    /// <summary>A folder for the data; one under the program's folder is written as it is from there (<c>.</c>, <c>data</c>).</summary>
    private void BrowseData_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = Strings.Settings_DataPickTitle,
            Multiselect = false,
            InitialDirectory = DialogService.LocalFolderToOpen(viewModel.DataFolderFull),
        };
        if (dialog.ShowDialog(this) == true && !string.IsNullOrWhiteSpace(dialog.FolderName))
        {
            viewModel.DataFolder = Core.Storage.DataLocation.AsTyped(dialog.FolderName, viewModel.ProgramDirectory);
        }
    }

    /// <summary>The data folder in use now, in File Explorer.</summary>
    private void OpenDataFolder_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { DataNow.Length: > 0 } viewModel)
        {
            Core.Launching.ProcessLauncher.Start(new System.Diagnostics.ProcessStartInfo(viewModel.DataNow) { UseShellExecute = true }).Process?.Dispose();
        }
    }

    /// <summary>
    /// This PC's bar into the portable copy (0.13.0 WP1), asked as at the start; Import closes the dialog (nothing else of it
    /// is kept: the bar is replaced) and the bar restarts to bring it in.
    /// </summary>
    private void ImportThisPc_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        var answer = ChoiceWindow.Ask(
            this,
            Strings.Data_Title,
            string.Format(Strings.Data_ImportQuestion, viewModel.ThisPcDirectory),
            [Strings.Data_Import, Strings.Common_Cancel],
            defaultButton: 1,
            cancelButton: 1);
        if (answer == 0)
        {
            viewModel.ImportThisPcRequested = true;
            DialogResult = true;
        }
    }

    private void ClearHotkey_Click(object sender, RoutedEventArgs e) => SetHotkey(HotkeyBox, string.Empty);

    private void ClearPointerHotkey_Click(object sender, RoutedEventArgs e) => SetHotkey(PointerHotkeyBox, string.Empty);

    private void ClearHoldHotkey_Click(object sender, RoutedEventArgs e) => SetHotkey(HoldHotkeyBox, string.Empty);

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
        else if (ReferenceEquals(box, HoldHotkeyBox))
        {
            viewModel.HoldAtPointerHotkey = text;
        }
        else
        {
            viewModel.ToggleHotkey = text;
        }

        WarningOf(box).Visibility = Visibility.Collapsed;
    }

    private TextBlock WarningOf(TextBox box) =>
        ReferenceEquals(box, VoiceHotkeyBox) ? VoiceHotkeyWarning
        : ReferenceEquals(box, PointerHotkeyBox) ? PointerHotkeyHint
        : ReferenceEquals(box, HoldHotkeyBox) ? HoldHotkeyHint
        : HotkeyHint;

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
