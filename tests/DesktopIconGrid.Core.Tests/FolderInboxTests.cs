using DesktopIconGrid.Core.Sync;

namespace DesktopIconGrid.Core.Tests;

/// <summary>Sending a file to another PC through the sync folder.</summary>
public sealed class FolderInboxTests : IDisposable
{
    private const string Laptop = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string Desktop = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    private readonly string _folder = Directory.CreateTempSubdirectory("dig-inbox-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string Sync => Path.Combine(_folder, "sync");

    private string MakeFile(string name, string text = "hello")
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllText(path, text);
        return path;
    }

    [Fact]
    public void A_file_sent_to_another_PC_arrives_in_its_downloads_and_leaves_the_inbox()
    {
        var laptop = new FolderInbox(Sync, Laptop, "Laptop");
        var desktop = new FolderInbox(Sync, Desktop, "Desktop‮ PC");
        laptop.Announce();
        desktop.Announce();

        Assert.Equal([new InboxDevice(Desktop, "Desktop PC")], laptop.Devices()); // not itself; the name cleaned
        Assert.Equal("report.pdf", laptop.Send(Desktop, MakeFile("report.pdf")));
        Assert.Empty(laptop.Pending());

        var waiting = Assert.Single(desktop.Pending());
        Assert.Equal(("report.pdf", Laptop, "Laptop", 5L), (waiting.Name, waiting.SenderId, waiting.SenderName, waiting.Size));

        var downloads = Path.Combine(_folder, "Downloads");
        Directory.CreateDirectory(downloads);
        File.WriteAllText(Path.Combine(downloads, "report.pdf"), "an older one");
        var received = FolderInbox.Receive(waiting, downloads);

        Assert.Equal(Path.Combine(downloads, "report (2).pdf"), received); // nothing is overwritten
        Assert.Equal("hello", File.ReadAllText(received));
        Assert.Empty(desktop.Pending());
    }

    [Fact]
    public void Only_what_the_app_put_there_is_taken_and_names_never_leave_the_folder()
    {
        var desktop = new FolderInbox(Sync, Desktop, "Desktop");
        desktop.Announce();
        var inbox = Path.Combine(Sync, FolderInbox.FolderName, Desktop);
        File.WriteAllText(Path.Combine(inbox, "stray.txt"), "x"); // no sender
        File.WriteAllText(Path.Combine(inbox, $"{Laptop}~half.zip.part"), "x"); // still arriving
        File.WriteAllText(Path.Combine(inbox, "not-an-id~x.txt"), "x");
        File.WriteAllText(Path.Combine(inbox, $"{Laptop}~CON"), "x"); // a reserved name

        var waiting = Assert.Single(desktop.Pending());

        Assert.Equal("CON_", waiting.Name);
        Assert.Equal(Laptop[..8], waiting.SenderName); // no device.json: the id's start
        var received = FolderInbox.Receive(waiting with { Name = @"..\..\evil.exe" }, Path.Combine(_folder, "Downloads"));
        Assert.Equal(Path.Combine(_folder, "Downloads"), Path.GetDirectoryName(received));
    }

    [Fact]
    public void Sending_needs_a_real_other_PC_and_a_file()
    {
        var laptop = new FolderInbox(Sync, Laptop, "Laptop");
        laptop.Announce();
        var file = MakeFile("a.txt");

        Assert.Throws<IOException>(() => laptop.Send(Laptop, file)); // itself
        Assert.Throws<IOException>(() => laptop.Send(Desktop, file)); // no such inbox
        Assert.Throws<IOException>(() => laptop.Send(@"..\..\x", file));
        new FolderInbox(Sync, Desktop, "Desktop").Announce();
        Assert.Throws<FileNotFoundException>(() => laptop.Send(Desktop, _folder)); // a folder
        Assert.Throws<ArgumentException>(() => new FolderInbox(Sync, "short", "x"));

        laptop.Send(Desktop, file);
        laptop.Send(Desktop, file); // twice: both kept
        Assert.Equal(2, new FolderInbox(Sync, Desktop, "Desktop").Pending().Count);
    }
}
