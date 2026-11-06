using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using DesktopIconGrid.Core;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Views;

namespace DesktopIconGrid.App.Tests;

/// <summary>The Show-at-pointer window built from its markup, on the shared WPF thread.</summary>
[Collection(WpfWindowsCollection.Name)]
public sealed class PointerWindowTests(WpfThread wpf) : IDisposable
{
    private const int WmSysCommand = 0x0112;
    private const int ScClose = 0xF060;

    private readonly string _directory = Directory.CreateTempSubdirectory("dig-pointer-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private TestBar CreateBar(int shortcuts)
    {
        var model = new LauncherModel();
        for (var i = 0; i < shortcuts; i++)
        {
            model.Add(new ShortcutItem { Name = $"Item {i}", Location = $"C:\\item{i}.exe" });
        }

        return new TestBar(_directory, model);
    }

    [Fact]
    public void Alt_F4_hides_the_window_and_the_hotkey_shows_it_again()
    {
        wpf.Invoke(() =>
        {
            using var bar = CreateBar(4);
            var window = new PointerWindow(bar.ViewModel);
            var handle = IntPtr.Zero;
            try
            {
                window.ShowAtPointer();
                handle = new WindowInteropHelper(window).Handle;
                Assert.True(window.IsVisible);

                SendMessage(handle, WmSysCommand, ScClose, IntPtr.Zero); // what Alt+F4 sends

                Assert.False(window.IsVisible);
                Assert.True(IsWindow(handle));
                window.ShowAtPointer(); // in 0.7.0 the window was closed by now, and this threw
                Assert.True(window.IsVisible);
            }
            finally
            {
                window.CloseForGood();
            }

            Assert.False(IsWindow(handle));
        });
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr hWnd);
}
