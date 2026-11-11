using DesktopIconGrid.Core.SpecialIcons;
using DesktopIconGrid.Core.Storage;

namespace DesktopIconGrid.Core.Tests;

public class PasteNamesTests
{
    [Fact]
    public void Pasted_pictures_and_text_get_dated_names_Windows_accepts()
    {
        var at = new DateTime(2026, 11, 6, 14, 30, 5);

        Assert.Equal("Screenshot 2026-11-06 143005.png", PasteNames.Image(at));
        Assert.Equal("Pasted text 2026-11-06 143005.txt", PasteNames.Text(at));
        Assert.Equal(PasteNames.Image(at), FileNames.Safe(PasteNames.Image(at)));
    }

    [Fact]
    public void The_preview_of_pasted_text_is_one_short_line()
    {
        Assert.Equal("hunter2", PasteNames.Preview("  hunter2\r\n"));
        Assert.Equal("a b", PasteNames.Preview("a\nb"));
        Assert.Equal("a b", PasteNames.Preview("a\u202Eb")); // a reordering character is shown as a space
        var cut = PasteNames.Preview(new string('x', 500));
        Assert.Equal(PasteNames.PreviewChars + 1, cut.Length);
        Assert.EndsWith("…", cut);
    }
}
