using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using DesktopIconGrid.Services;
using DesktopIconGrid.Views;

namespace DesktopIconGrid.App.Tests;

/// <summary>0.14.0 WP7: the program's icon set, from assets/icons, where the program takes each picture.</summary>
[Collection(WpfWindowsCollection.Name)]
public sealed class IconSetTests(WpfThread wpf) : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("dig-icons-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void The_tray_takes_the_exact_frame_at_16_20_24_and_32_and_the_nearest_otherwise()
    {
        IReadOnlyList<int> sizes = [16, 20, 24, 32, 40, 48];

        Assert.Equal([16, 20, 24, 32], new[] { 16, 20, 24, 32 }.Select(size => IconFrames.Pick(sizes, size)));
        Assert.Equal(32, IconFrames.Pick(sizes, 28)); // as near as 24: the larger, scaled down a little
        Assert.Equal(40, IconFrames.Pick(sizes, 36));
        Assert.Equal(48, IconFrames.Pick(sizes, 64)); // the largest the file has
        Assert.Equal(16, IconFrames.Pick(sizes, 12));
    }

    [Fact]
    public void The_built_in_tray_ico_has_the_small_frames_and_gives_an_icon_handle_at_each_tray_size()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        wpf.Invoke(() =>
        {
            var frames = MainWindow.TrayFrames();
            Assert.NotNull(frames);
            Assert.Subset(frames!.Sizes.ToHashSet(), new HashSet<int> { 16, 20, 24, 32 });
            foreach (var size in new[] { 16, 20, 24, 32, TrayIcon.SmallIconSize() })
            {
                var icon = frames.CreateIcon(size);
                Assert.NotEqual(IntPtr.Zero, icon);
                NativeMethods.DestroyIcon(icon);
            }

            using var tray = new TrayIcon(frames, "Desktop Icon Grid tests"); // made from the file's frame, and again on a DPI change
            tray.Refresh();
        });
    }

    [Fact]
    public void Every_window_icon_is_the_app_ico_with_all_its_frames()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        wpf.Invoke(() =>
        {
            var icon = Assert.IsAssignableFrom<BitmapFrame>(Application.Current.FindResource("AppIconImage"));
            Assert.True(icon.Decoder.Frames.Count >= 8, $"{icon.Decoder.Frames.Count} frames"); // 16 to 256: the title bar and Alt+Tab take theirs
            Assert.Contains(icon.Decoder.Frames, frame => frame.PixelWidth == 16);
            Assert.Contains(icon.Decoder.Frames, frame => frame.PixelWidth == 256);
        });
    }

    [Fact]
    public void The_Send_to_entry_shows_sendto_ico_next_to_the_program_and_the_programs_own_icon_without_it()
    {
        var program = Path.Combine(_folder, "DesktopIconGrid.exe");
        File.WriteAllText(program, string.Empty);
        Assert.Equal((program, 0), SendToEntry.IconFor(program)); // a copy without the file

        var icon = Path.Combine(_folder, SendToEntry.IconFileName);
        File.WriteAllText(icon, string.Empty);
        Assert.Equal((icon, 0), SendToEntry.IconFor(program));

        Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, SendToEntry.IconFileName))); // built next to the program
        Assert.False(Directory.Exists(Path.Combine(AppContext.BaseDirectory, "store")));
        Assert.False(Directory.Exists(Path.Combine(AppContext.BaseDirectory, "readme")));
    }

    [Fact]
    public void Only_this_programs_own_entry_for_this_copy_is_brought_up_to_date()
    {
        var program = Path.Combine(_folder, "DesktopIconGrid.exe");

        Assert.True(SendToEntry.IsThisCopy(program.ToUpperInvariant(), " --add ", program));
        Assert.False(SendToEntry.IsThisCopy(Path.Combine(_folder, "old", "DesktopIconGrid.exe"), "--add", program)); // a copy that moved
        Assert.False(SendToEntry.IsThisCopy(program, "--show", program)); // not the entry
        Assert.False(SendToEntry.IsThisCopy(null, "--add", program));
    }
}
