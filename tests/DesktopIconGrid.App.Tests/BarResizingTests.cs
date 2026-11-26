using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Shell;
using MenuItem = System.Windows.Controls.MenuItem;
using DesktopIconGrid.Core;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Resources;
using DesktopIconGrid.Services;
using DesktopIconGrid.Views;

namespace DesktopIconGrid.App.Tests;

/// <summary>
/// 0.16.1 WP1: while 鎖定格線 is on the bar cannot be resized by hand, docked or not. <see cref="BarResizing"/> is the one
/// place that decides; the bar's window follows it from the start, from either menu and from a sync.
/// </summary>
[Collection(WpfWindowsCollection.Name)]
public sealed class BarResizingTests(WpfThread wpf) : IDisposable
{
    private const int WmNcHitTest = 0x0084;
    private const int HtClient = 1;
    private const int HtLeft = 10;
    private const int HtRight = 11;
    private const int HtTop = 12;
    private const int HtTopLeft = 13;
    private const int HtBottomRight = 17;
    private const long WsMaximizeBox = 0x00010000;
    private const long WsMinimizeBox = 0x00020000;

    private readonly string _directory = Directory.CreateTempSubdirectory("dig-resize-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private static LauncherModel One()
    {
        var model = new LauncherModel();
        model.Add(new ShortcutItem { Name = "Editor", Location = @"C:\Tools\editor.exe" });
        return model;
    }

    /// <summary>A window with the bar's frame: no border drawn, no caption, the resize border at its edges.</summary>
    private static Window BarLike()
    {
        var window = new Window
        {
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.CanResize,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = 120,
            Top = 120,
            Width = 320,
            Height = 120,
        };
        WindowChrome.SetWindowChrome(window, new WindowChrome
        {
            CaptionHeight = 0,
            ResizeBorderThickness = new Thickness(BarResizing.ResizeBorder),
            GlassFrameThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false,
        });
        return window;
    }

    /// <summary>What the window answers, as Windows asks it, at the left edge, the top edge, the top-left corner, the right edge and the bottom-right corner.</summary>
    private static int[] Probe(IntPtr handle)
    {
        Assert.True(NativeMethods.GetWindowRect(handle, out var bounds));
        var middleX = (bounds.Left + bounds.Right) / 2;
        var middleY = (bounds.Top + bounds.Bottom) / 2;
        (int X, int Y)[] points =
        [
            (bounds.Left + 2, middleY),
            (middleX, bounds.Top + 2),
            (bounds.Left + 2, bounds.Top + 2),
            (bounds.Right - 3, middleY),
            (bounds.Right - 3, bounds.Bottom - 3),
        ];
        return points.Select(point => (int)SendMessage(handle, WmNcHitTest, IntPtr.Zero, new IntPtr((point.Y << 16) | (point.X & 0xFFFF)))).ToArray();
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    public void Only_an_unlocked_bar_that_is_not_auto_hidden_is_resized_by_hand(bool locked, bool autoHidden, bool resizable) =>
        Assert.Equal(resizable, BarResizing.CanResize(locked, autoHidden));

    [Theory]
    [InlineData(false, false, false, true, ResizeMode.CanResize)]
    [InlineData(false, false, true, true, ResizeMode.CanResize)] // docked, at its inner edge
    [InlineData(false, false, false, false, ResizeMode.CanResize)]
    [InlineData(true, false, false, true, ResizeMode.CanMinimize)] // floating, locked, on the taskbar (0.17.0 WP1)
    [InlineData(true, false, false, false, ResizeMode.NoResize)] // off the taskbar, as in 0.16.1
    [InlineData(true, false, true, true, ResizeMode.NoResize)] // docked is not minimised
    [InlineData(false, true, true, true, ResizeMode.NoResize)] // auto-hidden
    [InlineData(true, true, true, false, ResizeMode.NoResize)]
    public void A_locked_bar_floating_on_the_taskbar_keeps_only_its_minimise_box(bool locked, bool autoHidden, bool docked, bool onTaskbar, ResizeMode mode) =>
        Assert.Equal(mode, BarResizing.ModeFor(locked, autoHidden, docked, onTaskbar));

    [Fact]
    public void Locked_the_edges_and_corners_are_the_bars_own_and_unlocked_they_resize_again()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        wpf.Invoke(() =>
        {
            using var bar = new TestBar(_directory, One());
            var window = BarLike();
            var surface = new BarSurface(window, BarResizing.ResizeBorder, "Test bar");
            var resizing = new BarResizing(window, surface, bar.ViewModel, () => false, () => false);
            resizing.Apply();
            window.Show();
            try
            {
                var handle = new WindowInteropHelper(window).Handle;
                int[] resizeCodes = [HtLeft, HtTop, HtTopLeft, HtRight, HtBottomRight];
                Assert.Equal(ResizeMode.CanResize, window.ResizeMode);
                Assert.Equal(resizeCodes, Probe(handle));
                Assert.NotEqual(0L, NativeMethods.GetStyle(handle) & NativeMethods.WS_THICKFRAME);

                bar.ViewModel.LockGrid = true; // from either menu; on the taskbar, the default since 0.17.0

                Assert.Equal(ResizeMode.CanMinimize, window.ResizeMode);
                Assert.Equal(new Thickness(0), WindowChrome.GetWindowChrome(window).ResizeBorderThickness);
                Assert.Equal([HtClient, HtClient, HtClient, HtClient, HtClient], Probe(handle)); // no resize cursor, no drag
                var style = NativeMethods.GetStyle(handle);
                Assert.Equal(0L, style & NativeMethods.WS_THICKFRAME); // Size greyed in the system menu, no snap
                Assert.Equal(0L, style & WsMaximizeBox);
                Assert.NotEqual(0L, style & WsMinimizeBox); // its taskbar button can minimise it (0.17.0 WP1)

                var settings = bar.Model.Settings.Clone();
                settings.HideFromTaskbar = true; // off the taskbar, as in 0.16.1: a save in Settings
                bar.ViewModel.ApplySettings(settings);

                Assert.Equal(ResizeMode.NoResize, window.ResizeMode);
                Assert.Equal([HtClient, HtClient, HtClient, HtClient, HtClient], Probe(handle));
                style = NativeMethods.GetStyle(handle);
                Assert.Equal(0L, style & NativeMethods.WS_THICKFRAME);
                Assert.Equal(0L, style & WsMaximizeBox);
                Assert.Equal(0L, style & WsMinimizeBox);

                bar.ViewModel.LockGrid = false;

                Assert.Equal(ResizeMode.CanResize, window.ResizeMode);
                Assert.Equal(resizeCodes, Probe(handle));
                style = NativeMethods.GetStyle(handle);
                Assert.NotEqual(0L, style & NativeMethods.WS_THICKFRAME);
                Assert.NotEqual(0L, style & WsMaximizeBox);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void Locked_the_bar_stays_unresizable_docked_with_auto_hide_and_undocked_again()
    {
        wpf.Invoke(() =>
        {
            using var bar = new TestBar(_directory, One());
            var autoHidden = false;
            var window = BarLike();
            var resizing = new BarResizing(window, new BarSurface(window, BarResizing.ResizeBorder, "Test bar"), bar.ViewModel, () => autoHidden, () => autoHidden);
            bar.ViewModel.LockGrid = true;
            Assert.Equal(ResizeMode.CanMinimize, window.ResizeMode); // floating on the taskbar: only its minimise box (0.17.0)

            autoHidden = true; // docked with auto-hide: what docking asks for
            resizing.Apply();
            Assert.Equal(ResizeMode.NoResize, window.ResizeMode); // a docked bar is not minimised

            autoHidden = false; // undocked: what Undock asks for
            resizing.Apply();
            Assert.Equal(ResizeMode.CanMinimize, window.ResizeMode); // still locked: docking does not turn resizing back on
            Assert.Equal(new Thickness(0), WindowChrome.GetWindowChrome(window).ResizeBorderThickness);
            Assert.False(resizing.CanResizeNow);

            bar.ViewModel.LockGrid = false;
            Assert.Equal(ResizeMode.CanResize, window.ResizeMode);
            Assert.Equal(new Thickness(BarResizing.ResizeBorder), WindowChrome.GetWindowChrome(window).ResizeBorderThickness);

            autoHidden = true;
            resizing.Apply();
            Assert.Equal(ResizeMode.NoResize, window.ResizeMode); // auto-hide alone, as before 0.16.1
        });
    }

    [Fact]
    public void The_bars_window_follows_the_lock_from_the_start_from_a_menu_and_from_a_sync()
    {
        wpf.Invoke(() =>
        {
            var model = One();
            model.Settings.LockGrid = true;
            using var bar = new TestBar(_directory, model);
            var window = new MainWindow(bar.ViewModel); // never shown: no handle, hotkeys or tray icon

            Assert.Equal(ResizeMode.CanMinimize, window.ResizeMode); // locked from the start: no sizing frame when it is made
            Assert.Equal(new Thickness(0), WindowChrome.GetWindowChrome(window).ResizeBorderThickness);
            Assert.False(window.CanResizeByHand);

            bar.ViewModel.LockGrid = false; // either menu
            Assert.Equal(ResizeMode.CanResize, window.ResizeMode);
            Assert.Equal(new Thickness(BarResizing.ResizeBorder), WindowChrome.GetWindowChrome(window).ResizeBorderThickness);

            var layout = bar.ViewModel.ExportLayout();
            layout.Settings.LockGrid = true; // another PC locked it and keeps the bar off the taskbar, and its copy comes in
            layout.Settings.HideFromTaskbar = true;
            bar.ViewModel.ReplaceLayout(layout);
            Assert.Equal(ResizeMode.NoResize, window.ResizeMode);
            Assert.Equal(new Thickness(0), WindowChrome.GetWindowChrome(window).ResizeBorderThickness);
            Assert.False(window.ShowInTaskbar);

            var item = window.OptionsButton.ContextMenu.Items.OfType<MenuItem>().Single(entry => Equals(entry.Header, Strings.Menu_LockGrid));
            Assert.Equal(Strings.Menu_LockGridToolTip, item.ToolTip);
        });
    }

    [Fact]
    public void The_empty_areas_lock_item_has_the_tooltip()
    {
        wpf.Invoke(() =>
        {
            using var bar = new TestBar(_directory, One());
            var grid = new IconGridControl { DataContext = bar.ViewModel.RootItems };
            var item = grid.Scroller.ContextMenu.Items.OfType<MenuItem>().Single(entry => Equals(entry.Header, Strings.Menu_LockGrid));
            Assert.Equal(Strings.Menu_LockGridToolTip, item.ToolTip);
        });
    }

    [Theory]
    [InlineData("en", "also keeps the bar's size")]
    [InlineData("zh-Hant", "也會固定工具列大小")]
    [InlineData("zh-Hans", "也会固定工具栏大小")]
    public void The_tooltip_and_the_notice_say_the_lock_keeps_the_size(string language, string words)
    {
        var culture = CultureInfo.GetCultureInfo(language);
        Assert.Contains(words, Strings.ResourceManager.GetString(nameof(Strings.Menu_LockGridToolTip), culture));
        Assert.Contains(words, Strings.ResourceManager.GetString(nameof(Strings.Main_GridLocked), culture));
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
}
