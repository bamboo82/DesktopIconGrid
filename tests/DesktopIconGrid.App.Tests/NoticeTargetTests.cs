using System.IO;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Core.SpecialIcons;
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

        Assert.False(handler.ShowProperties(handler.CreateItem(), isNew: true));

        var notice = Assert.Single(dialogs.Shown);
        Assert.Equal((NoticeKind.Warning, NoticeTarget.Sync), Of(notice));
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
