using DesktopIconGrid.Core.Sync;

namespace DesktopIconGrid.Core.Tests;

/// <summary>Sending a file to another PC through the sync folder.</summary>
[Collection("This device")] // one test sets ThisDevice.Id, which is process-wide
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
        var (received, marked, cleared) = FolderInbox.Receive(waiting, downloads);
        Assert.False(marked); // no marker given
        Assert.True(cleared);

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
        var received = FolderInbox.Receive(waiting with { Name = @"..\..\evil.exe" }, Path.Combine(_folder, "Downloads")).Path;
        Assert.Equal(Path.Combine(_folder, "Downloads"), Path.GetDirectoryName(received));
    }

    [Fact]
    public void Sending_needs_a_real_other_PC_and_a_file()
    {
        var laptop = new FolderInbox(Sync, Laptop, "Laptop");
        laptop.Announce();
        var file = MakeFile("a.txt");

        Assert.Equal(SendProblem.NotAnotherPc, Assert.Throws<InboxSendException>(() => laptop.Send(Laptop, file)).Problem); // itself
        Assert.Equal(SendProblem.NoInbox, Assert.Throws<InboxSendException>(() => laptop.Send(Desktop, file)).Problem);
        Assert.Equal(SendProblem.NotAnotherPc, Assert.Throws<InboxSendException>(() => laptop.Send(@"..\..\x", file)).Problem);
        new FolderInbox(Sync, Desktop, "Desktop").Announce();
        Assert.Equal(SendProblem.NotAFile, Assert.Throws<InboxSendException>(() => laptop.Send(Desktop, _folder)).Problem); // a folder
        Assert.Throws<ArgumentException>(() => new FolderInbox(Sync, "short", "x"));

        laptop.Send(Desktop, file);
        laptop.Send(Desktop, file); // twice: both kept
        Assert.Equal(2, new FolderInbox(Sync, Desktop, "Desktop").Pending().Count);
    }

    [Fact]
    public void A_PC_that_takes_its_name_back_is_no_longer_offered_or_sent_to_and_its_waiting_files_stay()
    {
        // Review 27 S1.
        var laptop = new FolderInbox(Sync, Laptop, "Laptop");
        var desktop = new FolderInbox(Sync, Desktop, "Desktop");
        desktop.Announce();
        laptop.Send(Desktop, MakeFile("a.txt"));

        Assert.True(desktop.Withdraw());
        Assert.False(desktop.Withdraw()); // nothing left to take back

        Assert.Empty(laptop.Devices());
        Assert.Equal(SendProblem.NoInbox, Assert.Throws<InboxSendException>(() => laptop.Send(Desktop, MakeFile("b.txt"))).Problem);
        Assert.Equal("a.txt", Assert.Single(desktop.Pending()).Name); // taken when it receives again
        Assert.False(new FolderInbox(Path.Combine(_folder, "elsewhere"), Laptop, "Laptop").Withdraw()); // no inbox at all

        desktop.Announce();
        Assert.Equal([new InboxDevice(Desktop, "Desktop")], laptop.Devices());
    }

    [Fact]
    public void A_folder_without_a_name_file_is_not_a_PC_to_send_to()
    {
        // An inbox left by a PC that never said its name, or by an earlier version that made the folder early.
        Directory.CreateDirectory(Path.Combine(Sync, FolderInbox.FolderName, Desktop));
        var laptop = new FolderInbox(Sync, Laptop, "Laptop");

        Assert.Empty(laptop.Devices());
        Assert.Equal(SendProblem.NoInbox, Assert.Throws<InboxSendException>(() => laptop.Send(Desktop, MakeFile("a.txt"))).Problem);
    }

    [Fact]
    public void A_read_only_hidden_file_arrives_once_as_a_plain_marked_file()
    {
        // Review 24 R1: copy-then-delete left a read-only file unmarked, unannounced, and delivered again every minute.
        var laptop = new FolderInbox(Sync, Laptop, "Laptop");
        var desktop = new FolderInbox(Sync, Desktop, "Desktop");
        laptop.Announce();
        desktop.Announce();
        var source = MakeFile("tool.exe");
        File.SetAttributes(source, FileAttributes.ReadOnly | FileAttributes.Hidden);
        laptop.Send(Desktop, source);
        var waiting = Assert.Single(desktop.Pending());
        File.SetAttributes(waiting.Path, FileAttributes.ReadOnly | FileAttributes.Hidden); // as a hostile writer would leave it
        var downloads = Path.Combine(_folder, "Downloads");
        var markedWhile = new List<(string Path, FileAttributes Attributes)>();

        var (path, marked, cleared) = FolderInbox.Receive(waiting, downloads, arriving =>
        {
            markedWhile.Add((arriving, File.GetAttributes(arriving)));
            return true;
        });

        Assert.True(marked);
        Assert.True(cleared);
        var seen = Assert.Single(markedWhile);
        Assert.NotEqual(path, seen.Path); // marked under its temporary name, before it had its real one
        Assert.False(seen.Attributes.HasFlag(FileAttributes.ReadOnly));
        Assert.False(File.GetAttributes(path).HasFlag(FileAttributes.ReadOnly));
        Assert.False(File.GetAttributes(path).HasFlag(FileAttributes.Hidden));
        Assert.Empty(desktop.Pending()); // not delivered again
        Assert.Equal(["tool.exe"], Directory.GetFiles(downloads).Select(Path.GetFileName));
        File.SetAttributes(source, FileAttributes.Normal);
    }

    [Fact]
    public void A_file_called_desktop_ini_does_not_become_the_folders_own()
    {
        var desktop = new FolderInbox(Sync, Desktop, "Desktop");
        desktop.Announce();
        File.WriteAllText(Path.Combine(Sync, FolderInbox.FolderName, Desktop, $"{Laptop}~Desktop.INI"), "[.ShellClassInfo]");

        var waiting = Assert.Single(desktop.Pending());

        Assert.Equal("Desktop.INI.received", waiting.Name);
        Assert.EndsWith("Desktop.INI.received", FolderInbox.Receive(waiting, Path.Combine(_folder, "Downloads")).Path);
    }

    [Fact]
    public void A_delivery_that_fails_leaves_nothing_half_arrived_and_skipped_files_use_no_places()
    {
        var laptop = new FolderInbox(Sync, Laptop, "Laptop");
        var desktop = new FolderInbox(Sync, Desktop, "Desktop");
        laptop.Announce();
        desktop.Announce();
        laptop.Send(Desktop, MakeFile("a.txt"));
        var waiting = Assert.Single(desktop.Pending());
        var downloads = Path.Combine(_folder, "Downloads");

        Assert.Throws<IOException>(() => FolderInbox.Receive(waiting, downloads, _ => throw new IOException("the scanner holds it")));

        Assert.Empty(Directory.GetFiles(downloads)); // no a.txt.part left: the next look does not make "a (2).txt"
        Assert.Equal("a.txt", Path.GetFileName(FolderInbox.Receive(Assert.Single(desktop.Pending()), downloads).Path));

        // Review 25 L3: files that cannot be removed are passed over before the cap, so newer ones still arrive.
        for (var i = 0; i < FolderInbox.MaxPerLook + 3; i++)
        {
            laptop.Send(Desktop, MakeFile($"f{i:00}.txt"));
        }

        var stuck = desktop.Pending().Select(file => file.Path).ToHashSet();
        Assert.Equal(FolderInbox.MaxPerLook, stuck.Count);
        Assert.Equal(3, desktop.Pending(stuck.Contains).Count);
    }

    [Theory]
    [InlineData("movie.mkv.part")]
    [InlineData("notes.TAKEN")]
    public void A_file_named_like_the_inboxs_own_states_still_arrives(string name)
    {
        var laptop = new FolderInbox(Sync, Laptop, "Laptop");
        var desktop = new FolderInbox(Sync, Desktop, "Desktop");
        laptop.Announce();
        desktop.Announce();

        Assert.Equal(name + "_", laptop.Send(Desktop, MakeFile(name)));

        Assert.Equal(name + "_", Assert.Single(desktop.Pending()).Name);
    }

    [Theory]
    [InlineData(true, false, false, false)] // updated to 0.10.0 with a sync folder: nothing changes by itself
    [InlineData(true, true, false, true)] // the setting
    [InlineData(true, false, true, true)] // a Send to PC icon on the bar
    [InlineData(false, true, true, false)] // no sync folder: nowhere to receive from
    public void A_PC_takes_part_only_when_its_user_asked(bool folder, bool setting, bool icon, bool takesPart) =>
        Assert.Equal(takesPart, FolderInbox.TakesPart(folder, setting, icon));

    [Fact]
    public void The_receive_setting_is_off_by_default_and_the_bar_knows_its_Send_to_PC_icon()
    {
        Assert.False(new SyncSettings().ReceiveFiles);

        var model = new LauncherModel();
        Assert.False(model.HasSpecialIcon(DesktopIconGrid.Core.SpecialIcons.SpecialIconKinds.SendToDevice));
        var group = new DesktopIconGrid.Core.Models.GroupItem { Name = "G" };
        model.Add(group);
        model.Insert(new DesktopIconGrid.Core.Models.SpecialIconItem { Kind = "send-to-device" }, group, 0);
        Assert.True(model.HasSpecialIcon(DesktopIconGrid.Core.SpecialIcons.SpecialIconKinds.SendToDevice)); // inside a group counts
    }

    [Fact]
    public void Only_a_Send_to_PC_icon_marked_on_this_PC_counts()
    {
        var before = DesktopIconGrid.Core.Models.ThisDevice.Id;
        try
        {
            var addedHere = new DesktopIconGrid.Core.Models.SpecialIconItem { Kind = "send-to-device" };
            var arrived = new DesktopIconGrid.Core.Models.SpecialIconItem { Kind = "send-to-device" };
            var model = new LauncherModel();
            model.Add(arrived);
            var trust = new DesktopIconGrid.Core.SpecialIcons.CommandTrust(null);
            bool HasIconOfThisPc() => model.SpecialIconsOf("send-to-device").Any(icon => trust.IsApproved(FolderInbox.IconMark(icon.Id)));

            DesktopIconGrid.Core.Models.ThisDevice.Id = "pc-a";
            Assert.False(HasIconOfThisPc()); // it came with the synced bar

            model.Add(addedHere);
            trust.Approve(FolderInbox.IconMark(addedHere.Id)); // its properties were saved here
            Assert.True(HasIconOfThisPc());
            Assert.Equal(64, FolderInbox.IconMark(addedHere.Id).Length);
            Assert.NotEqual(FolderInbox.IconMark(addedHere.Id), FolderInbox.IconMark(arrived.Id));

            DesktopIconGrid.Core.Models.ThisDevice.Id = "pc-b"; // the same bar on another PC
            Assert.False(HasIconOfThisPc());
        }
        finally
        {
            DesktopIconGrid.Core.Models.ThisDevice.Id = before;
        }
    }

    [Fact]
    public void Receiving_keeps_a_gigabyte_free_and_names_come_from_the_PC_itself()
    {
        const long gb = 1024L * 1024 * 1024;
        Assert.True(FolderInbox.HasRoom(3 * gb, 2 * gb));
        Assert.False(FolderInbox.HasRoom(3 * gb, 2 * gb + 1));
        Assert.False(FolderInbox.HasRoom(gb / 2, 1));

        var laptop = new FolderInbox(Sync, Laptop, "Laptop");
        new FolderInbox(Sync, Desktop, "Den PC").Announce();

        Assert.Equal("Den PC", laptop.NameOfDevice(Desktop, "an old‮ name")); // what the PC says now
        Assert.Equal("an old name", laptop.NameOfDevice(new string('c', 32), "an old‮ name")); // no inbox: the stored one, cleaned
        Assert.Equal("cccccccc", laptop.NameOfDevice(new string('c', 32), null));
    }

    [Theory]
    [InlineData("site.url", "site.url.received")]
    [InlineData("Tool.LNK", "Tool.LNK.received")]
    [InlineData("x.scf", "x.scf.received")]
    [InlineData("Docs.library-ms", "Docs.library-ms.received")]
    [InlineData("s.searchConnector-ms", "s.searchConnector-ms.received")]
    [InlineData("report.pdf", "report.pdf")]
    [InlineData("setup.exe", "setup.exe")] // not read on sight; the mark of a downloaded file guards opening it
    [InlineData("notes.url.txt", "notes.url.txt")]
    public void A_file_Explorer_would_follow_on_sight_arrives_as_a_plain_file(string sent, string arrives)
    {
        Assert.Equal(arrives, FolderInbox.PlainName(sent));

        var laptop = new FolderInbox(Sync, Laptop, "Laptop");
        var desktop = new FolderInbox(Sync, Desktop, "Desktop");
        laptop.Announce();
        desktop.Announce();
        laptop.Send(Desktop, MakeFile(sent, "[InternetShortcut]\nURL=file://evil/share"));

        var received = FolderInbox.Receive(Assert.Single(desktop.Pending()), Path.Combine(_folder, "Downloads")).Path;

        Assert.Equal(arrives, Path.GetFileName(received));
    }
}
