using System.Windows.Controls;
using DesktopIconGrid.ViewModels;
using DesktopIconGrid.Views;

namespace DesktopIconGrid.App.Tests;

/// <summary>0.15.0 WP4: the colour picker. In the WPF collection for its window.</summary>
[Collection(WpfWindowsCollection.Name)]
public sealed class ColourPickerTests(WpfThread wpf)
{
    [Fact]
    public void It_opens_at_the_colour_given_with_every_part_showing_it()
    {
        var picker = new ColourPickerViewModel("#ff0000");

        Assert.Equal("#FF0000", picker.Current);
        Assert.Equal("#FF0000", picker.Hex);
        Assert.Equal(("#FF0000", "255", "0", "0"), (picker.HexText, picker.RedText, picker.GreenText, picker.BlueText));
        Assert.Equal((240.0, 0.0, 0.0), (picker.MarkerX, picker.MarkerY, picker.HueX)); // full saturation, full brightness, hue 0

        Assert.Equal("#808080", new ColourPickerViewModel("not a colour").Current); // mid-grey when given none
    }

    [Fact]
    public void The_square_sets_saturation_and_brightness_and_the_strip_the_hue()
    {
        var picker = new ColourPickerViewModel("#FF0000");
        var changes = new List<string>();
        picker.ColourChanged += changes.Add;

        picker.SetHueAt(80); // a third of the strip: 120°, green
        Assert.Equal("#00FF00", picker.Hex);

        picker.SetFromSquare(120, 80); // half saturated, half bright
        Assert.Equal("#408040", picker.Hex);
        Assert.Equal(("64", "128", "64"), (picker.RedText, picker.GreenText, picker.BlueText));

        picker.SetFromSquare(500, -20); // held to the square
        Assert.Equal((240.0, 0.0), (picker.MarkerX, picker.MarkerY));
        Assert.Equal(["#00FF00", "#408040", "#00FF00"], changes); // the preview follows each change
    }

    [Fact]
    public void The_arrow_keys_move_the_ring_and_the_thumb_by_one_unit_and_the_hue_wraps()
    {
        var picker = new ColourPickerViewModel("#FF0000");
        picker.SetFromSquare(100, 100);

        picker.MoveMarker(1, 0);
        Assert.Equal(101, picker.MarkerX, 6);
        picker.MoveMarker(0, 10); // up ten, as with Shift
        Assert.Equal(90, picker.MarkerY, 6);

        picker.MoveHue(-1); // from hue 0, left wraps to the strip's end
        Assert.Equal(239, picker.HueX, 6);
        picker.MoveHue(1);
        Assert.Equal(0, picker.HueX, 6);
    }

    [Fact]
    public void The_boxes_refuse_what_is_not_a_colour_and_a_typed_colour_takes_effect_at_once()
    {
        var picker = new ColourPickerViewModel("#336699");

        picker.HexText = "#12345G";
        Assert.False(picker.IsHexTextValid);
        Assert.Equal("#336699", picker.Hex);

        picker.HexText = "abcdef"; // without the hash
        Assert.True(picker.IsHexTextValid);
        Assert.Equal("#ABCDEF", picker.Hex);
        Assert.Equal("abcdef", picker.HexText); // the box keeps what was typed

        picker.RedText = "300";
        Assert.False(picker.IsRedTextValid);
        picker.GreenText = "x";
        Assert.False(picker.IsGreenTextValid);
        Assert.Equal("#ABCDEF", picker.Hex);

        picker.BlueText = "0";
        Assert.Equal("#ABCD00", picker.Hex);

        picker.RefreshBoxes(); // the boxes are left: what is no colour goes
        Assert.Equal(("#ABCD00", "171", "205", "0"), (picker.HexText, picker.RedText, picker.GreenText, picker.BlueText));
        Assert.True(picker.IsRedTextValid && picker.IsGreenTextValid && picker.IsHexTextValid);
    }

    [Fact]
    public void A_grey_keeps_the_hue_and_black_keeps_the_saturation_so_the_marks_stay()
    {
        var picker = new ColourPickerViewModel("#FF0000");
        picker.SetHueAt(160); // 240°, blue
        picker.SetFromSquare(180, 40);
        var hue = picker.HueX;

        picker.HexText = "#808080";
        Assert.Equal(hue, picker.HueX, 6);
        Assert.Equal(0, picker.MarkerX, 6); // a grey has no saturation

        picker.SetFromSquare(180, 40);
        picker.HexText = "#000000";
        Assert.Equal(hue, picker.HueX, 6);
        Assert.Equal(180, picker.MarkerX, 6);
        Assert.Equal(160, picker.MarkerY, 6);
    }

    [Fact]
    public void A_swatch_sets_the_colour_and_the_swatches_are_Settings_own_unless_given()
    {
        var picker = new ColourPickerViewModel("#FF0000");
        Assert.Equal(SettingsViewModel.ColorPresets, picker.Presets);

        picker.SelectPresetCommand.Execute(SettingsViewModel.ColorPresets[1]);
        Assert.Equal(SettingsViewModel.ColorPresets[1].ToUpperInvariant(), picker.Hex);

        Assert.Equal(["#111111"], new ColourPickerViewModel(null, ["#111111"]).Presets);
    }

    [Fact]
    public void The_window_puts_the_ring_and_the_thumb_where_the_colour_is()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        wpf.Invoke(() =>
        {
            var window = new ColourPickerWindow(new ColourPickerViewModel("#FF0000"));
            try
            {
                window.ViewModel.SetFromSquare(120, 80);
                window.ViewModel.SetHueAt(60);
                Assert.Equal(120 - 7, Canvas.GetLeft(window.RingOuter), 6);
                Assert.Equal(80 - 7, Canvas.GetTop(window.RingOuter), 6);
                Assert.Equal(60 - 3, Canvas.GetLeft(window.HueThumb), 6);
                Assert.Equal(System.Windows.Media.TextFormattingMode.Display, System.Windows.Media.TextOptions.GetTextFormattingMode(window)); // a dialog like the others

                window.ViewModel.RedText = "999";
                Assert.Equal(System.Windows.Visibility.Visible, window.NotAColour.Visibility);
                window.ViewModel.RefreshBoxes();
                Assert.Equal(System.Windows.Visibility.Collapsed, window.NotAColour.Visibility);
            }
            finally
            {
                window.Close();
            }
        });
    }
}
