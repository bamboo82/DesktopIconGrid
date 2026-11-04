using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Core.Storage;

namespace DesktopIconGrid.Core.Tests;

public class TextIconTests
{
    [Fact]
    public void Normalized_trims_clamps_and_validates()
    {
        var icon = new TextIcon("  備忘錄  ", "  SimSun ", 500, true, "#abcdef").Normalized();

        Assert.NotNull(icon);
        Assert.Equal("備忘錄", icon.Text);
        Assert.Equal("SimSun", icon.FontFamily);
        Assert.Equal(TextIcon.MaxFontSize, icon.FontSize);
        Assert.True(icon.Bold);
        Assert.Equal("#ABCDEF", icon.Color);
    }

    [Fact]
    public void Normalized_drops_empty_text_and_bad_values()
    {
        Assert.Null(new TextIcon("   ").Normalized());

        var icon = new TextIcon(new string('x', 40), " ", double.NaN, false, "red").Normalized();

        Assert.NotNull(icon);
        Assert.Equal(TextIcon.MaxTextLength, icon.Text.Length);
        Assert.Null(icon.FontFamily);
        Assert.Equal(TextIcon.DefaultFontSize, icon.FontSize);
        Assert.Null(icon.Color);
    }

    [Fact]
    public void Text_icon_survives_the_layout_file_and_model_copies()
    {
        var directory = Path.Combine(Path.GetTempPath(), "DesktopIconGrid.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var storage = new LayoutStorage(Path.Combine(directory, "layout.json"));
            var shortcut = new ShortcutItem { Name = "Notes", Location = "notepad.exe", TextIcon = new TextIcon("記", "PMingLiU", 20, true, "#112233") };
            var group = new GroupItem { Name = "G", TextIcon = new TextIcon("工具") };
            storage.Save(new LauncherLayout { Items = [shortcut, group] });

            var loaded = storage.Load().Layout;
            var loadedShortcut = Assert.IsType<ShortcutItem>(loaded.Items[0]);
            Assert.Equal(new TextIcon("記", "PMingLiU", 20, true, "#112233"), loadedShortcut.TextIcon);
            Assert.Equal("工具", Assert.IsType<GroupItem>(loaded.Items[1]).TextIcon!.Text);

            var model = LauncherModel.FromLayout(loaded);
            Assert.Equal("記", model.Items[0].TextIcon!.Text);

            var edited = (ShortcutItem)model.Items[0].Clone();
            edited.TextIcon = new TextIcon(" 新 ");
            model.Update(edited);
            Assert.Equal("新", model.Items[0].TextIcon!.Text);

            edited.TextIcon = null;
            model.Update(edited);
            Assert.Null(model.Items[0].TextIcon);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
