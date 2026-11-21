using System.Collections.Concurrent;
using System.IO;
using System.Windows.Media;
using System.Windows.Threading;
using DesktopIconGrid.Core;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Core.Storage;
using DesktopIconGrid.Services;
using DesktopIconGrid.SpecialIcons;
using DesktopIconGrid.ViewModels;

namespace DesktopIconGrid.App.Tests;

/// <summary>0.15.0 WP8, more icon sizes: the list, a typed size, the pixels asked of the source, the cells.</summary>
[Collection("Shared icon worker")]
public sealed class IconSizeTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("dig-icon-size-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    /// <summary>An icon provider that finds nothing and remembers each item and size it was asked for.</summary>
    private sealed class RecordingIcons : IIconProvider
    {
        private readonly ConcurrentQueue<(string Name, int Size)> _asked = new();

        public ImageSource? GetIcon(string source, int pixelSize) => null;

        public ImageSource? GetItemIcon(LauncherItem item, int pixelSize)
        {
            _asked.Enqueue((item.Name, pixelSize));
            return null;
        }

        public ImageSource? GetFolderIcon(int pixelSize) => null;

        public bool CanResolve(string? source) => false;

        public void ClearCache()
        {
        }

        /// <summary>Completes once <paramref name="name"/> was asked at <paramref name="size"/> (the icon worker runs apart).</summary>
        public async Task AskedAsync(string name, int size)
        {
            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (!_asked.Contains((name, size)))
            {
                Assert.True(DateTime.UtcNow < deadline, $"{name} was not asked at {size} px; asked: {string.Join(", ", _asked)}");
                await Task.Delay(20);
            }
        }
    }

    private LauncherViewModel Bar(LauncherModel model, IIconProvider icons) => new(
        model,
        new LayoutStorage(Path.Combine(_directory, "layout.json")),
        icons,
        new RecordingDialogs(),
        new RecordingLauncher(),
        new ProcessTracker(Dispatcher.CurrentDispatcher, Path.Combine(_directory, "logs")),
        new SpecialIconRegistry([]),
        action => action(),
        null,
        _ => false);

    [Fact]
    public async Task Icons_are_asked_at_the_first_usual_size_covering_their_pixels_and_again_when_the_scale_wants_more()
    {
        // Before 0.15.0 the bar asked for twice the icon size whatever the screen: 144 for 72.
        var model = new LauncherModel();
        model.Add(new ShortcutItem { Name = "A", Location = @"C:\a.exe" });
        model.Settings.IconSize = 72;
        var icons = new RecordingIcons();
        var bar = Bar(model, icons);

        Assert.Equal(72, bar.IconSourceSize);
        await icons.AskedAsync("A", 72); // at 100 %

        bar.IconScale = 1.5;
        Assert.Equal(128, bar.IconSourceSize);
        await icons.AskedAsync("A", 128); // 108 pixels: the 128 image, scaled down
    }

    [Theory]
    [InlineData(20, 40)]
    [InlineData(128, 148)]
    [InlineData(50, 70)]
    public void A_cell_is_the_icon_and_its_spacing_on_both_sides_at_any_size(int iconSize, double cell)
    {
        var model = new LauncherModel();
        var bar = Bar(model, new NoIcons());
        var settings = model.Settings.Clone();
        settings.IconSize = iconSize;
        settings.CellPadding = 10;
        bar.ApplySettings(settings);

        Assert.Equal(iconSize, bar.IconSize);
        Assert.Equal(cell, bar.CellWidth);
        Assert.Equal(cell, bar.CellHeight);
    }

    [Fact]
    public void The_box_lists_thirteen_sizes_and_takes_one_typed_to_the_nearer_end()
    {
        var viewModel = new SettingsViewModel(new LauncherSettings { IconSize = 32 });
        Assert.Equal("32 px", viewModel.IconSizeText);
        Assert.Equal(
            ["16 px", "20 px", "24 px", "28 px", "32 px", "40 px", "48 px", "56 px", "64 px", "72 px", "80 px", "96 px", "128 px"],
            viewModel.IconSizeChoices);

        viewModel.IconSizeText = "72 px"; // picked from the list
        Assert.Equal(72, viewModel.IconSize);

        viewModel.IconSizeText = "50"; // typed, not listed
        Assert.Equal("50 px", viewModel.IconSizeText);
        Assert.Equal(50, viewModel.ToSettings().IconSize);

        viewModel.IconSizeText = "300";
        Assert.Equal((256, "256 px"), (viewModel.IconSize, viewModel.IconSizeText));

        viewModel.IconSizeText = "big"; // not a number: the size before
        Assert.Equal((256, "256 px"), (viewModel.IconSize, viewModel.IconSizeText));

        Assert.Equal("200 px", new SettingsViewModel(new LauncherSettings { IconSize = 200 }).IconSizeText); // a typed size from the layout
    }
}
