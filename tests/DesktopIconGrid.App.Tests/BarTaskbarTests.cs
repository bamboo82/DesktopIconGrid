using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using DesktopIconGrid.Core;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Services;
using DesktopIconGrid.ViewModels;
using DesktopIconGrid.Views;

namespace DesktopIconGrid.App.Tests;

/// <summary>
/// 0.17.0 WP1: the bar has a taskbar button and an Alt+Tab entry like any program's window, unless 不在工作列與 Alt+Tab
/// 中顯示工具列 is ticked; the setting applies at once, both ways, on a shown bar.
/// </summary>
[Collection(WpfWindowsCollection.Name)]
public sealed class BarTaskbarTests(WpfThread wpf) : IDisposable
{
    private const uint GwOwner = 4;

    private readonly string _directory = Directory.CreateTempSubdirectory("dig-taskbar-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private static LauncherModel One()
    {
        var model = new LauncherModel();
        model.Add(new ShortcutItem { Name = "Editor", Location = @"C:\Tools\editor.exe" });
        return model;
    }

    /// <summary>Lets the bar take its last change and finish what it starts when idle (the window at the pointer, made ahead).</summary>
    private static void Settle(Window window)
    {
        window.UpdateLayout();
        var frame = new DispatcherFrame();
        window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => frame.Continue = false);
        Dispatcher.PushFrame(frame);
    }

    /// <summary>What the taskbar and Alt+Tab go by: ShowInTaskbar, the tool-window style, an owner, and whether Windows shows it.</summary>
    private static (bool ShowInTaskbar, bool ToolWindow, bool Owned, bool Visible) Presence(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        var style = NativeMethods.GetExStyle(handle);
        return (window.ShowInTaskbar, (style & NativeMethods.WS_EX_TOOLWINDOW) != 0, GetWindow(handle, GwOwner) != IntPtr.Zero, IsWindowVisible(handle));
    }

    /// <summary>What saving Settings does with the choice.</summary>
    private static void Choose(TestBar bar, bool hidden)
    {
        var settings = bar.Model.Settings.Clone();
        settings.HideFromTaskbar = hidden;
        bar.ViewModel.ApplySettings(settings);
    }

    [Fact]
    public void The_bar_is_on_the_taskbar_by_default_and_follows_the_setting_both_ways_while_shown()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        wpf.Invoke(() =>
        {
            using var bar = new TestBar(_directory, One());
            var window = new MainWindow(bar.ViewModel);
            try
            {
                window.Show();
                Settle(window);
                var handle = new WindowInteropHelper(window).Handle;
                Assert.Equal((true, false, false, true), Presence(window)); // a taskbar button and an Alt+Tab entry
                Assert.NotEqual(0L, NativeMethods.GetExStyle(handle) & NativeMethods.WS_EX_APPWINDOW);

                Choose(bar, hidden: true);
                Assert.Equal((false, true, true, true), Presence(window)); // off both, as in 0.16.1, and still shown
                Assert.Equal(0L, NativeMethods.GetExStyle(handle) & NativeMethods.WS_EX_APPWINDOW);

                Choose(bar, hidden: false);
                Assert.Equal((true, false, false, true), Presence(window)); // back at once, without a restart
            }
            finally
            {
                Settle(window);
                window.ExitApplication();
            }
        });
    }

    [Fact]
    public void A_bar_made_with_the_setting_on_has_no_button_from_the_first()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        wpf.Invoke(() =>
        {
            var model = One();
            model.Settings.HideFromTaskbar = true;
            using var bar = new TestBar(_directory, model);
            var window = new MainWindow(bar.ViewModel);
            Assert.False(window.ShowInTaskbar); // set before the window is made
            try
            {
                window.Show();
                Settle(window);
                Assert.Equal((false, true, true, true), Presence(window));
            }
            finally
            {
                Settle(window);
                window.ExitApplication();
            }
        });
    }

    [Fact]
    public void A_floating_bar_minimises_to_its_button_saves_its_restored_place_and_starts_normal()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        WindowPlacement? saved = null;
        Rect restored = default;
        wpf.Invoke(() =>
        {
            using var bar = new TestBar(_directory, One());
            var window = new MainWindow(bar.ViewModel);
            try
            {
                window.Show();
                Settle(window);
                window.Left = 160;
                window.Top = 140;
                window.Width = 420;
                window.Height = 150;
                Settle(window);
                restored = new Rect(window.Left, window.Top, window.ActualWidth, window.ActualHeight);

                // What its taskbar button sends for a click while the bar is in front.
                SendMessage(new WindowInteropHelper(window).Handle, NativeMethods.WM_SYSCOMMAND, new IntPtr(NativeMethods.SC_MINIMIZE), IntPtr.Zero);
                Assert.Equal(WindowState.Minimized, window.WindowState);
            }
            finally
            {
                Settle(window);
                window.ExitApplication(); // closed while minimised
            }

            saved = bar.Model.Window;
        });

        Assert.NotNull(saved);
        Assert.Equal(restored.Left, saved!.Left, 1); // the restored place, never the minimised one
        Assert.Equal(restored.Top, saved.Top, 1);
        Assert.Equal(restored.Width, saved.Width, 1);
        Assert.Equal(restored.Height, saved.Height, 1);

        var again = Path.Combine(_directory, "again");
        Directory.CreateDirectory(again);
        wpf.Invoke(() =>
        {
            var model = One();
            model.Window = saved;
            using var bar = new TestBar(again, model);
            var window = new MainWindow(bar.ViewModel);
            try
            {
                window.Show();
                Settle(window);
                Assert.Equal(WindowState.Normal, window.WindowState);
                Assert.Equal(restored.Left, window.Left, 1);
                Assert.Equal(restored.Top, window.Top, 1);
            }
            finally
            {
                Settle(window);
                window.ExitApplication();
            }
        });
    }

    [Theory]
    [InlineData(0x0112, 0xF020, true, true)] // SC_MINIMIZE while docked: ignored
    [InlineData(0x0112, 0xF022, true, true)] // with the low bits Windows may set
    [InlineData(0x0112, 0xF020, false, false)] // floating: minimised
    [InlineData(0x0112, 0xF120, true, false)] // SC_RESTORE
    [InlineData(0x0112, 0xF030, true, false)] // SC_MAXIMIZE
    [InlineData(0x0005, 0xF020, true, false)] // not a system command
    public void Only_a_docked_bar_ignores_minimise(int message, int command, bool docked, bool ignored) =>
        Assert.Equal(ignored, MainWindow.IgnoresMinimize(message, new IntPtr(command), docked));

    [Fact]
    public void Settings_reads_and_saves_the_choice()
    {
        wpf.Invoke(() =>
        {
            Assert.False(new SettingsViewModel(new LauncherSettings()).HideFromTaskbar);
            Assert.False(new SettingsViewModel(new LauncherSettings()).ToSettings().HideFromTaskbar);

            var viewModel = new SettingsViewModel(new LauncherSettings { HideFromTaskbar = true });
            Assert.True(viewModel.HideFromTaskbar);
            Assert.True(viewModel.ToSettings().HideFromTaskbar);

            viewModel.HideFromTaskbar = false;
            Assert.False(viewModel.ToSettings().HideFromTaskbar);
        });
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
}
