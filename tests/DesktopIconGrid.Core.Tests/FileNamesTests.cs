using DesktopIconGrid.Core.Storage;

namespace DesktopIconGrid.Core.Tests;

public class FileNamesTests
{
    [Theory]
    [InlineData("report.txt", "report.txt")]
    [InlineData("a<b>c:d|e*f?g", "a_b_c_d_e_f_g")]
    [InlineData("a/b\\c", "a_b_c")]
    [InlineData("  name  ", "name")]
    [InlineData("report.", "report")]
    [InlineData("report . . ", "report")]
    [InlineData("\u0001x\ty", "_x_y")]
    [InlineData("備忘錄.docx", "備忘錄.docx")]
    public void Forbidden_characters_and_trailing_dots_are_cleaned(string name, string expected) =>
        Assert.Equal(expected, FileNames.Safe(name));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("...")]
    [InlineData(null)]
    public void Nothing_usable_becomes_file(string? name) => Assert.Equal("file", FileNames.Safe(name));

    [Theory]
    [InlineData("CON", "CON_")]
    [InlineData("con", "con_")]
    [InlineData("nul.txt", "nul_.txt")]
    [InlineData("COM1", "COM1_")]
    [InlineData("lpt9.log", "lpt9_.log")]
    [InlineData("Aux.tar.gz", "Aux_.tar.gz")]
    [InlineData("COM¹", "COM¹_")]
    public void Reserved_device_names_get_a_suffix(string name, string expected) => Assert.Equal(expected, FileNames.Safe(name));

    [Theory]
    [InlineData("console.log")]
    [InlineData("CONTACTS")]
    [InlineData("com10")]
    [InlineData("lpt")]
    public void Names_that_merely_start_like_a_device_are_kept(string name) => Assert.Equal(name, FileNames.Safe(name));
}
