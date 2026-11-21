using System.Globalization;
using System.IO;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Core.SpecialIcons;
using DesktopIconGrid.Resources;
using DesktopIconGrid.Services;
using DesktopIconGrid.SpecialIcons;
using DesktopIconGrid.ViewModels;

namespace DesktopIconGrid.App.Tests;

/// <summary>0.12.0 WP1: what each notice is, and what a click on its balloon does.</summary>
public class NoticeTargetTests
{
    private static (NoticeActions Actions, List<string> Done) Actions(Func<string, bool>? fileExists = null, Func<string, bool>? directoryExists = null)
    {
        var done = new List<string>();
        var actions = new NoticeActions(
            () => done.Add("log"),
            () => done.Add("sync"),
            () => done.Add("settings"),
            () => done.Add("bar"),
            path => done.Add($"select {path}"),
            folder => done.Add($"open {folder}"))
        {
            FileExists = fileExists ?? (_ => false),
            DirectoryExists = directoryExists ?? (_ => false),
        };
        return (actions, done);
    }

    [Theory]
    [InlineData(NoticeTarget.Log, "log")]
    [InlineData(NoticeTarget.Sync, "sync")]
    [InlineData(NoticeTarget.Settings, "settings")]
    [InlineData(NoticeTarget.Bar, "bar")]
    public void A_click_goes_where_the_notice_says(NoticeTarget target, string done)
    {
        var (actions, log) = Actions();
        actions.Act(new Notice("T", "M", NoticeKind.Warning, target));
        Assert.Equal([done], log);
    }

    [Fact]
    public void A_received_file_is_selected_in_its_folder_and_nothing_happens_when_it_and_its_folder_are_gone()
    {
        var received = InboxService.ReceivedNotice(@"C:\Users\me\Downloads\report (2).pdf", "Den", marked: true);
        Assert.Equal((NoticeKind.Information, NoticeTarget.Folder, @"C:\Users\me\Downloads\report (2).pdf"), (received.Kind, received.Target, received.Path));
        Assert.Contains("report (2).pdf", received.Message, StringComparison.Ordinal);

        var (there, selected) = Actions(fileExists: path => path == received.Path);
        there.Act(received);
        Assert.Equal([$"select {received.Path}"], selected);

        var (moved, opened) = Actions(directoryExists: path => path == @"C:\Users\me\Downloads");
        moved.Act(received);
        Assert.Equal([@"open C:\Users\me\Downloads"], opened);

        var (gone, nothing) = Actions();
        gone.Act(received);
        Assert.Empty(nothing);

        var (none, quiet) = Actions();
        none.Act(Notice.Information("Sent", "3 files on the way"));
        Assert.Empty(quiet);
    }

    [Fact]
    public void Each_caller_gives_its_notice_the_kind_and_target_of_the_plan()
    {
        Assert.Equal((NoticeKind.Warning, NoticeTarget.Sync), Of(InboxService.WaitingNotice("big.iso", "Den", 5_000_000_000, 1_000_000)));
        Assert.Equal((NoticeKind.Information, NoticeTarget.Bar), Of(ClockSession.PhaseNotice("Tea", "The countdown has ended")));
        Assert.Equal((NoticeKind.Warning, NoticeTarget.Log), Of(HealthLight.ChangeNotice("NAS", up: false, "down")));
        Assert.Equal((NoticeKind.Information, NoticeTarget.Log), Of(HealthLight.ChangeNotice("NAS", up: true, "up again")));
        Assert.Equal((NoticeKind.Warning, NoticeTarget.Sync), Of(DriveBucketHandler.DriveProblem("Google Drive", "not signed in")));
    }

    private static (NoticeKind, NoticeTarget) Of(Notice notice) => (notice.Kind, notice.Target);

