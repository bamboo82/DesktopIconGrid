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

        Assert.Equal((Path.TrimEndingDirectorySeparator(program), true, false), DataLocation.Resolve(program, local, path => path.EndsWith(DataLocation.PortableMarker, StringComparison.Ordinal)));
        Assert.Equal((Path.Combine(local, LayoutStorage.ApplicationFolderName), false, false), DataLocation.Resolve(program, local, _ => false));
    }

    [Fact]
    public void A_portable_folder_that_cannot_be_written_leaves_the_data_in_LocalAppData_and_says_so()
    {
        // The review's R7: portable.txt under Program Files without elevation, or on a read-only drive, stopped the app.
        var program = Path.Combine("C:", "Program Files", "DesktopIconGrid");
        var local = Path.Combine("C:", "Users", "me", "AppData", "Local");

        var resolved = DataLocation.Resolve(program, local, path => path.EndsWith(DataLocation.PortableMarker, StringComparison.Ordinal), canWrite: _ => false);

        Assert.Equal((Path.Combine(local, LayoutStorage.ApplicationFolderName), false, true), resolved);
    }

    [Theory]
    [InlineData(@"E:\Tools\DesktopIconGrid\", "C:", "E:")]
    [InlineData(@"C:\Tools\DesktopIconGrid\", "C:", null)] // the review's R4: Windows' own drive is never written as %APPDRIVE%
    [InlineData(@"c:\Tools\", "C:", null)]
    [InlineData(@"\\nas\tools\", "C:", null)]
    [InlineData(@"E:\Tools\", null, "E:")]
    public void Paths_are_written_with_APPDRIVE_only_for_a_drive_that_is_not_Windows_own(string program, string? systemDrive, string? drive)
    {
        Assert.Equal(drive, DataLocation.StorageDrive(program, systemDrive));
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
