using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DesktopIconGrid.Services;

/// <summary>One entry of the tray menu; <see cref="Separator"/> draws a line.</summary>
public sealed record TrayMenuEntry(string? Text, Action? Execute = null, bool IsChecked = false, bool IsEnabled = true)
{
    public static readonly TrayMenuEntry Separator = new(Text: null);

    public bool IsSeparator => Text is null;
}

/// <summary>The notification-area icon with a menu that is rebuilt every time it opens.</summary>
public sealed class TrayIcon : IDisposable
{
    private const int SmCxSmIcon = 49;

    private readonly System.Windows.Forms.NotifyIcon _notifyIcon;
    private readonly System.Windows.Forms.ContextMenuStrip _menu = new();
    private readonly System.Drawing.Icon? _icon;
    private Func<IEnumerable<TrayMenuEntry>>? _buildMenu;
    private bool _disposed;

    public TrayIcon(ImageSource image, string text)
    {
        _icon = CreateIcon(image, SmallIconSize());
        _notifyIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = _icon ?? System.Drawing.SystemIcons.Application,
            Text = text.Length > 63 ? text[..63] : text,
            ContextMenuStrip = _menu,
            Visible = true,
        };
        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == System.Windows.Forms.MouseButtons.Left)
            {
                Clicked?.Invoke(this, EventArgs.Empty);
            }
        };
        _menu.Opening += (_, _) => Rebuild();
    }

    /// <summary>Left click on the icon.</summary>
    public event EventHandler? Clicked;

    public void SetMenu(Func<IEnumerable<TrayMenuEntry>> buildMenu)
    {
        _buildMenu = buildMenu;
        Rebuild();
    }

    /// <summary>Shows a balloon/toast from the icon.</summary>
    public void Notify(string title, string message) =>
        _notifyIcon.ShowBalloonTip(4000, title, message, System.Windows.Forms.ToolTipIcon.Info);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menu.Dispose();
        _icon?.Dispose();
    }

    private void Rebuild()
    {
        _menu.Items.Clear();
        if (_buildMenu is null)
        {
            return;
        }

        foreach (var entry in _buildMenu())
        {
            if (entry.IsSeparator)
            {
                _menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
                continue;
            }

            var item = new System.Windows.Forms.ToolStripMenuItem(entry.Text)
            {
                Checked = entry.IsChecked,
                Enabled = entry.IsEnabled,
            };
            var execute = entry.Execute;
            if (execute is not null)
            {
                item.Click += (_, _) => execute();
            }

            _menu.Items.Add(item);
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

    /// <summary>Renders a WPF image (the app's vector icon) into a GDI icon of exactly <paramref name="size"/> pixels, so it is crisp at any DPI.</summary>
    private static System.Drawing.Icon? CreateIcon(ImageSource image, int size)
    {
        try
        {
            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen())
            {
                context.DrawImage(image, new Rect(0, 0, size, size));
            }

            var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            stream.Position = 0;

            using var drawing = new System.Drawing.Bitmap(stream);
            var handle = drawing.GetHicon();
            try
            {
                // Clone so the icon owns its handle and can be disposed normally.
                using var temporary = System.Drawing.Icon.FromHandle(handle);
                return (System.Drawing.Icon)temporary.Clone();
            }
            finally
            {
                NativeMethods.DestroyIcon(handle);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.Runtime.InteropServices.ExternalException)
        {
            return null;
        }
    }

    private static class NativeMethods
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        public static extern bool DestroyIcon(IntPtr hIcon);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern uint GetDpiForSystem();

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern int GetSystemMetricsForDpi(int nIndex, uint dpi);
    }
}
