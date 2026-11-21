using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Services;

namespace DesktopIconGrid.App.Tests;

/// <summary>
/// 0.14.0 WP3: a see-through background on real windows, read back from the screen. The bar's background takes the
/// transparency and what is drawn on it does not; a click on the background still lands on the window.
/// </summary>
[Collection(WpfWindowsCollection.Name)]
public sealed class SeeThroughTests(WpfThread wpf)
{
    private static readonly Color Behind = Color.FromRgb(255, 0, 0);
    private static readonly Color Bar = Color.FromRgb(0, 0, 255);
    private static readonly Color Solid = Color.FromRgb(0, 255, 0);

    [Fact]
    public void Only_the_background_takes_the_transparency_and_the_brushes_drawn_on_it_stay_solid()
    {
        var resources = new ResourceDictionary();

        ThemeManager.Apply(ThemeCatalog.Classic, resources, translucentBar: true, tintOpacity: 0.2);

        Assert.Equal(0.2, Assert.IsType<SolidColorBrush>(resources[ThemeManager.BarBackgroundKey]).Opacity);
        foreach (var key in new[] { ThemeManager.ForegroundKey, ThemeManager.BorderKey, ThemeManager.HoverKey, ThemeManager.HoverBorderKey, ThemeManager.PressedKey, ThemeManager.AccentKey, ThemeManager.GripKey })
        {
            Assert.Equal(1.0, Assert.IsType<SolidColorBrush>(resources[key]).Opacity);
        }
    }

    [Fact]
    public void A_see_through_background_shows_what_is_behind_while_what_is_drawn_on_it_keeps_its_colour_and_takes_clicks()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        Window? behind = null;
        Window? bar = null;
        try
        {
            var seeThrough = false;
            var barHandle = IntPtr.Zero;
            var behindHandle = IntPtr.Zero;
            wpf.Invoke(() =>
            {
                // A red window, and over its middle a "bar": a blue background at 20 % with a solid green square on it.
                behind = Plain(80, 80, 320, 220, new SolidColorBrush(Behind));
                behind.Show();
                behindHandle = new WindowInteropHelper(behind).Handle;
                bar = Plain(120, 120, 240, 140, Brushes.Transparent);
                bar.Content = new Border
                {
                    Background = new SolidColorBrush(Bar) { Opacity = 0.2 },
                    Child = new Rectangle { Width = 40, Height = 40, Fill = new SolidColorBrush(Solid), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
                };
                bar.SourceInitialized += (_, _) => seeThrough = WindowTransparency.Set((HwndSource)PresentationSource.FromVisual(bar), seeThrough: true);
                bar.Show();
                barHandle = new WindowInteropHelper(bar).Handle;
            });
            Assert.True(seeThrough, "Windows refused the see-through background");

            Point uncovered = default, background = default, square = default;
            wpf.Invoke(() =>
            {
                uncovered = behind!.PointToScreen(new Point(15, 15)); // the red window where the bar is not
                background = bar!.PointToScreen(new Point(20, 20));
                square = bar.PointToScreen(new Point(120, 70));
            });

            // Read once the square is on the screen: WPF draws on its own thread and DWM composes after it.
            var (uncoveredColour, backgroundColour, squareColour) = (default(Color), default(Color), default(Color));
            for (var attempt = 0; attempt < 60; attempt++)
            {
                wpf.Settle();
                _ = DwmFlush();
                (uncoveredColour, backgroundColour, squareColour) = (PixelAt(uncovered), PixelAt(background), PixelAt(square));
                if (squareColour == Solid && uncoveredColour == Behind)
                {
                    break;
                }

                Thread.Sleep(50);
            }

            var seen = $"behind {Describe(uncoveredColour)}, background {Describe(backgroundColour)}, square {Describe(squareColour)}";
            Assert.True(uncoveredColour == Behind, $"the screen could not be read here: {seen}");
            Assert.True(squareColour == Solid, $"what is drawn on the background is not solid: {seen}");

            // 20 % blue over red is about (204, 0, 51); a window-wide alpha would have faded the square too, and an
            // opaque surface would show the blue at 20 % over black, (0, 0, 51).
            Assert.True(
                Math.Abs(backgroundColour.R - 204) <= 8 && backgroundColour.G <= 8 && Math.Abs(backgroundColour.B - 51) <= 8,
                $"the background does not let the window behind show through: {seen}");

            Assert.Equal(barHandle, WindowFromPoint(new NativePoint((int)background.X, (int)background.Y))); // not the window behind
            Assert.NotEqual(behindHandle, barHandle);
        }
        finally
        {
            wpf.Invoke(() =>
            {
                bar?.Close();
                behind?.Close();
            });
        }
    }

    private static Window Plain(double left, double top, double width, double height, Brush background) => new()
    {
        WindowStyle = WindowStyle.None,
        ResizeMode = ResizeMode.NoResize,
        ShowInTaskbar = false,
        ShowActivated = false,
        Topmost = true,
        WindowStartupLocation = WindowStartupLocation.Manual,
        Left = left,
        Top = top,
        Width = width,
        Height = height,
        Background = background,
    };

    private static string Describe(Color colour) => $"({colour.R}, {colour.G}, {colour.B})";

    private static Color PixelAt(Point point)
    {
        var screen = GetDC(IntPtr.Zero);
        try
        {
            var value = GetPixel(screen, (int)point.X, (int)point.Y); // 0x00BBGGRR
            return Color.FromRgb((byte)(value & 0xFF), (byte)((value >> 8) & 0xFF), (byte)((value >> 16) & 0xFF));
        }
        finally
        {
            _ = ReleaseDC(IntPtr.Zero, screen);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct NativePoint(int X, int Y);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern uint GetPixel(IntPtr hdc, int x, int y);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(NativePoint point);

    [DllImport("dwmapi.dll")]
    private static extern int DwmFlush();
}
