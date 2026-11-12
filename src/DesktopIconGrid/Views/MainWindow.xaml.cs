using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Shell;
using System.Windows.Threading;
using DesktopIconGrid.Core.Commands;
using DesktopIconGrid.Core.Diagnostics;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Resources;
using DesktopIconGrid.Services;
using DesktopIconGrid.ViewModels;
using Microsoft.Win32;

namespace DesktopIconGrid.Views;

/// <summary>The launcher bar: a borderless window with a move grip, the icon grid, close/options buttons and the group popup.</summary>
public partial class MainWindow : Window
{
    private readonly LauncherViewModel _viewModel;
    private GlobalHotkey? _hotkey;
    private GlobalHotkey? _pointerHotkey;
    private HotkeySet? _hotkeys;
    private PointerWindow? _pointerWindow;
    private TrayIcon? _tray;
    private TrayMenuHost? _trayMenu;
    private bool _exiting;
    private bool _closing;
    private bool _backdropActive;

    public MainWindow(LauncherViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();

        viewModel.GroupActivated += OnGroupActivated;
        viewModel.ItemActivated += (_, _) => CloseSearch();
        viewModel.ExitRequested += (_, _) => ExitApplication();
        viewModel.SettingsChanged += (_, _) =>
        {
            EnsureMinimumSize();
            ApplyHotkeys(reportErrors: true);
            ApplyAppearance();
            ApplyDocking();
        };
        Deactivated += OnDeactivated;
        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
        MainGrid.MouseLeftButtonDown += GridBackground_MouseLeftButtonDown;
        InitializeDocking();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ApplyPlacement();

        HideFromAltTab();
        if (HwndSource.FromHwnd(new WindowInteropHelper(this).Handle) is { } source)
        {
            _hotkey = new GlobalHotkey(source);
            _hotkey.Pressed += (_, _) => ToggleVisibility();
            _pointerHotkey = new GlobalHotkey(source, GlobalHotkey.PointerIds);
            _pointerHotkey.Pressed += (_, _) => TogglePointerWindow();
            _hotkeys = new HotkeySet((_hotkey, "Show/hide hotkey"), (_pointerHotkey, "Show-at-pointer hotkey"));
            ApplyHotkeys(reportErrors: true);
        }

        // Made ahead, while nothing waits for it: a new window's bindings come alive only once the dispatcher has run,
        // and the pointer window is measured from them the moment its hotkey shows it.
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => EnsurePointerWindow());

