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
}
