using DesktopIconGrid.Core.Diagnostics;
using DesktopIconGrid.Services;

namespace DesktopIconGrid.App.Tests;

/// <summary>The release check of 0.11.0: a notice that reports no failure is information in app.log, not a warning.</summary>
public class NoticeLogTests
{
    [Fact]
    public void A_notice_of_no_failure_is_logged_as_information_and_one_of_a_failure_as_a_warning()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var dialogs = new DialogService(new NoIcons()) { Notifier = (_, _) => true }; // shown as a balloon: no box
        var mark = Guid.NewGuid().ToString("N"); // the log is shared with tests running at the same time

        dialogs.Inform("File from Den", $"{mark} is in your Downloads folder");
        dialogs.Notify("Could not open Tool", $"{mark} was not found");

        var entries = AppLog.Default.Snapshot().Where(entry => entry.Message.Contains(mark, StringComparison.Ordinal)).ToList();
        Assert.Equal(LogLevel.Info, entries.Single(entry => entry.Message.Contains("Downloads", StringComparison.Ordinal)).Level);
        Assert.Equal(LogLevel.Warning, entries.Single(entry => entry.Message.Contains("not found", StringComparison.Ordinal)).Level);
    }
}