    [Fact]
    public void Files_received_in_one_look_make_one_notice_per_sender_and_one_file_is_as_before()
    {
        // Review of WP1, F1: twenty files in one look were twenty balloons, each replacing the last.
        (string, string, bool)[] received =
        [
            (@"C:\Users\me\Downloads\a.pdf", "Den", true),
            (@"C:\Users\me\Downloads\b (2).pdf", "Den", false),
            (@"C:\Users\me\Downloads\c.txt", "Den", true),
            (@"C:\Users\me\Downloads\d.png", "Study", true),
        ];

        var notices = InboxService.Notices(received, []);

        Assert.Equal(2, notices.Count);
        var den = notices[0];
        Assert.Equal((NoticeKind.Information, NoticeTarget.Folder, @"C:\Users\me\Downloads\a.pdf"), (den.Kind, den.Target, den.Path));
        Assert.Equal(string.Format(Strings.Inbox_ReceivedTitle, "Den"), den.Title);
        Assert.Equal(
            [string.Format(Strings.Inbox_ReceivedMany, 3), Strings.Inbox_NotMarked, "a.pdf", "b (2).pdf", "c.txt"],
            den.Message.Split('\n'));

        // One file from the other sender: exactly the notice of one file.
        Assert.Equal(InboxService.ReceivedNotice(@"C:\Users\me\Downloads\d.png", "Study", marked: true), notices[1]);

        // All marked: no line on the mark.
        var marked = InboxService.Notices([(@"C:\x\a.txt", "Den", true), (@"C:\x\b.txt", "Den", true)], []);
        Assert.Equal([string.Format(Strings.Inbox_ReceivedMany, 2), "a.txt", "b.txt"], Assert.Single(marked).Message.Split('\n'));
    }

    [Fact]
    public void Files_waiting_for_space_make_one_warning_per_sender_for_the_first_and_how_many_more()
    {
        var notices = InboxService.Notices(
            [],
            [("big.iso", "Den", 5_000_000_000, 1_000_000), ("bigger.iso", "Den", 6_000_000_000, 1_000_000), ("huge.iso", "Den", 7_000_000_000, 1_000_000), ("one.iso", "Study", 5_000_000_000, 1_000_000)]);

        Assert.Equal(2, notices.Count);
        var first = InboxService.WaitingNotice("big.iso", "Den", 5_000_000_000, 1_000_000);
        Assert.Equal(first with { Message = $"{first.Message}\n{string.Format(Strings.Inbox_AndMore, 2)}" }, notices[0]);
        Assert.Equal((NoticeKind.Warning, NoticeTarget.Sync), Of(notices[0]));
        Assert.Equal(InboxService.WaitingNotice("one.iso", "Study", 5_000_000_000, 1_000_000), notices[1]);
    }

    [Fact]
    public void Files_that_went_to_Downloads_because_the_chosen_folder_failed_end_with_one_warning_that_names_both()
    {
        // 0.14.0 WP5: a stick pulled out; the setting is kept, and the warning comes last so its balloon stays.
        var notices = InboxService.Notices([(@"C:\Users\me\Downloads\a.pdf", "Den", true), (@"C:\Users\me\Downloads\b.pdf", "Study", true)], [], fellBackFrom: @"E:\Inbox");

        Assert.Equal([NoticeKind.Information, NoticeKind.Information, NoticeKind.Warning], notices.Select(notice => notice.Kind));
        var warning = notices[2];
        Assert.Equal((Strings.Inbox_FolderFellBackTitle, NoticeTarget.Folder, @"C:\Users\me\Downloads\a.pdf"), (warning.Title, warning.Target, warning.Path));
        Assert.Equal([string.Format(Strings.Inbox_FolderFellBack, @"E:\Inbox", "a.pdf"), string.Format(Strings.Inbox_AndMore, 1)], warning.Message.Split('\n'));
        Assert.Equal(string.Format(Strings.Inbox_FolderFellBack, @"E:\Inbox", "c.txt"), InboxService.Notices([(@"C:\x\c.txt", "Den", true)], [], fellBackFrom: @"E:\Inbox")[1].Message);
        Assert.Empty(InboxService.Notices([], [], fellBackFrom: @"E:\Inbox")); // nothing received, nothing to say
    }

