using DesktopIconGrid.Core.Storage;

namespace DesktopIconGrid.Core.Tests;

/// <summary>Portable mode: data next to the program, and shortcuts that follow the program's drive letter.</summary>
public class DataLocationTests
{
    [Theory]
    [InlineData(@"%APPDRIVE%\Windows\System32\notepad.exe", @"C:\Windows\System32\notepad.exe")] // 0.8.0 on C: wrote it
    [InlineData(@"%appdrive%\Program Files\App\app.exe", @"C:\Program Files\App\app.exe")]
    [InlineData(@"%APPDRIVE%\Users\me\Documents", @"C:\Users\me\Documents")]
    [InlineData(@"%APPDRIVE%\PortableApps\x.exe", @"%APPDRIVE%\PortableApps\x.exe")] // a stick's own: stays
    [InlineData(@"%APPDRIVE%\WindowsApps\x.exe", @"%APPDRIVE%\WindowsApps\x.exe")]
    [InlineData(@"D:\Windows\x.exe", @"D:\Windows\x.exe")]
    [InlineData(null, null)]
    public void Paths_of_0_8_0_under_Windows_folders_get_Windows_drive_back(string? stored, string? healed)
    {
        // Review 15 R4: a 0.8.0 copy on C: stored %APPDRIVE%\Windows\…, which a copy on a stick reads as the stick's.
        Assert.Equal(healed, DataLocation.WithWindowsDrive(stored, "C:"));
        Assert.Equal(stored, DataLocation.WithWindowsDrive(stored, null)); // off Windows: nothing to go by
    }

    [Fact]
    public void Portable_txt_next_to_the_program_keeps_the_data_there()
    {
        var program = Path.Combine("E:", "Tools", "DesktopIconGrid") + Path.DirectorySeparatorChar;
        var local = Path.Combine("C:", "Users", "me", "AppData", "Local");

        var portable = Path.TrimEndingDirectorySeparator(program);
        Assert.Equal(new DataPlace(portable, true, false, portable), DataLocation.Resolve(program, local, path => path.EndsWith(DataLocation.PortableMarker, StringComparison.Ordinal)));
        Assert.Equal(new DataPlace(Path.Combine(local, LayoutStorage.ApplicationFolderName), false, false, null), DataLocation.Resolve(program, local, _ => false));
    }

    [Fact]
    public void A_portable_folder_that_cannot_be_written_leaves_the_data_in_LocalAppData_and_says_so()
    {
        // The review's R7: portable.txt under Program Files without elevation, or on a read-only drive, stopped the app.
        var program = Path.Combine("C:", "Program Files", "DesktopIconGrid");
        var local = Path.Combine("C:", "Users", "me", "AppData", "Local");

        var resolved = DataLocation.Resolve(program, local, path => path.EndsWith(DataLocation.PortableMarker, StringComparison.Ordinal), canWrite: _ => false);

        Assert.Equal(new DataPlace(Path.Combine(local, LayoutStorage.ApplicationFolderName), false, true, program), resolved);
    }

    // ----- 0.13.0 WP1: portable.txt names the data folder ---------------------------------------------------------------

    private const string Program = @"E:\Tools\DesktopIconGrid\";
    private const string Local = @"C:\Users\me\AppData\Local";
    private static readonly string ThisPc = Path.Combine(Local, LayoutStorage.ApplicationFolderName);

    /// <summary>Resolves with <paramref name="marker"/> as the text of portable.txt, every folder made and written unless said.</summary>
    private static DataPlace With(string marker, Func<string, bool>? usable = null) =>
        DataLocation.Resolve(
            Program,
            Local,
            path => path.EndsWith(DataLocation.PortableMarker, StringComparison.Ordinal),
            canWrite: usable ?? (_ => true),
            readMarker: _ => marker,
            makeFolder: _ => true,
            expand: value => value.Replace("%USERPROFILE%", @"C:\Users\me", StringComparison.OrdinalIgnoreCase));

