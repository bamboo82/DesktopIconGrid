using DesktopIconGrid.Core.Models;

namespace DesktopIconGrid.Core.Tests;

/// <summary>0.12.0 WP1: a notice's kind, its balloon, and where a click on it leads.</summary>
public class NoticeTests
{
    [Fact]
    public void A_warning_opens_the_log_and_information_leads_nowhere_by_default()
    {
        Assert.Equal(new Notice("T", "M", NoticeKind.Warning, NoticeTarget.Log), Notice.Warning("T", "M"));
        Assert.Equal(new Notice("T", "M", NoticeKind.Information, NoticeTarget.None), Notice.Information("T", "M"));
    }

    [Fact]
    public void The_balloon_has_the_icon_of_its_kind_and_information_waits_for_quiet_time_to_end()
    {
        Assert.Equal(0x01u | 0x80u, NoticeBalloon.Flags(NoticeKind.Information)); // NIIF_INFO | NIIF_RESPECT_QUIET_TIME
        Assert.Equal(0x02u, NoticeBalloon.Flags(NoticeKind.Warning)); // NIIF_WARNING, shown at once as before
    }

    [Fact]
    public void A_long_text_is_cut_at_a_line_end_in_its_last_80_characters_else_at_255()
    {
        Assert.Equal("short", NoticeBalloon.Text("short"));
        var exact = new string('x', NoticeBalloon.MaxText);
        Assert.Same(exact, NoticeBalloon.Text(exact));

        // A list of failures: whole lines, then "…" on a line of its own.
        var lines = string.Join("\r\n", Enumerable.Range(1, 20).Select(i => $"file {i:00}.txt: could not be sent"));
        var cut = NoticeBalloon.Text(lines);
        Assert.True(cut.Length <= NoticeBalloon.MaxText, $"{cut.Length} characters");
        Assert.EndsWith("could not be sent\n…", cut, StringComparison.Ordinal);
        Assert.DoesNotContain("\r\n…", cut, StringComparison.Ordinal);

        // No line end in the last 80 characters kept: cut at 254 and "…".
        var prose = "first line\n" + new string('y', 400);
        var plain = NoticeBalloon.Text(prose);
        Assert.Equal(NoticeBalloon.MaxText, plain.Length);
        Assert.EndsWith("y…", plain, StringComparison.Ordinal);

        // Never half a surrogate pair at the cut.
        var emoji = new string('z', 253) + "😀" + new string('z', 10);
        var safe = NoticeBalloon.Text(emoji);
        Assert.False(char.IsHighSurrogate(safe[^2]));
        Assert.Equal(new string('z', 253) + "…", safe);
    }

    [Fact]
    public void A_click_acts_on_the_last_notice_shown_once()
    {
        var memory = new NoticeMemory();
        Assert.Null(memory.Clicked()); // a click before any notice

        memory.Shown(Notice.Information("old", "x"));
        var received = new Notice("Received", "a.txt", NoticeKind.Information, NoticeTarget.Folder, @"C:\a.txt");
        memory.Shown(received);

        Assert.Equal(received, memory.Clicked());
        Assert.Null(memory.Clicked()); // forgotten after one click
    }

    [Fact]
    public void A_folder_notice_selects_the_file_else_opens_its_folder_and_does_nothing_when_both_are_gone()
    {
        var file = Path.Combine("downloads", "a.txt");
        bool Has(string path, params string[] there) => there.Contains(path);

        Assert.Equal((file, (string?)null), NoticeFolder.Resolve(file, path => Has(path, file), path => Has(path, "downloads")));
        Assert.Equal(((string?)null, "downloads"), NoticeFolder.Resolve(file, _ => false, path => Has(path, "downloads"))); // the file moved
        Assert.Equal(((string?)null, "downloads"), NoticeFolder.Resolve("downloads", _ => false, path => Has(path, "downloads")));
        Assert.Equal(((string?)null, (string?)null), NoticeFolder.Resolve(file, _ => false, _ => false)); // both gone: nothing
        Assert.Equal(((string?)null, (string?)null), NoticeFolder.Resolve(null, _ => true, _ => true));
        Assert.Equal(((string?)null, (string?)null), NoticeFolder.Resolve(" ", _ => true, _ => true));
    }
}