    [Fact]
    public void A_notice_names_the_folder_a_file_went_to_and_Downloads_by_its_word()
    {
        // 0.14.0 WP5: it was always "is in your Downloads folder".
        Assert.Equal(string.Format(Strings.Inbox_Received, "a.pdf", "From laptop"), InboxService.ReceivedNotice(@"E:\Inbox\From laptop\a.pdf", "Den", marked: true).Message);
        Assert.Equal(Strings.Inbox_Downloads, InboxService.FolderShownName(InboxService.DownloadsPath()));
        Assert.Equal(Strings.Inbox_Downloads, InboxService.FolderShownName(InboxService.DownloadsPath() + "\\"));
        Assert.Equal(Strings.Inbox_Downloads, InboxService.FolderShownName(null));
        Assert.Equal(@"E:\", InboxService.FolderShownName(@"E:\")); // a drive by its root
        Assert.Contains("Inbox", InboxService.WaitingNotice("big.iso", "Den", 5_000_000_000, 1_000_000, @"E:\Inbox").Message, StringComparison.Ordinal);
        Assert.Contains(Strings.Inbox_Downloads, InboxService.WaitingNotice("big.iso", "Den", 5_000_000_000, 1_000_000).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Received_files_come_first_and_the_warning_last_so_the_balloon_that_stays_is_the_warning()
    {
        // Release check U4: each balloon replaces the one before.
        var notices = InboxService.Notices(
            [(@"C:\x\a.txt", "Den", true), (@"C:\x\b.txt", "Study", true)],
            [("big.iso", "Den", 5_000_000_000, 1_000_000)]);

        Assert.Equal([NoticeKind.Information, NoticeKind.Information, NoticeKind.Warning], notices.Select(notice => notice.Kind));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("zh-Hant")]
    [InlineData("zh-Hans")]
    public void A_waiting_notice_with_a_long_name_and_the_more_line_fits_the_balloon(string language)
    {
        // Release check U2: a name of 60 characters, sizes of nine characters and "and 99 more" within the 255 a balloon
        // takes. The UI culture is the test thread's own.
        var before = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
        try
        {
            var name = new string('n', 56) + ".iso";
            const long size = 1_099_404_000_000;
            var waiting = Enumerable.Range(0, 100).Select(i => (i == 0 ? name : $"{i}.iso", "Den", size, size - 1)).ToList();

            var notice = Assert.Single(InboxService.Notices([], waiting));

            var shown = SystemMonitor.FormatBytes(size);
            Assert.Equal(9, shown.Length); // "1023.9 GB"
            Assert.Contains(shown, notice.Message, StringComparison.Ordinal);
            Assert.EndsWith(string.Format(Strings.Inbox_AndMore, 99), notice.Message, StringComparison.Ordinal);
            Assert.True(notice.Message.Length <= NoticeBalloon.MaxText, $"{language}: {notice.Message.Length} characters");
            Assert.Equal(notice.Message, NoticeBalloon.Text(notice.Message)); // not cut
        }
        finally
        {
            CultureInfo.CurrentUICulture = before;
        }
    }

    /// <summary>The receiving side without a sync folder or a Google sign-in.</summary>
    private sealed class NoReceiving : IReceiving
    {
        public Core.Sync.IInbox? Current() => null;

        public bool ReceiveSetting => false;

        public void Opened()
        {
        }

        public void Changed()
        {
        }
    }

    [Fact]
    public void Send_to_PC_without_a_sync_folder_points_to_the_Sync_window()
    {
        var dialogs = new RecordingDialogs();
        var handler = new SendToDeviceHandler(new NoReceiving(), dialogs, () => null, Path.GetTempPath(), new CommandTrust(null));

        // An existing icon's properties: nothing changes, the warning leads to where sync is set up.
        Assert.False(handler.ShowProperties(handler.CreateItem(), isNew: false));
        var notice = Assert.Single(dialogs.Shown);
        Assert.Equal((NoticeKind.Warning, NoticeTarget.Sync), Of(notice));

        // 0.13.0 WP5: a new icon is added all the same, and its notice leads there too.
        Assert.True(handler.ShowProperties(handler.CreateItem(), isNew: true));
        Assert.Equal((NoticeKind.Information, NoticeTarget.Sync), Of(dialogs.Shown[^1]));
        Assert.Equal(Strings.SendTo_AddedWithoutSync, dialogs.Shown[^1].Message);
    }

    [Fact]
    public void Notify_and_Inform_keep_their_defaults_and_Show_passes_a_notice_on_whole()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var shown = new List<Notice>();
        var dialogs = new DialogService(new NoIcons()) { Notifier = notice => { shown.Add(notice); return true; } };
        var received = new Notice("Received", "a.txt", NoticeKind.Information, NoticeTarget.Folder, @"C:\a.txt");

        dialogs.Notify("Could not open", "x");
        dialogs.Inform("Positions remembered", "y");
        dialogs.Show(received);

        Assert.Equal([Notice.Warning("Could not open", "x"), Notice.Information("Positions remembered", "y"), received], shown);
    }
}
