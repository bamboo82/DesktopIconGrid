using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using DesktopIconGrid.Core.Diagnostics;
using DesktopIconGrid.Services;

namespace DesktopIconGrid.App.Tests;

/// <summary>
/// 0.17.0 WP2: run as administrator, the program gets Explorer's messages only where its windows let them in: the tray
/// icon's callback and TaskbarCreated on the tray's window, the AppBar notifications on the docked bar's; nothing else.
/// </summary>
[Collection(WpfWindowsCollection.Name)]
public sealed class ShellMessagesTests(WpfThread wpf)
{
    private static DrawingImage Square()
    {
        var drawing = new GeometryDrawing(Brushes.SteelBlue, null, new RectangleGeometry(new Rect(0, 0, 16, 16)));
        drawing.Freeze();
        return new DrawingImage(drawing);
    }

    [Fact]
    public void The_tray_window_lets_in_exactly_the_icons_callback_and_TaskbarCreated()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        wpf.Invoke(() =>
        {
            var calls = new List<(IntPtr Window, uint Message)>();
            using var tray = new TrayIcon(Square(), "Desktop Icon Grid tests", (window, message) =>
            {
                calls.Add((window, message));
                return true;
            });

            Assert.NotEqual(IntPtr.Zero, tray.WindowHandle);
            Assert.Equal(
                [(tray.WindowHandle, (uint)TrayIcon.CallbackMessage), (tray.WindowHandle, RegisterWindowMessage("TaskbarCreated"))],
                calls);
        });
    }

    [Fact]
    public void The_docked_bars_window_lets_in_exactly_the_AppBar_message()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        wpf.Invoke(() =>
        {
            var calls = new List<(IntPtr Window, uint Message)>();
            using var source = new HwndSource(new HwndSourceParameters("DesktopIconGrid.Tests.AppBar", 100, 40));
            using var bar = new AppBar(source, (window, message) =>
            {
                calls.Add((window, message));
                return true;
            });

            Assert.Equal([(source.Handle, RegisterWindowMessage("DesktopIconGrid.AppBar"))], calls);
        });
    }

    [Fact]
    public void Letting_a_message_in_is_harmless_unelevated_and_a_refusal_stops_nothing()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        wpf.Invoke(() =>
        {
            using var source = new HwndSource(new HwndSourceParameters("DesktopIconGrid.Tests.Filter", 100, 40));
            Assert.True(ShellMessages.AllowOnWindow(source.Handle, RegisterWindowMessage("TaskbarCreated"))); // the real call

            var asked = 0;
            ShellMessages.Allow((_, _) => { asked++; return false; }, source.Handle, 0x0401, "test window"); // logged once, never thrown
            ShellMessages.Allow((_, _) => { asked++; return false; }, source.Handle, 0x0401, "test window");
            Assert.Equal(2, asked);
        });
    }

    [Fact]
    public void The_log_says_what_cannot_work_only_when_elevated()
    {
        var log = new AppLog();
        Elevation.NoteAtStart(log, () => false);
        Assert.Empty(log.Snapshot());

        Elevation.NoteAtStart(log, () => true);
        var entry = Assert.Single(log.Snapshot());
        Assert.Equal(Elevation.Note, entry.Message);
        Assert.Equal(LogLevel.Info, entry.Level);
        Assert.Contains("files cannot be dropped on the bar from Explorer", Elevation.Note, StringComparison.Ordinal);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string lpString);
}