        ApplyAppearance();
        ApplyDocking();
        CreateTrayIcon();
    }

    private void CreateTrayIcon()
    {
        if (TryFindResource("AppIconImage") is not ImageSource image)
        {
            return;
        }

        try
        {
            _tray = new TrayIcon(image, Strings.App_Name);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return;
        }

        _tray.Clicked += (_, _) => ToggleVisibility();
        _tray.MenuRequested += (_, _) => (_trayMenu ??= new TrayMenuHost()).Open(BuildTrayMenu());
        _tray.BalloonClicked += (_, _) => _viewModel.OpenLogViewerCommand.Execute(null);
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        AppLog.Default.Debug("App", "Tray icon created");
        TrayReady?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Raised once the tray icon exists, so notices can go there instead of message boxes.</summary>
    public event EventHandler? TrayReady;

    /// <summary>Shows a balloon from the tray icon; false when there is none.</summary>
    public bool TryNotify(string title, string message)
    {
        if (_tray is null)
        {
            return false;
        }

        _tray.Notify(title, message);
        return true;
    }

    /// <summary>The display settings changed (scale, monitors): the tray icon is re-rendered for the system DPI.</summary>
    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(() => _tray?.Refresh());

    private IEnumerable<TrayMenuEntry> BuildTrayMenu()
    {
        var shown = IsVisible && WindowState != WindowState.Minimized;
        yield return new TrayMenuEntry(shown ? Strings.Menu_HideBar : Strings.Menu_ShowBar, shown ? HideBar : ShowBar);
        yield return new TrayMenuEntry(Strings.Menu_ResetBarPosition, ResetPosition);
        yield return TrayMenuEntry.Separator;
        yield return new TrayMenuEntry(Strings.Menu_NewShortcut, () => RunOnBar(() => _viewModel.NewShortcutCommand.Execute(null)));
        yield return new TrayMenuEntry(Strings.Menu_NewGroup, () => RunOnBar(() => _viewModel.NewGroupCommand.Execute(null)));
        yield return new TrayMenuEntry(Strings.Menu_EditNames, () => RunOnBar(() => _viewModel.OpenNameTableCommand.Execute(null)));
        yield return new TrayMenuEntry(Strings.Menu_LaunchMultiLaunch, () => _viewModel.LaunchMultiLaunchCommand.Execute(null), IsEnabled: _viewModel.HasMultiLaunchMembers);
        foreach (var workspace in _viewModel.WorkspaceNames)
        {
            var name = workspace;
            if (!Core.Launching.Workspaces.Same(name, Core.Launching.Workspaces.MultiLaunch))
            {
                yield return new TrayMenuEntry(string.Format(Strings.Workspace_Start, name), () => _ = _viewModel.StartWorkspaceAsync(name));
            }

            yield return new TrayMenuEntry(string.Format(Strings.Workspace_Capture, LauncherViewModel.WorkspaceLabel(name)), () => _viewModel.CaptureWorkspace(name));
        }

        yield return new TrayMenuEntry(Strings.Menu_AlwaysOnTop, () => _viewModel.AlwaysOnTop = !_viewModel.AlwaysOnTop, IsChecked: _viewModel.AlwaysOnTop);
        yield return new TrayMenuEntry(Strings.Menu_SendTo, ToggleSendTo, IsChecked: SendToEntry.Exists);
        if (Inbox is { CanReceive: true } inbox)
        {
            yield return new TrayMenuEntry(Strings.Menu_ReceiveFiles, () => inbox.ReceiveSetting = !inbox.ReceiveSetting, IsChecked: inbox.ReceiveSetting);
        }
        yield return TrayMenuEntry.Separator;
        yield return new TrayMenuEntry(Strings.Menu_Settings, () => RunOnBar(() => _viewModel.OpenSettingsCommand.Execute(null)));
        yield return new TrayMenuEntry(Strings.Menu_Sync, () => RunOnBar(() => _viewModel.OpenSyncCommand.Execute(null)));
        yield return new TrayMenuEntry(Strings.Menu_LogViewer, () => _viewModel.OpenLogViewerCommand.Execute(null));
        yield return new TrayMenuEntry(Strings.Menu_RestoreLayout, () => RunOnBar(() => _viewModel.RestoreLayoutCommand.Execute(null)));
        yield return new TrayMenuEntry(Strings.Menu_OpenLayoutLocation, () => _viewModel.OpenLayoutFolderCommand.Execute(null));
        yield return new TrayMenuEntry(Strings.Menu_About, () => _viewModel.AboutCommand.Execute(null));
        yield return TrayMenuEntry.Separator;
        yield return new TrayMenuEntry(Strings.Common_Exit, ExitApplication);
    }

    /// <summary>Brings the bar to the front before running an action that opens a dialog owned by it.</summary>
    private void RunOnBar(Action action)
    {
        if (!IsVisible)
        {
            ToggleVisibility();
        }

        Dispatcher.BeginInvoke(action);
    }

    /// <summary>Moves the bar to the middle of the primary screen's work area (for when it got lost on a disconnected monitor).</summary>
    public void ResetPosition()
    {
        AppLog.Default.Info("App", "Bar position reset to the primary screen");
        Show();
        WindowState = WindowState.Normal;
        var area = SystemParameters.WorkArea;
        Width = Math.Min(Width, area.Width);
        Height = Math.Min(Height, area.Height);
        Left = area.Left + Math.Max(0, (area.Width - Width) / 2);
        Top = area.Top + Math.Max(0, (area.Height - Height) / 2);
        Activate();
    }

    public void ExitApplication()
    {
        if (_closing)
        {
            return; // already on the way out (a pending upload is being flushed)
        }

        _exiting = true;
        Close();
    }

    /// <summary>
    /// Theme brushes, the Windows 11 backdrop and the opacity, in that order: the backdrop decides whether the bar
    /// background is a tint and whether the layered alpha applies. Runs once the window has a handle, and again on
    /// every settings change and when Windows High Contrast is switched.
    /// </summary>
    private void ApplyAppearance()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return; // before SourceInitialized; it calls again
        }

        var settings = _viewModel.Model.Settings;
        var palette = ThemeManager.EffectivePalette(_viewModel.Palette);
        var wanted = SystemParameters.HighContrast ? LauncherSettings.BackdropSolid : settings.BarBackdrop; // High Contrast asks for flat colours
        if (!string.Equals(wanted, LauncherSettings.BackdropSolid, StringComparison.Ordinal) && WindowBackdrop.IsSupported)
        {
            WindowAlpha.Apply(handle, LauncherSettings.MaxOpacity); // a layered window cannot take a backdrop: drop the style first
        }

        var active = WindowBackdrop.Apply(handle, wanted, darkMode: ThemeCatalog.Luminance(palette.Background) <= 0.6);
        if (active != _backdropActive)
        {
            _backdropActive = active;

            // For DWM to paint behind the client area WPF must not: extend the frame into it and leave the window transparent.
            WindowChrome.SetWindowChrome(this, new WindowChrome
            {
                CaptionHeight = 0,
                ResizeBorderThickness = new Thickness(6),
                GlassFrameThickness = active ? new Thickness(-1) : new Thickness(0),
                CornerRadius = new CornerRadius(0),
                UseAeroCaptionButtons = false,
            });
            if (active)
            {
                Background = Brushes.Transparent;
                if (HwndSource.FromHwnd(handle) is { CompositionTarget: { } target })
                {
                    target.BackgroundColor = Colors.Transparent;
                }
            }
            else
            {
                ClearValue(BackgroundProperty);
            }

            AppLog.Default.Info("App", active ? $"Bar background: {wanted} backdrop" : "Bar background: solid colour");
        }

        ThemeManager.Apply(palette, translucentBar: active, tintOpacity: _viewModel.Opacity / 100.0);
        DialogTheme.Configure(settings.FluentDialogs);
        ApplyOpacity();
    }

    private void OnSystemParametersChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null or nameof(SystemParameters.HighContrast))
        {
            Dispatcher.BeginInvoke(ApplyAppearance);
        }
    }

    /// <summary>With a backdrop the tint already carries the transparency; the layered alpha would fade the icons as well.</summary>
    private int EffectiveOpacity => _backdropActive ? LauncherSettings.MaxOpacity : _viewModel.Opacity;

    private void ApplyOpacity()
    {
        WindowAlpha.Apply(new WindowInteropHelper(this).Handle, EffectiveOpacity);
        if (GroupPopup.IsOpen && GroupPopup.Child is { } child && PresentationSource.FromVisual(child) is HwndSource popupSource)
        {
            WindowAlpha.Apply(popupSource.Handle, EffectiveOpacity);
        }
    }

    /// <summary>The popup has its own window: give it an Acrylic backdrop when the bar has one, else the solid colour.</summary>
    private void GroupPopup_Opened(object? sender, EventArgs e)
    {
        if (GroupPopup.Child is not { } child || PresentationSource.FromVisual(child) is not HwndSource popupSource)
        {
            return;
        }

        var palette = ThemeManager.EffectivePalette(_viewModel.Palette);
        var translucent = false;
        if (_backdropActive)
        {
            translucent = WindowBackdrop.ApplyToPopup(popupSource.Handle, darkMode: ThemeCatalog.Luminance(palette.Background) <= 0.6);
            if (translucent && popupSource.CompositionTarget is { } target)
            {
                target.BackgroundColor = Colors.Transparent;
            }
        }

        ThemeManager.SetPopupBackground(palette, translucent, _viewModel.Opacity / 100.0);
        WindowAlpha.Apply(popupSource.Handle, EffectiveOpacity);
    }

    protected override void OnClosed(EventArgs e)
    {
        SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        DisposeDocking();
        _hotkeys = null;
        _hotkey?.Dispose();
        _hotkey = null;
        _pointerHotkey?.Dispose();
        _pointerHotkey = null;
        _pointerWindow?.CloseForGood();
        _pointerWindow = null;
        _tray?.Dispose();
        _tray = null;
        _trayMenu?.Close();
        _trayMenu = null;
        base.OnClosed(e);
    }

    /// <summary>A utility bar with a tray icon should not also sit in Alt+Tab.</summary>
    private void HideFromAltTab()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.SetExStyle(handle, NativeMethods.GetExStyle(handle) | NativeMethods.WS_EX_TOOLWINDOW);
    }

    /// <summary>Called when a second copy of the app was started: make the bar visible and bring it forward.</summary>
    /// <summary>
    /// Carries out the command line of a start (this one's, or another start's, handed over by <see cref="CommandPipe"/>),
    /// in order; consecutive <c>--add</c> paths for one group become one addition.
    /// </summary>
    public void Run(IReadOnlyList<BarCommand> commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        // Under a dialog (Restore an earlier layout, the question before a sync load, the messages of a first start) the
        // bar may be about to be replaced, and what was added now would go with it: the commands wait until it closes.
        if (_waitingCommands.Count > 0 || IsUnderDialog())
        {
            _waitingCommands.Enqueue(commands);
            if (_waitingCommandsTimer is null)
            {
                AppLog.Default.Info("Commands", "A dialog is open: the commands wait until it is closed");
                _waitingCommandsTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(400), DispatcherPriority.Background, (_, _) => RunWaitingCommands(), Dispatcher);
            }

            return;
        }

        RunNow(commands);
    }

    private readonly Queue<IReadOnlyList<BarCommand>> _waitingCommands = new();
    private DispatcherTimer? _waitingCommandsTimer;

    /// <summary>A modal window of this program is open (WPF's or a message box, which disables this window).</summary>
    private bool IsUnderDialog() =>
        ComponentDispatcher.IsThreadModal
        || (new WindowInteropHelper(this).Handle is var handle && handle != IntPtr.Zero && !IsWindowEnabled(handle));

    private void RunWaitingCommands()
    {
        if (IsUnderDialog())
        {
            return;
        }

        _waitingCommandsTimer?.Stop();
        _waitingCommandsTimer = null;
        while (_waitingCommands.Count > 0 && !IsUnderDialog())
        {
            RunNow(_waitingCommands.Dequeue()); // a command may open a dialog itself: the rest wait again
        }

        if (_waitingCommands.Count > 0)
        {
            _waitingCommandsTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(400), DispatcherPriority.Background, (_, _) => RunWaitingCommands(), Dispatcher);
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowEnabled(IntPtr window);

    private void RunNow(IReadOnlyList<BarCommand> commands)
    {
        for (var i = 0; i < commands.Count; i++)
        {
            var command = commands[i];
            switch (command.Kind)
            {
                case BarCommandKind.Add:
                    var paths = new List<string>();
                    while (i < commands.Count && commands[i].Kind == BarCommandKind.Add && commands[i].Group == command.Group)
                    {
                        paths.Add(commands[i++].Value!);
                    }

                    i--;
                    _viewModel.AddPathsFromCommandLine(paths, command.Group);
                    break;
                case BarCommandKind.Launch:
                    _viewModel.LaunchByName(command.Value!);
                    break;
                case BarCommandKind.Workspace:
                    _ = _viewModel.StartWorkspaceAsync(command.Value!);
                    break;
                case BarCommandKind.Show:
                    ShowFromOtherInstance();
                    break;
                case BarCommandKind.Hide:
                    HideBar();
                    break;
                case BarCommandKind.Toggle:
                    ToggleVisibility();
                    break;
            }
        }
    }

    public void ShowFromOtherInstance()
    {
        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        // A request from another process carries no foreground rights of its own; the second copy called
        // AllowSetForegroundWindow for us, and BringToFront has the fallback for when Windows still refuses.
        BringToFront();
    }

    /// <summary>
    /// The hotkey and a click on the tray icon: hides the bar when it is in front; otherwise shows it, expands an
    /// auto-hidden strip and brings it to the front. A bar that is visible but covered by other windows comes
    /// forward instead of disappearing, so the first press always does something visible.
    /// </summary>
    public void ToggleVisibility()
    {
        if (IsVisible && WindowState != WindowState.Minimized)
        {
            if (IsAutoHide && _collapsed)
            {
                Expand();
                BringToFront();
                AppLog.Default.Debug("App", "Bar expanded");
                return;
            }

            if (!Topmost && !IsInFront())
            {
                BringToFront();
                AppLog.Default.Debug("App", "Bar brought to the front");
                return;
            }

            HideBar();
            return;
        }

        ShowBar();
    }

    /// <summary>Hides the bar and its popup; the tray icon and the hotkey bring it back.</summary>
    public void HideBar()
    {
        _viewModel.IsGroupPopupOpen = false;
        CloseSearch();
        Hide();
        AppLog.Default.Debug("App", "Bar hidden");
    }

    /// <summary>Shows the bar, expands an auto-hidden strip and brings it to the front.</summary>
    public void ShowBar()
    {
        Show();
        AppLog.Default.Debug("App", "Bar shown");
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        if (IsAutoHide)
        {
            Expand();
        }

        BringToFront();
    }

    /// <summary>True when the bar, or a popup or dialog it owns, is the foreground window.</summary>
    private bool IsInFront()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return false;
        }

        var foreground = NativeMethods.GetForegroundWindow();
        return foreground == handle || NativeMethods.GetAncestor(foreground, NativeMethods.GA_ROOTOWNER) == handle;
    }

    /// <summary>
    /// Brings the bar to the front. A hotkey or a tray click gives this process the right to take the foreground;
    /// when Windows refuses anyway (a request relayed from another process, for example) a flip of Topmost puts
    /// the bar above the current window. SetCurrentValue keeps the Always-on-top binding intact.
    /// </summary>
    private void BringToFront()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero || !NativeMethods.SetForegroundWindow(handle))
        {
            var wasTopmost = Topmost;
            SetCurrentValue(TopmostProperty, true);
            SetCurrentValue(TopmostProperty, wasTopmost);
        }

        Activate();
    }

    /// <summary>
    /// Registers the show/hide and Show-at-pointer hotkeys from the settings, together (see <see cref="HotkeySet"/>:
    /// swapping the two in one save works, and a refusal is shown once).
    /// </summary>
    private void ApplyHotkeys(bool reportErrors)
    {
        if (_hotkeys is null)
        {
            return;
        }

        var settings = _viewModel.Model.Settings;
        var refusals = _hotkeys.Apply([settings.ToggleHotkey, settings.ShowAtPointerHotkey]);
        var toggleError = refusals[0];
        var pointerError = refusals[1];
        if (!reportErrors)
        {
            return;
        }

        if (!string.IsNullOrEmpty(toggleError))
        {
            MessageBox.Show(this, toggleError, Strings.Main_HotkeyTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        if (!string.IsNullOrEmpty(pointerError))
        {
            MessageBox.Show(this, pointerError, Strings.Main_PointerHotkeyTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ----- type to find ------------------------------------------------------------------------------

    /// <summary>
    /// While the bar has the focus, the first key that types a character opens the search box at its bottom and
    /// moves the focus there; the key itself is left alone, so its character (or the IME's composition) lands in
    /// the box. Ctrl+F opens the box as well.
    /// </summary>
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Handled)
        {
            return;
        }

        if (SearchBox.IsKeyboardFocusWithin || Keyboard.FocusedElement is TextBoxBase)
        {
            // In a text box the keys are the box's, but for the three that open something and that no text box uses:
            // with Ctrl or Alt held, and not one of the editing keys (review 21: Ctrl+N, Ctrl+G and Ctrl+, stopped working
            // while the search box had the focus).
            var pressed = e.Key == Key.System ? e.SystemKey : e.Key;
            var held = HotkeyGesture.HeldModifiers();
            if (WindowKeyMap.LeavesTextBoxAlone(pressed, held))
            {
                switch (_viewModel.Keys.ActionFor(pressed, held))
                {
                    case WindowKeyAction.NewShortcut:
                        e.Handled = true;
                        _viewModel.NewShortcutCommand.Execute(null);
                        break;
                    case WindowKeyAction.NewGroup:
                        e.Handled = true;
                        _viewModel.NewGroupCommand.Execute(null);
                        break;
                    case WindowKeyAction.OpenSettings:
                        e.Handled = true;
                        _viewModel.OpenSettingsCommand.Execute(null);
                        break;
                }
            }

            return;
        }

        // The settings' keys (WindowKeyMap); removing and Properties belong to the focused icon (IconGridControl).
        switch (_viewModel.Keys.ActionFor(e.Key == Key.System ? e.SystemKey : e.Key, HotkeyGesture.HeldModifiers()))
        {
            case WindowKeyAction.Find:
                OpenSearch();
                e.Handled = true;
                return;
            case WindowKeyAction.NewShortcut:
                e.Handled = true;
                _viewModel.NewShortcutCommand.Execute(null);
                return;
            case WindowKeyAction.NewGroup:
                e.Handled = true;
                _viewModel.NewGroupCommand.Execute(null);
                return;
            case WindowKeyAction.OpenSettings:
                e.Handled = true;
                _viewModel.OpenSettingsCommand.Execute(null);
                return;
            case WindowKeyAction.UndoRemove:
                e.Handled = true;
                _viewModel.UndoRemoveCommand.Execute(null);
                return;
        }

        if (TypeToFind.IsTextKey(e.Key, Keyboard.Modifiers))
        {
            OpenSearch();
        }
    }

    private void OpenSearch()
    {
        SearchPanel.Visibility = Visibility.Visible;
        SearchPanel.UpdateLayout();
        SearchBox.Focus();
    }

    /// <summary>Ends the bar's search: the box goes, the icons light up again, and the keys go back to the icons.</summary>
    private void CloseSearch()
    {
        if (SearchPanel.Visibility != Visibility.Visible)
        {
            return;
        }

        var hadFocus = SearchBox.IsKeyboardFocusWithin;
        _viewModel.ClearSearch();
        SearchPanel.Visibility = Visibility.Collapsed;
        if (hadFocus && IsActive)
        {
            Focus();
        }
    }

    /// <summary>Arrows move the frame, Enter opens the framed match, Esc clears the text and then closes the box.</summary>
    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            if (_viewModel.IsSearching)
            {
                _viewModel.ClearSearch();
            }
            else
            {
                CloseSearch();
            }

            return;
        }

        e.Handled = TypeToFind.HandleSearchBoxKey(_viewModel, e.Key);
    }

    /// <summary>An empty box closes when the focus leaves it; one with text stays, so a click on a lit icon still works.</summary>
    private void SearchBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (!_viewModel.IsSearching && !SearchPanel.IsKeyboardFocusWithin)
        {
            CloseSearch();
        }
    }

    /// <summary>The Show-at-pointer hotkey: the icons in a small window under the mouse, or away again on a second press.</summary>
    private void TogglePointerWindow()
    {
        if (EnsurePointerWindow() is not { } window)
        {
            return;
        }

        if (window.IsVisible)
        {
            window.Dismiss();
            return;
        }

        _viewModel.IsGroupPopupOpen = false;
        CloseSearch();
        window.ShowAtPointer();
    }

    /// <summary>The Show-at-pointer window, made the first time it is needed; none once the bar is closing.</summary>
    private PointerWindow? EnsurePointerWindow()
    {
        if (_pointerWindow is null && !_closing)
        {
            _pointerWindow = new PointerWindow(_viewModel);
            _pointerWindow.Closed += (sender, _) =>
            {
                if (ReferenceEquals(sender, _pointerWindow))
                {
                    _pointerWindow = null; // closed after all (the app is quitting): the next press makes a new one
                }
            };
        }

        return _pointerWindow;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel)
        {
            return;
        }

        if (_closing)
        {
            e.Cancel = true; // a second Exit while the pending upload is being flushed below
            return;
        }

        if (!_exiting && _viewModel.CloseToTray && _tray is not null)
        {
            e.Cancel = true;
            _viewModel.IsGroupPopupOpen = false;
            SavePlacement();
            Hide();
            return;
        }

        _closing = true;
        IsEnabled = false; // the message loop keeps running in WaitForPendingWork; no more clicks on the bar
        SavePlacement();
        WaitForPendingWork();
    }

    /// <summary>Lets a pending sync upload finish (bounded) while still pumping messages, so "tidy up, then quit" reaches Drive.</summary>
    private void WaitForPendingWork()
    {
        if (_viewModel.FlushBeforeExit is not { } flush)
        {
            return;
        }

        Task task;
        try
        {
            task = flush();
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            return;
        }

        if (task.IsCompleted)
        {
            return;
        }

        var frame = new DispatcherFrame();
        task.ContinueWith(_ => frame.Continue = false, TaskScheduler.Default);
        var guard = new DispatcherTimer(TimeSpan.FromSeconds(6), DispatcherPriority.Normal, (_, _) => frame.Continue = false, Dispatcher);
        Dispatcher.PushFrame(frame);
        guard.Stop();
    }

    private void SavePlacement()
    {
        if (DockedPlacementToSave() is { } free)
        {
            _viewModel.UpdateWindowPlacement(free);
            return;
        }

        var placement = CurrentPlacement();
        if (placement.IsUsable)
        {
            _viewModel.UpdateWindowPlacement(placement);
        }
    }

    /// <summary>
    /// The bar's place now, in WPF units and (for a normal window with a handle) in device pixels relative to the
    /// monitor it is on, named by that monitor's device name.
    /// </summary>
    private WindowPlacement CurrentPlacement()
    {
        var bounds = WindowState == WindowState.Normal
            ? new Rect(Left, Top, ActualWidth > 0 ? ActualWidth : Width, ActualHeight > 0 ? ActualHeight : Height)
            : RestoreBounds;
        var placement = new WindowPlacement { Left = bounds.Left, Top = bounds.Top, Width = bounds.Width, Height = bounds.Height };

        var handle = new WindowInteropHelper(this).Handle;
        if (WindowState == WindowState.Normal
            && handle != IntPtr.Zero
            && ScreenGeometry.MonitorOfWindow(handle) is { } monitor
            && NativeMethods.GetWindowRect(handle, out var rect))
        {
            placement.Monitor = monitor.DeviceName;
            placement.PixelLeft = rect.Left - monitor.Left;
            placement.PixelTop = rect.Top - monitor.Top;
            placement.PixelWidth = rect.Right - rect.Left;
            placement.PixelHeight = rect.Bottom - rect.Top;
        }

        return placement;
    }

    protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseDown(e);
        if (!_viewModel.IsGroupPopupOpen || e.OriginalSource is not DependencyObject source)
        {
            return;
        }

        if (IsInsidePopup(source))
        {
            return;
        }

        // Clicking the open group's own icon toggles it through Activate; everything else closes the popup.
        if (FindAncestor<Button>(source)?.DataContext is ItemViewModel { IsGroup: true } clicked
            && ReferenceEquals(clicked, _viewModel.OpenGroup))
        {
            return;
        }

        _viewModel.IsGroupPopupOpen = false;
    }

    private void ApplyPlacement()
    {
        var placement = _viewModel.WindowPlacement;
        if (placement is { IsUsable: true })
        {
            // Saved with its monitor: go straight back there in device pixels, whatever the scales involved.
            var handle = new WindowInteropHelper(this).Handle;
            if (handle != IntPtr.Zero && placement.ResolvePixels(ScreenGeometry.Monitors()) is { } pixels)
            {
                ScreenGeometry.MoveToPixels(this, pixels);
                AppLog.Default.Debug("App", $"Bar restored to {placement.Monitor} at {pixels.Left},{pixels.Top} ({pixels.Width}×{pixels.Height} px)");
                return;
            }

            // Older file, or the monitor is gone: the WPF-unit values, if they land on a screen.
            if (IntersectsAScreen(placement))
            {
                Left = placement.Left;
                Top = placement.Top;
                Width = placement.Width;
                Height = placement.Height;
                return;
            }
        }

        // First run: room for six icons in two rows, centred in the work area.
        Width = Math.Max(MinWidth, 36 + 6 * _viewModel.CellWidth);
        Height = Math.Max(MinHeight, 8 + 2 * _viewModel.CellHeight);
        var area = SystemParameters.WorkArea;
        Left = area.Left + Math.Max(0, (area.Width - Width) / 2);
        Top = area.Top + Math.Max(0, (area.Height - Height) / 2);
    }

    /// <summary>
    /// Checked against the real monitors, not the virtual screen's bounding box (an L-shaped layout has a dead
    /// corner), and at every plausible display scale, so a mixed-DPI layout never moves a visible bar by mistake.
    /// </summary>
    private static bool IntersectsAScreen(WindowPlacement placement) =>
        ScreenGeometry.IsVisibleAtAnyScale(
            new Rect(placement.Left, placement.Top, placement.Width, placement.Height),
            ScreenGeometry.MonitorsInPixels(),
            ScreenGeometry.PrimaryScale());

    private void EnsureMinimumSize()
    {
        var neededWidth = 36 + _viewModel.CellWidth;
        var neededHeight = 8 + _viewModel.CellHeight;
        if (ActualWidth < neededWidth)
        {
            Width = neededWidth;
        }

        if (ActualHeight < neededHeight)
        {
            Height = neededHeight;
        }
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        _viewModel.EndLaunchFeedback(); // the program that was started has come up (or something else took over)
        if (!DragState.IsDragging)
        {
            _viewModel.IsGroupPopupOpen = false;
            CloseSearch();
        }
    }

    private void OnGroupActivated(object? sender, GroupActivatedEventArgs e)
    {
        GroupPopup.PlacementTarget = e.Anchor as UIElement ?? MainGrid;
        GroupPopup.Placement = PlacementMode.Bottom;
    }

    private void Grip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            _viewModel.AlwaysOnTop = !_viewModel.AlwaysOnTop;
            e.Handled = true;
            return;
        }

        _viewModel.IsGroupPopupOpen = false;
        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // The button was released before the move could start; nothing to do.
        }
    }

    /// <summary>Pressing on empty grid background moves the bar, like the grip does.</summary>
    private void GridBackground_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source
            || FindAncestor<ButtonBase>(source) is not null
            || FindAncestor<ScrollBar>(source) is not null)
        {
            return;
        }

        _viewModel.IsGroupPopupOpen = false;
        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // Button released before the move started.
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.CloseToTray && _tray is not null)
        {
            Close(); // cancelled in OnClosing and turned into Hide()
        }
        else
        {
            ExitApplication();
        }
    }

    private void Options_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { ContextMenu: { } menu } button)
        {
            return;
        }

        SendToMenuItem.IsChecked = SendToEntry.Exists;
        ReceiveFilesMenuItem.IsChecked = Inbox?.ReceiveSetting == true;
        ReceiveFilesMenuItem.IsEnabled = Inbox?.CanReceive == true;
        ReceiveFilesMenuItem.ToolTip = Inbox?.CanReceive == true ? Strings.Menu_ReceiveFilesToolTip : Strings.SendTo_NeedsFolder;
        ToolTipService.SetShowOnDisabled(ReceiveFilesMenuItem, true);
        PopulateWorkspaceEntries(menu);
        menu.PlacementTarget = button;
        menu.Placement = PlacementMode.Top;
        menu.IsOpen = true;
    }

    /// <summary>The options menu's workspace entries (start, and remember window positions), under "Launch Multi Launch group".</summary>
    private void PopulateWorkspaceEntries(ContextMenu menu)
    {
        foreach (var stale in menu.Items.OfType<MenuItem>().Where(entry => Equals(entry.Tag, "workspace")).ToList())
        {
            menu.Items.Remove(stale);
        }

        var index = menu.Items.IndexOf(MultiLaunchMenuItem) + 1;
        foreach (var workspace in _viewModel.WorkspaceNames)
        {
            var name = workspace;
            if (!Core.Launching.Workspaces.Same(name, Core.Launching.Workspaces.MultiLaunch))
            {
                var start = new MenuItem { Header = string.Format(Strings.Workspace_Start, name).Replace("_", "__", StringComparison.Ordinal), Tag = "workspace" };
                start.Click += (_, _) => _ = _viewModel.StartWorkspaceAsync(name);
                menu.Items.Insert(index++, start);
            }

            var capture = new MenuItem { Header = string.Format(Strings.Workspace_Capture, LauncherViewModel.WorkspaceLabel(name)).Replace("_", "__", StringComparison.Ordinal), Tag = "workspace" };
            capture.Click += (_, _) => _viewModel.CaptureWorkspace(name);
            menu.Items.Insert(index++, capture);
        }
    }

    /// <summary>Receiving files from the other PCs of the sync folder; set by the app once the service exists.</summary>
    public InboxService? Inbox { get; init; }

    private void ReceiveFiles_Click(object sender, RoutedEventArgs e)
    {
        if (Inbox is { CanReceive: true } inbox)
        {
            inbox.ReceiveSetting = !inbox.ReceiveSetting;
        }

        ReceiveFilesMenuItem.IsChecked = Inbox?.ReceiveSetting == true;
    }

    private void SendTo_Click(object sender, RoutedEventArgs e) => ToggleSendTo();

    /// <summary>Adds the bar to Explorer's <em>Send to</em> menu, or takes it out again.</summary>
    private void ToggleSendTo()
    {
        // A portable copy leaves nothing on the PC it visits (as with the Run entry): the entry would stay in that
        // user's Send to menu, pointing at a drive that is gone. Taking an old one out is always possible.
        if (!SendToEntry.Exists && DesktopIconGrid.Core.Storage.DataLocation.IsPortable)
        {
            MessageBox.Show(this, Strings.Commands_SendToPortable, Strings.App_Name, MessageBoxButton.OK, MessageBoxImage.Information);
            SendToMenuItem.IsChecked = false;
            return;
        }

        try
        {
            SendToEntry.Set(!SendToEntry.Exists);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // IOException, UnauthorizedAccessException, COMException, and whatever else an HRESULT maps to
            // (ArgumentException, InvalidCastException, NotImplementedException…).
            AppLog.Default.Warning("Commands", $"The Send To entry could not be changed: {ex.Message}");
            MessageBox.Show(this, string.Format(Strings.Commands_SendToFailed, ex.Message), Strings.App_Name, MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        SendToMenuItem.IsChecked = SendToEntry.Exists;
    }

    private bool IsInsidePopup(DependencyObject source)
    {
        var child = GroupPopup.Child;
        return child is not null && (ReferenceEquals(child, source) || IsAncestor(child, source));
    }

    private static DependencyObject? ParentOf(DependencyObject element) =>
        element is Visual or Visual3D ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);

    private static bool IsAncestor(DependencyObject ancestor, DependencyObject element)
    {
        for (var current = ParentOf(element); current is not null; current = ParentOf(current))
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }
        }

        return false;
    }

    private static T? FindAncestor<T>(DependencyObject element)
        where T : DependencyObject
    {
        for (var current = element; current is not null; current = ParentOf(current))
        {
            if (current is T match)
            {
                return match;
            }
        }

        return null;
    }
}
