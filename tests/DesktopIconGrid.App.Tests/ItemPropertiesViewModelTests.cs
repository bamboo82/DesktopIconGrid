using System.Windows.Media;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Services;
using DesktopIconGrid.ViewModels;

namespace DesktopIconGrid.App.Tests;

[Collection("Shared icon worker")]
public class ItemPropertiesViewModelTests
{
    private sealed class CountingIcons : IIconProvider
    {
        private int _lookups;

        public int Lookups => Volatile.Read(ref _lookups);

        public ImageSource? GetIcon(string source, int pixelSize)
        {
            Interlocked.Increment(ref _lookups);
            return null;
        }

        public ImageSource? GetItemIcon(LauncherItem item, int pixelSize) => null;

        public ImageSource? GetFolderIcon(int pixelSize) => null;

        public bool CanResolve(string? source) => !string.IsNullOrEmpty(source);

        public void ClearCache()
        {
        }
    }

    [Fact]
    public async Task Typing_a_location_resolves_the_preview_once_after_the_last_keystroke()
    {
        var previous = ItemPropertiesViewModel.PreviewDebounce;
        ItemPropertiesViewModel.PreviewDebounce = TimeSpan.FromMilliseconds(150); // edits below are 10 ms apart
        try
        {
            var icons = new CountingIcons();
            var viewModel = new ItemPropertiesViewModel(new ShortcutItem { Name = "Tool", Location = "C:\\tool.exe" }, isNew: false, icons);
            await WaitForLookupsAsync(icons, 1); // the dialog opens with a preview right away

            viewModel.Location = "C:\\t";
            await Task.Delay(10);
            viewModel.Location = "C:\\to";
            await Task.Delay(10);
            viewModel.Location = "C:\\too";
            await WaitForLookupsAsync(icons, 2);
            await Task.Delay(400);

            Assert.Equal(2, icons.Lookups); // one for the opening, one for the whole burst of edits
        }
        finally
        {
            ItemPropertiesViewModel.PreviewDebounce = previous;
        }
    }

    private static async Task WaitForLookupsAsync(CountingIcons icons, int expected)
    {
        for (var i = 0; i < 100 && icons.Lookups < expected; i++)
        {
            await Task.Delay(20);
        }
    }
}
