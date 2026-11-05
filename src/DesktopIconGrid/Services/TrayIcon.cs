using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopIconGrid.Core.Diagnostics;

namespace DesktopIconGrid.Services;

/// <summary>One entry of the tray menu; <see cref="Separator"/> draws a line.</summary>
public sealed record TrayMenuEntry(string? Text, Action? Execute = null, bool IsChecked = false, bool IsEnabled = true)
{
    public static readonly TrayMenuEntry Separator = new(Text: null);

    public bool IsSeparator => Text is null;
}

/// <summary>
/// The notification-area icon, through <c>Shell_NotifyIcon</c> on a hidden window of its own (no WinForms). The
/// menu is WPF, opened by the owner on <see cref="MenuRequested"/>. The shell's version-4 callbacks are used, so a
/// click, Enter on the icon, the context-menu request (right click or Shift+F10) and a balloon click arrive as
/// distinct events; the icon is added again when Explorer restarts.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private const int SmCxSmIcon = 49;
    private const uint IconId = 1;
    private const int CallbackMessage = 0x0400 + 1; // WM_USER + 1
    private const int WmLButtonUp = 0x0202;
    private const int WmRButtonUp = 0x0205;
    private const int WmContextMenu = 0x007B;
    private const int NinSelect = 0x0400;
    private const int NinKeySelect = 0x0401;
    private const int NinBalloonUserClick = 0x0405;
    private const uint NimAdd = 0;
    private const uint NimModify = 1;
    private const uint NimDelete = 2;
    private const uint NimSetVersion = 4;
    private const uint NifMessage = 0x01;
    private const uint NifIcon = 0x02;
    private const uint NifTip = 0x04;
    private const uint NifInfo = 0x10;
    private const uint NifShowTip = 0x80;
    private const uint NiifWarning = 0x02;
    private const uint NotifyIconVersion4 = 4;
    private const int WsPopup = unchecked((int)0x80000000);
    private const int WsExToolWindow = 0x00000080;
    private const int IdiApplication = 32512;

    private readonly ImageSource _image;
    private readonly string _text;
    private readonly HwndSource _source;
    private readonly uint _taskbarCreated;
    private IntPtr _icon;
    private bool _version4;
    private bool _disposed;

    public TrayIcon(ImageSource image, string text)
    {
        _image = image;
        _text = text;
        _taskbarCreated = NativeMethods.RegisterWindowMessage("TaskbarCreated");
        _source = new HwndSource(new HwndSourceParameters("DesktopIconGrid.Tray", 0, 0)
        {
            PositionX = 0,
            PositionY = 0,
            WindowStyle = WsPopup, // top level (so the shell's TaskbarCreated broadcast reaches it), never visible
            ExtendedWindowStyle = WsExToolWindow,
        });
        _source.AddHook(WndProc);
        _icon = CreateIcon(image, SmallIconSize());
        Add();
    }

    /// <summary>Left click on the icon (or Enter/Space while it has the keyboard focus).</summary>
    public event EventHandler? Clicked;

    /// <summary>Right click on the icon (or Shift+F10); the owner shows its menu at the pointer.</summary>
    public event EventHandler? MenuRequested;

    /// <summary>The user clicked a balloon shown by <see cref="Notify"/>.</summary>
    public event EventHandler? BalloonClicked;

    /// <summary>Shows a balloon/toast from the icon (Windows caps the title at 63 and the text at 255 characters).</summary>
    public void Notify(string title, string message)
    {
        if (_disposed)
        {
            return;
        }

        var text = string.IsNullOrWhiteSpace(message) ? title : message;
        var data = Data(NifInfo);
        data.szInfoTitle = Clip(title, 63);
        data.szInfo = text.Length > 255 ? text[..252] + "…" : text;
        data.dwInfoFlags = NiifWarning;
        NativeMethods.Shell_NotifyIcon(NimModify, ref data);
    }

    /// <summary>Re-renders the icon for the current DPI (after a monitor or scaling change).</summary>
    public void Refresh()
    {
        if (_disposed)
        {
            return;
        }

        var fresh = CreateIcon(_image, SmallIconSize());
        if (fresh == IntPtr.Zero)
        {
            return;
        }

        var old = _icon;
        _icon = fresh;
        var data = Data(NifIcon);
        data.hIcon = _icon;
        NativeMethods.Shell_NotifyIcon(NimModify, ref data);
        if (old != IntPtr.Zero)
        {
            NativeMethods.DestroyIcon(old);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        var data = Data(0);
        NativeMethods.Shell_NotifyIcon(NimDelete, ref data);
        _source.RemoveHook(WndProc);
        _source.Dispose();
        if (_icon != IntPtr.Zero)
        {
            NativeMethods.DestroyIcon(_icon);
            _icon = IntPtr.Zero;
        }
    }

    /// <summary>The notification area's icon size at the system DPI: 16 px at 100 %, 24 at 150 %, 32 at 200 %.</summary>
    internal static int SmallIconSize()
    {
        try
        {
            var size = NativeMethods.GetSystemMetricsForDpi(SmCxSmIcon, NativeMethods.GetDpiForSystem());
            return size > 0 ? size : 16;
        }
        catch (EntryPointNotFoundException)
        {
            return 16; // before Windows 10 1607
        }
    }

    /// <summary>Adds the icon (again, after an Explorer restart) and asks for the version-4 callbacks.</summary>
    private void Add()
    {
        var data = Data(NifMessage | NifIcon | NifTip | NifShowTip);
        data.uCallbackMessage = CallbackMessage;
        data.hIcon = _icon != IntPtr.Zero ? _icon : NativeMethods.LoadIcon(IntPtr.Zero, (IntPtr)IdiApplication);
        data.szTip = Clip(_text, 127);
        if (!NativeMethods.Shell_NotifyIcon(NimAdd, ref data))
        {
            AppLog.Default.Warning("App", "The tray icon could not be added to the notification area");
            return;
        }

        data.uTimeoutOrVersion = NotifyIconVersion4;
        _version4 = NativeMethods.Shell_NotifyIcon(NimSetVersion, ref data);
    }

    private NativeMethods.NOTIFYICONDATA Data(uint flags) => new()
    {
        cbSize = (uint)Marshal.SizeOf<NativeMethods.NOTIFYICONDATA>(),
        hWnd = _source.Handle,
        uID = IconId,
        uFlags = flags,
        szTip = string.Empty,
        szInfo = string.Empty,
        szInfoTitle = string.Empty,
    };

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == CallbackMessage)
        {
            // Version 4: the event is in the low word of lParam (the high word is the icon id, wParam the anchor point).
            // Without version 4 the whole lParam is the mouse message.
            var eventCode = _version4 ? (int)(lParam.ToInt64() & 0xFFFF) : (int)lParam.ToInt64();
            switch (eventCode)
            {
                case NinSelect:
                case NinKeySelect:
                case WmLButtonUp when !_version4:
                    Clicked?.Invoke(this, EventArgs.Empty);
                    break;
                case WmContextMenu:
                case WmRButtonUp when !_version4:
                    MenuRequested?.Invoke(this, EventArgs.Empty);
                    break;
                case NinBalloonUserClick:
                    BalloonClicked?.Invoke(this, EventArgs.Empty);
                    break;
            }

            handled = true;
        }
        else if (!_disposed && _taskbarCreated != 0 && msg == (int)_taskbarCreated)
        {
            Add(); // Explorer restarted and the notification area is empty again
        }

        return IntPtr.Zero;
    }

    private static string Clip(string text, int maxLength) => text.Length > maxLength ? text[..maxLength] : text;

    /// <summary>
    /// Renders a WPF image (the app's vector icon) into a GDI icon of exactly <paramref name="size"/> pixels, so it
    /// is crisp at any DPI: a top-down 32-bit DIB with straight alpha plus an empty mask, through CreateIconIndirect.
    /// </summary>
    private static IntPtr CreateIcon(ImageSource image, int size)
    {
        try
        {
            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen())
            {
                context.DrawImage(image, new Rect(0, 0, size, size));
            }

            var rendered = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            rendered.Render(visual);
            var straight = new FormatConvertedBitmap(rendered, PixelFormats.Bgra32, null, 0);
            var stride = size * 4;
            var pixels = new byte[stride * size];
            straight.CopyPixels(pixels, stride, 0);

            var header = new NativeMethods.BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<NativeMethods.BITMAPINFOHEADER>(),
                biWidth = size,
                biHeight = -size, // top-down
                biPlanes = 1,
                biBitCount = 32,
            };
            var color = NativeMethods.CreateDIBSection(IntPtr.Zero, ref header, 0, out var bits, IntPtr.Zero, 0);
            if (color == IntPtr.Zero || bits == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            var mask = IntPtr.Zero;
            try
            {
                Marshal.Copy(pixels, 0, bits, pixels.Length);
                var maskBits = new byte[((size + 15) / 16) * 2 * size]; // 1 bpp, rows padded to 16 bits; all zero = the alpha channel decides
                mask = NativeMethods.CreateBitmap(size, size, 1, 1, maskBits);
                var info = new NativeMethods.ICONINFO { fIcon = true, hbmMask = mask, hbmColor = color };
                return NativeMethods.CreateIconIndirect(ref info);
            }
            finally
            {
                NativeMethods.DeleteObject(color);
                if (mask != IntPtr.Zero)
                {
                    NativeMethods.DeleteObject(mask);
                }
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or ExternalException)
        {
            return IntPtr.Zero;
        }
    }

    private static class NativeMethods
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct NOTIFYICONDATA
        {
            public uint cbSize;
            public IntPtr hWnd;
            public uint uID;
            public uint uFlags;
            public uint uCallbackMessage;
            public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szTip;
            public uint dwState;
            public uint dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string szInfo;
            public uint uTimeoutOrVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string szInfoTitle;
            public uint dwInfoFlags;
            public Guid guidItem;
            public IntPtr hBalloonIcon;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAPINFOHEADER
        {
            public uint biSize;
            public int biWidth;
            public int biHeight;
            public ushort biPlanes;
            public ushort biBitCount;
            public uint biCompression;
            public uint biSizeImage;
            public int biXPelsPerMeter;
            public int biYPelsPerMeter;
            public uint biClrUsed;
            public uint biClrImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct ICONINFO
        {
            [MarshalAs(UnmanagedType.Bool)]
            public bool fIcon;
            public uint xHotspot;
            public uint yHotspot;
            public IntPtr hbmMask;
            public IntPtr hbmColor;
        }

        [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool Shell_NotifyIcon(uint message, ref NOTIFYICONDATA data);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern uint RegisterWindowMessage(string message);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr LoadIcon(IntPtr instance, IntPtr iconName);

        [DllImport("user32.dll")]
        public static extern IntPtr CreateIconIndirect(ref ICONINFO iconInfo);

        [DllImport("user32.dll")]
        public static extern uint GetDpiForSystem();

        [DllImport("user32.dll")]
        public static extern int GetSystemMetricsForDpi(int nIndex, uint dpi);

        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER bitmapInfo, uint usage, out IntPtr bits, IntPtr section, uint offset);

        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateBitmap(int width, int height, uint planes, uint bitCount, byte[] bits);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DeleteObject(IntPtr obj);
    }
}
