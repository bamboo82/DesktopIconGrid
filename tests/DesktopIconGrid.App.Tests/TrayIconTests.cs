using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopIconGrid.Services;

namespace DesktopIconGrid.App.Tests;

/// <summary>
/// The notification-area icon without Windows Forms: the structure sizes the shell checks, the icon the app draws
/// for it, and how the shell's callbacks become click, menu and balloon events.
/// </summary>
public class TrayIconTests
{
    [Fact]
    public void Structures_have_the_sizes_the_shell_checks()
    {
        // Shell_NotifyIcon and SHAppBarMessage reject a cbSize they do not know.
        Assert.Equal(Environment.Is64BitProcess ? 976 : 956, Marshal.SizeOf<NativeMethods.NOTIFYICONDATA>());
        Assert.Equal(Environment.Is64BitProcess ? 48 : 36, Marshal.SizeOf<NativeMethods.APPBARDATA>());
        Assert.Equal(104, Marshal.SizeOf<NativeMethods.MONITORINFOEX>());
    }

    [Fact]
    public void The_icon_is_drawn_upright_at_the_requested_size_and_keeps_its_transparency()
    {
        RunOnSta(() =>
        {
            // Red over blue on a transparent square: a bottom-up DIB would put blue on top, a lost alpha channel would
            // make the margin opaque.
            var drawing = new DrawingGroup();
            using (var context = drawing.Open())
            {
                context.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, 40, 40));
                context.DrawRectangle(Brushes.Red, null, new Rect(10, 10, 20, 10));
                context.DrawRectangle(Brushes.Blue, null, new Rect(10, 20, 20, 10));
            }

            var icon = TrayIcon.CreateIcon(new DrawingImage(drawing), 40);
            Assert.NotEqual(IntPtr.Zero, icon);
            try
            {
                var source = Imaging.CreateBitmapSourceFromHIcon(icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                Assert.Equal(40, source.PixelWidth);
                Assert.Equal(40, source.PixelHeight);

                var pixels = new byte[40 * 40 * 4];
                new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0).CopyPixels(pixels, 40 * 4, 0);
                (byte B, byte G, byte R, byte A) At(int x, int y)
                {
                    var i = ((y * 40) + x) * 4;
                    return (pixels[i], pixels[i + 1], pixels[i + 2], pixels[i + 3]);
                }

                var top = At(20, 14);
                Assert.True(top.R > 200 && top.B < 60 && top.A > 200, $"upper half {top}");
                var bottom = At(20, 25);
                Assert.True(bottom.B > 200 && bottom.R < 60 && bottom.A > 200, $"lower half {bottom}");
                Assert.True(At(3, 3).A < 20, $"transparent corner {At(3, 3)}");
            }
            finally
            {
                NativeMethods.DestroyIcon(icon);
            }
        });
    }

    [Fact]
    public void Shell_callbacks_become_click_menu_and_balloon_events()
    {
        RunOnSta(() =>
        {
            // Whether or not the test machine has a notification area, the hidden window exists and decodes callbacks.
            using var tray = new TrayIcon(Square(), "Desktop Icon Grid tests");
            var events = new List<string>();
            tray.Clicked += (_, _) => events.Add("click");
            tray.MenuRequested += (_, _) => events.Add("menu");
            tray.BalloonClicked += (_, _) => events.Add("balloon");

            // Version 4 puts the event in the low word of lParam (the icon id in the high word); before it, lParam is
            // the plain mouse message.
            IntPtr Event(int version4Event, int legacyMessage) =>
                tray.UsesVersion4 ? (IntPtr)(version4Event | (1 << 16)) : (IntPtr)legacyMessage;

            Send(tray.WindowHandle, Event(0x0400, 0x0202)); // NIN_SELECT / WM_LBUTTONUP
            Send(tray.WindowHandle, Event(0x007B, 0x0205)); // WM_CONTEXTMENU / WM_RBUTTONUP
            Send(tray.WindowHandle, Event(0x0405, 0x0405)); // NIN_BALLOONUSERCLICK
            Send(tray.WindowHandle, Event(0x0200, 0x0200)); // WM_MOUSEMOVE: no event

            Assert.Equal(["click", "menu", "balloon"], events);
        });
    }

    [Fact]
    public void A_disposed_icon_ignores_notifications_and_refreshes()
    {
        RunOnSta(() =>
        {
            var tray = new TrayIcon(Square(), "Desktop Icon Grid tests");
            tray.Dispose();

            tray.Notify("title", "message");
            tray.Refresh();
            tray.Dispose();
        });
    }

    private static DrawingImage Square()
    {
        var drawing = new GeometryDrawing(Brushes.SteelBlue, null, new RectangleGeometry(new Rect(0, 0, 16, 16)));
        drawing.Freeze();
        return new DrawingImage(drawing);
    }

    private static void Send(IntPtr window, IntPtr lParam)
    {
        Assert.NotEqual(IntPtr.Zero, window);
        SendMessage(window, TrayIcon.CallbackMessage, IntPtr.Zero, lParam);
    }

    /// <summary>WPF objects and window handles need a single-threaded apartment; xunit's threads are multi-threaded.</summary>
    private static void RunOnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
}
