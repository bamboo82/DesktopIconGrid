using DesktopIconGrid.Core.Sync;

namespace DesktopIconGrid.Core.Tests;

/// <summary>0.14.0 WP5: the folder files from the other PCs are saved in, and its rules.</summary>
public sealed class ReceiveFolderTests : IDisposable
{
    private const string Data = @"C:\Users\Bo\AppData\Local\DesktopIconGrid";
    private const string SyncFolder = @"D:\OneDrive\DIG";

    private readonly string _root = Directory.CreateTempSubdirectory("dig-receive-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Theory]
    [InlineData(null, null)]
    [InlineData("   ", null)]
    [InlineData(@"D:\Inbox", @"D:\Inbox")]
    [InlineData(@"  ""D:\My files""  ", @"D:\My files")]
    [InlineData(@"%APPDRIVE%\Inbox", @"E:\Inbox")]
    [InlineData(@"%USERPROFILE%\Received", @"C:\Users\Bo\Received")]
    public void The_setting_is_used_with_its_quotes_off_and_its_variables_expanded(string? setting, string? expected)
    {
        static string Expand(string text) => text.Replace("%APPDRIVE%", "E:", StringComparison.OrdinalIgnoreCase).Replace("%USERPROFILE%", @"C:\Users\Bo", StringComparison.OrdinalIgnoreCase);

        Assert.Equal(expected, ReceiveFolder.Expand(setting, Expand));
    }

    [Theory]
    [InlineData(@"D:\Inbox", ReceiveFolderProblem.None)]
    [InlineData(@"C:\Users\Bo\Downloads\From other PCs", ReceiveFolderProblem.None)]
    [InlineData(@"\\nas\share\inbox", ReceiveFolderProblem.None)]
    [InlineData(@"D:\OneDrive", ReceiveFolderProblem.None)] // around the sync folder, not in it
    [InlineData("files", ReceiveFolderProblem.NotAbsolute)]
    [InlineData(@"\files", ReceiveFolderProblem.NotAbsolute)]
    [InlineData(@"E:files", ReceiveFolderProblem.NotAbsolute)]
    [InlineData(@"\\nas", ReceiveFolderProblem.NotAbsolute)]
    [InlineData(@"%NOTSET%\files", ReceiveFolderProblem.NotAbsolute)]
    [InlineData(Data, ReceiveFolderProblem.InDataFolder)]
    [InlineData(@"c:\users\bo\appdata\local\desktopicongrid\icons\", ReceiveFolderProblem.InDataFolder)]
    [InlineData(SyncFolder, ReceiveFolderProblem.InSyncFolder)]
    [InlineData(@"D:/OneDrive/DIG/inbox", ReceiveFolderProblem.InSyncFolder)] // a received file would be sent on
    [InlineData(@"C:\Users\Bo\AppData\Local\DesktopIconGrid-old", ReceiveFolderProblem.None)] // a name that only starts the same
    public void A_name_is_taken_when_it_is_full_and_neither_in_the_data_nor_in_the_sync_folder(string folder, ReceiveFolderProblem expected) =>
        Assert.Equal(expected, ReceiveFolder.CheckName(folder, Data, SyncFolder));

    [Fact]
    public void A_name_longer_than_Windows_allows_for_a_folder_is_refused_and_one_at_the_limit_taken()
    {
        var atLimit = @"D:\" + new string('a', ReceiveFolder.MaxLength - 3);
        Assert.Equal(ReceiveFolderProblem.None, ReceiveFolder.CheckName(atLimit, Data, null));
        Assert.Equal(ReceiveFolderProblem.TooLong, ReceiveFolder.CheckName(atLimit + "a", Data, null));
        Assert.Equal(ReceiveFolderProblem.None, ReceiveFolder.CheckName(SyncFolder, Data, syncFolder: null)); // through Google Drive: no sync folder
    }

    [Fact]
    public void On_the_disk_a_missing_folder_is_made_and_a_file_or_a_folder_that_cannot_be_made_is_refused()
    {
        var made = Path.Combine(_root, "inbox", "from other PCs");
        Assert.Equal(ReceiveFolderProblem.None, ReceiveFolder.Check(made, Path.Combine(_root, "data"), null));
        Assert.True(Directory.Exists(made));

        var file = Path.Combine(_root, "a file");
        File.WriteAllText(file, "x");
        Assert.Equal(ReceiveFolderProblem.NotAFolder, ReceiveFolder.Check(file, Path.Combine(_root, "data"), null));
        Assert.Equal(ReceiveFolderProblem.CannotUse, ReceiveFolder.Check(Path.Combine(file, "under it"), Path.Combine(_root, "data"), null));
        Assert.Equal(ReceiveFolderProblem.InDataFolder, ReceiveFolder.Check(Path.Combine(_root, "data", "x"), Path.Combine(_root, "data"), null));
        Assert.False(Directory.Exists(Path.Combine(_root, "data"))); // refused by its name: nothing made
    }

    [Fact]
    public void The_setting_is_kept_trimmed_in_sync_json_and_empty_means_Downloads()
    {
        var settings = new SyncSettings { ReceiveFolder = "  " };
        settings.Normalize();
        Assert.Null(settings.ReceiveFolder);

        var store = new SyncSettingsStore(Path.Combine(_root, SyncSettingsStore.FileName));
        store.Save(new SyncSettings { ReceiveFolder = @" %APPDRIVE%\Inbox " });
        Assert.Equal(@"%APPDRIVE%\Inbox", store.Load().ReceiveFolder); // kept as written: expanded where it is used
        Assert.Contains("\"receiveFolder\"", File.ReadAllText(store.FilePath), StringComparison.Ordinal);

        store.Save(new SyncSettings());
        Assert.DoesNotContain("receiveFolder", File.ReadAllText(store.FilePath), StringComparison.Ordinal); // not written for Downloads
    }
}