    [Theory]
    [InlineData("", @"E:\Tools\DesktopIconGrid")] // an empty file: the program's folder, as before 0.13.0
    [InlineData(".", @"E:\Tools\DesktopIconGrid")]
    [InlineData("\r\n  \r\n.\r\nsecond line", @"E:\Tools\DesktopIconGrid")] // the first line with text in it
    [InlineData("data", @"E:\Tools\DesktopIconGrid\data")]
    [InlineData(@"..\DIG-data", @"E:\Tools\DIG-data")]
    [InlineData(@".\data\.\bar\", @"E:\Tools\DesktopIconGrid\data\bar")]
    [InlineData("data/bar", @"E:\Tools\DesktopIconGrid\data\bar")]
    [InlineData(@"\DIG-data", @"E:\DIG-data")] // on the program's drive
    [InlineData(@"D:\Bar data", @"D:\Bar data")]
    [InlineData(@"d:\", @"D:\")]
    [InlineData("\"D:\\Bar data\"", @"D:\Bar data")] // in quotes
    [InlineData(@"%APPDRIVE%\DIG-data", @"E:\DIG-data")]
    [InlineData(@"%UserProfile%\DIG", @"C:\Users\me\DIG")]
    [InlineData(@"\\nas\share\DIG", @"\\nas\share\DIG")]
    [InlineData(@"..\..\..\..\up", @"E:\up")] // never above the root
    [InlineData("100% mine", @"E:\Tools\DesktopIconGrid\100% mine")] // a lone % is part of a name
    [InlineData("\uFEFFdata", @"E:\Tools\DesktopIconGrid\data")] // a byte order mark read as a character
    public void Portable_txt_names_the_data_folder(string marker, string folder)
    {
        Assert.Equal(new DataPlace(folder, true, false, folder), With(marker));
    }

    [Theory]
    [InlineData("E:data")] // relative to a folder Windows keeps per drive
    [InlineData(@"D:\a|b")]
    [InlineData(@"D:\what?")]
    [InlineData(@"D:\x\file.txt:stream")]
    [InlineData(@"%NOT_SET_HERE%\data")]
    [InlineData(@"\\nas")] // a server, no share
    public void A_line_that_names_no_folder_leaves_the_data_in_LocalAppData_and_names_the_line(string marker)
    {
        Assert.Equal(new DataPlace(ThisPc, false, true, marker), With(marker));
    }

    [Fact]
    public void Too_long_a_name_a_NUL_or_too_long_a_line_names_no_folder()
    {
        var deep = @"D:\" + string.Join('\\', Enumerable.Repeat("folder", 40)); // over 248 in full
        Assert.True(deep.Length > DataLocation.MaxFolderLength);
        Assert.Equal(new DataPlace(ThisPc, false, true, deep[..119] + "…"), With(deep));

        Assert.True(With("da\0ta").Unusable);
        Assert.Equal("da ta", With("da\0ta").Named); // shown without the control character
        Assert.True(With(new string('x', DataLocation.MaxLineLength + 1)).Unusable);
        Assert.Null(DataLocation.FirstLine("x\0y"));
        Assert.Equal(string.Empty, DataLocation.FirstLine(null));
        Assert.Equal("data", DataLocation.FirstLine("\r\n \r\n data \r\nsecond line"));
    }

    [Fact]
    public void A_folder_that_cannot_be_made_or_written_leaves_the_data_in_LocalAppData_and_names_the_folder()
    {
        Assert.Equal(new DataPlace(ThisPc, false, true, @"D:\Read only"), With(@"D:\Read only", usable: _ => false));

        var notMade = DataLocation.Resolve(Program, Local, _ => true, canWrite: _ => true, readMarker: _ => "data", makeFolder: _ => false);
        Assert.Equal(new DataPlace(ThisPc, false, true, @"E:\Tools\DesktopIconGrid\data"), notMade);
    }

    [Fact]
    public void A_note_in_portable_txt_leaves_the_bar_in_the_program_folder_while_only_that_folder_has_one()
    {
        // Review 33 R1: a copy's portable.txt had said "portable copy for the in-app check" since 0.8; 0.13.0 read it as a
        // folder, and the bar came up empty there.
        const string program = @"E:\Tools\DesktopIconGrid";
        const string note = "portable copy for the in-app check";
        const string named = @"E:\Tools\DesktopIconGrid\portable copy for the in-app check";
        var made = new List<string>();
        DataPlace With(string marker, params string[] bars) => DataLocation.Resolve(
            Program,
            Local,
            path => path.EndsWith(DataLocation.PortableMarker, StringComparison.Ordinal),
            canWrite: _ => true,
            readMarker: _ => marker,
            makeFolder: folder =>
            {
                made.Add(folder);
                return true;
            },
            hasBar: folder => bars.Any(bar => DataLocation.SameFolder(bar, folder)));

        Assert.Equal(new DataPlace(program, true, false, named, KeptInProgramFolder: true), With(note, program));
        Assert.Empty(made); // nothing is made for the note

        // A line that names no folder at all: the same.
        Assert.Equal(new DataPlace(program, true, false, "copy: test", KeptInProgramFolder: true), With("copy: test", program));

        // The folder named has a bar (moved there in Settings): it is used.
        Assert.Equal(new DataPlace(named, true, false, named), With(note, program, named));

        // A new copy, no bar anywhere: the folder named, made.
        made.Clear();
        Assert.Equal(new DataPlace(named, true, false, named), With(note));
        Assert.Equal([named], made);

        // "." is the program's folder itself; a program folder that cannot be written sends the data to LocalAppData.
        Assert.Equal(new DataPlace(program, true, false, program), With(".", program));
        var readOnly = DataLocation.Resolve(Program, Local, _ => true, canWrite: _ => false, readMarker: _ => note, makeFolder: _ => true, hasBar: folder => DataLocation.SameFolder(folder, program));
        Assert.Equal(new DataPlace(ThisPc, false, true, program), readOnly);
    }

    [Fact]
    public void A_program_on_a_share_has_no_drive_for_APPDRIVE_or_a_name_from_the_root()
    {
        const string share = @"\\nas\tools\DIG";
        Assert.True(DataLocation.Resolve(share, Local, _ => true, readMarker: _ => @"%APPDRIVE%\data").Unusable);
        Assert.Null(DataLocation.FolderOf(@"\data", share, value => value));
        Assert.Equal(@"\\nas\tools\DIG\data", DataLocation.FolderOf("data", share, value => value));
    }

    [Theory]
    [InlineData(@"E:\Tools\DesktopIconGrid", ".")]
    [InlineData(@"E:\Tools\DesktopIconGrid\", ".")]
    [InlineData(@"e:\tools\desktopicongrid\Data", "Data")]
    [InlineData(@"E:\Tools\DesktopIconGrid\data\bar", @"data\bar")]
    [InlineData(@"E:\Tools\DesktopIconGrid-data", @"E:\Tools\DesktopIconGrid-data")] // a neighbour, not under it
    [InlineData(@"D:\Bar", @"D:\Bar")]
    public void A_folder_picked_with_Browse_is_written_relative_under_the_program_else_in_full(string picked, string written)
    {
        Assert.Equal(written, DataLocation.AsTyped(picked, Program));
    }

    [Fact]
    public void Folders_are_the_same_whatever_their_case_and_ending()
    {
        Assert.True(DataLocation.SameFolder(@"C:\Users\me\AppData\Local\DesktopIconGrid", @"c:\users\ME\appdata\local\desktopicongrid\"));
        Assert.False(DataLocation.SameFolder(@"E:\DIG", @"E:\DIG-data"));
        Assert.False(DataLocation.SameFolder(null, @"E:\DIG"));
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
    public void The_program_drive_replaces_an_inherited_APPDRIVE_and_a_copy_on_a_share_clears_it()
    {
        // Review 17: a UNC copy started from a stick's bar kept the stick's %APPDRIVE%, and expanded paths as the stick.
        Environment.SetEnvironmentVariable(DataLocation.AppDriveVariable, "Q:");
        try
        {
            DataLocation.PublishProgramDrive();

            Assert.Equal(DataLocation.DriveOf(AppContext.BaseDirectory), Environment.GetEnvironmentVariable(DataLocation.AppDriveVariable));
        }
        finally
        {
            Environment.SetEnvironmentVariable(DataLocation.AppDriveVariable, null);
        }
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
