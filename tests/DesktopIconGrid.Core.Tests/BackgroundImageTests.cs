using System.Text.Json;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Core.Storage;

namespace DesktopIconGrid.Core.Tests;

/// <summary>0.15.0 WP5: the custom theme's background picture, its stored name, its fit and its effect on the text.</summary>
public class BackgroundImageTests
{
    private const string Stored = "0123456789abcdef0123456789abcdef.jpg";

    [Theory]
    [InlineData(Stored, true)]
    [InlineData("0123456789abcdef0123456789abcdef.png", true)]
    [InlineData("0123456789ABCDEF0123456789ABCDEF.jpg", false)] // the program writes lower case
    [InlineData(@"..\0123456789abcdef0123456789abcdef.jpg", false)]
    [InlineData(@"C:\Windows\0123456789abcdef0123456789abcdef.jpg", false)]
    [InlineData("0123456789abcdef0123456789abcdef.exe", false)]
    [InlineData("holiday.jpg", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_a_name_the_program_gives_is_read_from_the_folder(string? name, bool stored)
    {
        Assert.Equal(stored, BackgroundImage.IsStoredName(name));
    }

    [Fact]
    public void The_name_comes_from_the_content_so_the_same_picture_is_stored_once()
    {
        var name = BackgroundImage.NameFor([1, 2, 3], png: false);
        Assert.True(BackgroundImage.IsStoredName(name));
        Assert.Equal(name, BackgroundImage.NameFor([1, 2, 3], png: false));
        Assert.EndsWith(".png", BackgroundImage.NameFor([1, 2, 3], png: true), StringComparison.Ordinal);
        Assert.NotEqual(name, BackgroundImage.NameFor([1, 2, 4], png: false));
    }

    [Fact]
    public void Values_out_of_range_go_back_to_their_defaults_and_a_name_that_is_not_stored_drops_the_picture()
    {
        var picture = new BackgroundImage { File = Stored, FocusX = 1.5, FocusY = double.NaN, Zoom = 50, Strength = 10, Average = "#abcdef" }.Normalized();
        Assert.Equal(new BackgroundImage { File = Stored, Average = "#ABCDEF" }, picture);

        var kept = new BackgroundImage { File = Stored, FocusX = 0, FocusY = 1, Zoom = 400, Strength = 20 };
        Assert.Equal(kept, kept.Normalized());

        Assert.Null(new BackgroundImage { File = @"..\..\secret.jpg" }.Normalized());

        var settings = new LauncherSettings { BackgroundImage = new BackgroundImage { File = @"C:\x.jpg" } };
        Assert.True(settings.Normalize());
        Assert.Null(settings.BackgroundImage);
    }

    [Fact]
    public void The_picture_reads_back_with_the_theme()
    {
        var settings = new LauncherSettings { BackgroundImage = new BackgroundImage { File = Stored, FocusX = 0.25, FocusY = 0.75, Zoom = 150, Strength = 60, Average = "#112233" } };
        var json = JsonSerializer.Serialize(settings, LayoutStorage.JsonOptions);
        using (var document = JsonDocument.Parse(json))
        {
            var picture = document.RootElement.GetProperty("backgroundImage");
            Assert.Equal(Stored, picture.GetProperty("file").GetString());
            Assert.Equal(150, picture.GetProperty("zoom").GetInt32());
            Assert.Equal(60, picture.GetProperty("strength").GetInt32());
        }

        var copy = new LauncherSettings();
        copy.CopyFrom(JsonSerializer.Deserialize<LauncherSettings>(json, LayoutStorage.JsonOptions)!);
        Assert.Equal(settings.BackgroundImage, copy.BackgroundImage);
        Assert.DoesNotContain("backgroundImage", JsonSerializer.Serialize(new LauncherSettings(), LayoutStorage.JsonOptions), StringComparison.Ordinal);
    }

    [Fact]
    public void Pictures_no_setting_names_are_deleted_and_nothing_else_in_the_folder()
    {
        var other = "fedcba9876543210fedcba9876543210.png";
        Assert.Equal([other], BackgroundImage.Unused([Stored, other, "desktop.ini", "notes.txt"], inUse: Stored));
        Assert.Equal([Stored, other], BackgroundImage.Unused([Stored, other], inUse: null));
    }

    [Fact]
    public void A_wide_bar_on_a_tall_picture_takes_its_full_width_and_a_tall_bar_its_full_height()
    {
        // A 1000 × 2000 picture behind a 500 × 100 bar: scaled by 0.5 to cover, it shows a 1000 × 200 band.
        Assert.Equal((0.0, 900.0, 1000.0, 200.0), BackgroundFit.SourceRect(1000, 2000, 500, 100, 0.5, 0.5, 100));
        // A 2000 × 1000 picture behind a 100 × 500 bar: a 200 × 1000 column.
        Assert.Equal((900.0, 0.0, 200.0, 1000.0), BackgroundFit.SourceRect(2000, 1000, 100, 500, 0.5, 0.5, 100));
    }

    [Fact]
    public void A_bar_larger_or_smaller_than_the_picture_is_covered_alike()
    {
        Assert.Equal((0.0, 30.0, 200.0, 40.0), BackgroundFit.SourceRect(200, 100, 1000, 200, 0.5, 0.5, 100)); // scaled up 5 times
        Assert.Equal((0.0, 30.0, 200.0, 40.0), BackgroundFit.SourceRect(200, 100, 50, 10, 0.5, 0.5, 100)); // scaled down 4 times
    }

    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(1, 0, 600, 0)]
    [InlineData(0, 1, 0, 800)]
    [InlineData(1, 1, 600, 800)]
    public void The_focus_in_a_corner_pushes_the_picture_back_so_there_is_never_a_gap(double focusX, double focusY, double x, double y)
    {
        // A 1000 × 1000 picture behind a 400 × 200 bar at zoom 250: it shows 400 × 200 of it.
        var (left, top, width, height) = BackgroundFit.SourceRect(1000, 1000, 400, 200, focusX, focusY, 250);
        Assert.Equal((x, y, 400.0, 200.0), (left, top, width, height));
        Assert.True(left >= 0 && top >= 0 && left + width <= 1000 && top + height <= 1000);
    }

    [Fact]
    public void Zoom_400_shows_a_quarter_of_what_zoom_100_shows_around_the_focus()
    {
        var whole = BackgroundFit.SourceRect(1600, 900, 160, 90, 0.5, 0.5, 100);
        var zoomed = BackgroundFit.SourceRect(1600, 900, 160, 90, 0.5, 0.5, 400);
        Assert.Equal((0.0, 0.0, 1600.0, 900.0), whole);
        Assert.Equal((600.0, 337.5, 400.0, 225.0), zoomed);
    }

    [Fact]
    public void With_the_picture_shown_the_text_is_chosen_for_the_picture_blended_into_the_colour()
    {
        var settings = new LauncherSettings
        {
            Theme = ThemeCatalog.CustomId,
            BackgroundColor = "#F3F3F3", // light: dark text
            BackgroundImage = new BackgroundImage { File = Stored, Strength = 100, Average = "#101010" }, // a dark picture
        };

        Assert.Equal("#202020", ThemeCatalog.Resolve(settings).Foreground); // the file is not on this PC: the colour alone
        var shown = ThemeCatalog.Resolve(settings, pictureShown: true);
        Assert.Equal("#FFFFFF", shown.Foreground);
        Assert.Equal("#2EFFFFFF", shown.Hover); // the dark set
        Assert.Equal("#F3F3F3", shown.Background); // the colour and its border stay
        Assert.Equal("#797979", shown.Border);

        settings.BackgroundImage = settings.BackgroundImage with { Strength = 20 }; // a faint dark picture on light grey stays light
        Assert.Equal("#202020", ThemeCatalog.Resolve(settings, pictureShown: true).Foreground);
        Assert.Equal("#C6C6C6", ThemeCatalog.Blend("#F3F3F3", "#101010", 20)); // 243 + (16 − 243) × 0.2 = 197.6
    }
}
