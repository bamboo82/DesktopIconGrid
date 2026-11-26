using System.ComponentModel;
using System.Windows;
using DesktopIconGrid.ViewModels;

namespace DesktopIconGrid.Views;

/// <summary>
/// Whether the bar can be resized by hand, decided here alone (0.16.1 WP1). Not while its grid is locked (鎖定格線,
/// <see cref="LauncherViewModel.LockGrid"/>), docked or not, nor while it is docked with auto-hide, which brings it out at
/// its thickness and tucks it back; else it can, free at every edge and docked at its inner one (the new thickness).
/// When it cannot, the window has no sizing frame, so Windows greys Size and Maximize in its system menu and snaps it to
/// no half or whole screen, and no resize border, so its edge is the bar's own: no resize cursor there, and a press does
/// what a press on what is there does (on the grid's empty part or the grip, the bar moves). Minimize goes with them,
/// except for a floating bar on the taskbar (0.17.0 WP1): it keeps its minimise box, so its taskbar button can minimise
/// it. The program's own changes of size go on: icon size, spacing, text size, names shown, ▸, docking, the saved size.
/// </summary>
internal sealed class BarResizing
{
    /// <summary>How far in from its edges the bar can be resized when it can, in units.</summary>
    public const double ResizeBorder = 6;

    private readonly Window _window;
    private readonly BarSurface _surface;
    private readonly LauncherViewModel _viewModel;
    private readonly Func<bool> _autoHidden;
    private readonly Func<bool> _docked;

    /// <param name="window">The bar's window.</param>
    /// <param name="surface">The window's background, which owns its frame.</param>
    /// <param name="viewModel">The bar; its lock and its place on the taskbar are followed from the menus, Settings and a sync.</param>
    /// <param name="autoHidden">Whether the bar is docked with auto-hide just now.</param>
    /// <param name="docked">Whether the bar is docked at a screen edge just now.</param>
    public BarResizing(Window window, BarSurface surface, LauncherViewModel viewModel, Func<bool> autoHidden, Func<bool> docked)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(autoHidden);
        ArgumentNullException.ThrowIfNull(docked);
        _window = window;
        _surface = surface;
        _viewModel = viewModel;
        _autoHidden = autoHidden;
        _docked = docked;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    /// <summary>The rule: by hand only while the grid is not <paramref name="locked"/> and the bar is not docked with auto-hide.</summary>
    public static bool CanResize(bool locked, bool autoHidden) => !locked && !autoHidden;

    /// <summary>
    /// The window's mode: <see cref="ResizeMode.CanResize"/> while it can be resized by hand; else, floating on the
    /// taskbar (0.17.0 WP1), <see cref="ResizeMode.CanMinimize"/>, still with no sizing frame, maximise box or resize
    /// border, so that its taskbar button can minimise it; else <see cref="ResizeMode.NoResize"/>, as before. A docked bar
    /// is not minimised, so it needs no minimise box.
    /// </summary>
    public static ResizeMode ModeFor(bool locked, bool autoHidden, bool docked, bool onTaskbar) =>
        CanResize(locked, autoHidden) ? ResizeMode.CanResize
        : onTaskbar && !docked ? ResizeMode.CanMinimize
        : ResizeMode.NoResize;

    /// <summary>Whether the bar can be resized by hand just now.</summary>
    public bool CanResizeNow => CanResize(_viewModel.LockGrid, _autoHidden());

    /// <summary>The window's mode just now (<see cref="ModeFor"/>).</summary>
    public ResizeMode ModeNow => ModeFor(_viewModel.LockGrid, _autoHidden(), _docked(), !_viewModel.HideFromTaskbar);

    /// <summary>
    /// Brings the window to the rule. Docking calls it whenever it docks, undocks or turns auto-hide; the lock's changes
    /// and the taskbar setting's call it here.
    /// </summary>
    public void Apply()
    {
        var mode = ModeNow;
        _window.ResizeMode = mode;
        _surface.ResizeBorder = mode == ResizeMode.CanResize ? ResizeBorder : 0;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName)
            || e.PropertyName is nameof(LauncherViewModel.LockGrid) or nameof(LauncherViewModel.HideFromTaskbar))
        {
            Apply();
        }
    }
}
