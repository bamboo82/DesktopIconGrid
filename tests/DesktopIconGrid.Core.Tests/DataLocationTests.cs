using DesktopIconGrid.Core.Storage;

namespace DesktopIconGrid.Core.Tests;

/// <summary>Portable mode: data next to the program, and shortcuts that follow the program's drive letter.</summary>
public class DataLocationTests
{
    [Fact]
    public void Portable_txt_next_to_the_program_keeps_the_data_there()
    {
        var program = Path.Combine("E:", "Tools", "DesktopIconGrid") + Path.DirectorySeparatorChar;
        var local = Path.Combine("C:", "Users", "me", "AppData", "Local");

        Assert.Equal((Path.TrimEndingDirectorySeparator(program), true), DataLocation.Resolve(program, local, path => path.EndsWith(DataLocation.PortableMarker, StringComparison.Ordinal)));
        Assert.Equal((Path.Combine(local, LayoutStorage.ApplicationFolderName), false), DataLocation.Resolve(program, local, _ => false));
    }

    [Theory]
    [InlineData(@"E:\Tools\DesktopIconGrid\", "E:")]
    [InlineData(@"e:\x", "E:")]
    [InlineData(@"\\nas\tools\", null)]
    [InlineData("/opt/app/", null)]
    [InlineData(null, null)]
    public void The_program_drive_is_its_letter(string? directory, string? drive)
    {
        Assert.Equal(drive, DataLocation.DriveOf(directory));
    }

    [Theory]
    [InlineData(@"E:\Tools\WinSCP\WinSCP.exe", "E:", @"%APPDRIVE%\Tools\WinSCP\WinSCP.exe")]
    [InlineData(@"e:\Notes", "E:", @"%APPDRIVE%\Notes")]
    [InlineData(@"C:\Windows\notepad.exe", "E:", @"C:\Windows\notepad.exe")]
    [InlineData(@"E:", "E:", @"E:")]
    [InlineData(@"https://example.com", "E:", @"https://example.com")]
    [InlineData(@"E:\Tools\x.exe", null, @"E:\Tools\x.exe")]
    public void Paths_on_the_program_drive_are_written_with_APPDRIVE(string path, string? drive, string stored)
    {
        Assert.Equal(stored, DataLocation.WithAppDrive(path, drive));
    }

    [Fact]
    public void APPDRIVE_expands_like_any_variable()
    {
        Environment.SetEnvironmentVariable(DataLocation.AppDriveVariable, "F:");
        try
        {
            Assert.Equal(@"F:\Tools\x.exe", Environment.ExpandEnvironmentVariables(@"%APPDRIVE%\Tools\x.exe"));
        }
        finally
        {
            Environment.SetEnvironmentVariable(DataLocation.AppDriveVariable, null);
        }
    }
}
