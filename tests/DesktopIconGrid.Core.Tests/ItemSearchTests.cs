namespace DesktopIconGrid.Core.Tests;

/// <summary>Type-to-find over the bar's items.</summary>
public class ItemSearchTests
{
    private static SearchCandidate Item(string name, string? original = null, string? file = null) => new(Guid.NewGuid(), name, original, file);

    [Theory]
    [InlineData("Visual Studio Code", "VSC")]
    [InlineData("WinSCP", "WS")] // a run of capitals is one word
    [InlineData("my-tool_v2", "mtv2")]
    [InlineData("Notepad++", "N")]
    [InlineData("檔案總管", "檔案總管")]
    [InlineData("", "")]
    public void Initials_take_the_first_letter_of_every_word(string name, string initials)
    {
        Assert.Equal(initials, ItemSearch.Initials(name));
    }

    [Fact]
    public void The_given_name_ranks_whole_then_start_then_word_then_initials_then_anywhere()
    {
        var exact = Item("Code");
        var start = Item("Codewriter");
        var word = Item("Visual Code");
        var initials = Item("Chrome Old Desktop Edition"); // C-O-D-E
        var anywhere = Item("Barcode scanner");
        var none = Item("Paint");

        var ranked = ItemSearch.Rank("code", [none, anywhere, initials, word, start, exact]);

        Assert.Equal([exact.Id, start.Id, word.Id, initials.Id, anywhere.Id], ranked);
    }

    [Fact]
    public void Word_initials_find_programs_by_their_abbreviation()
    {
        var vscode = Item("Visual Studio Code");
        var vs = Item("Visual Studio 2022");

        Assert.Equal([vscode.Id], ItemSearch.Rank("vsc", [vs, vscode]));
        Assert.Equal([vs.Id, vscode.Id], ItemSearch.Rank("vis", [vs, vscode]));
    }

    [Fact]
    public void The_original_name_and_the_file_name_are_searched_after_the_given_name()
    {
        var renamed = Item("Work editor", original: "Notepad++", file: "notepad++");
        var byFile = Item("Terminal", original: "Windows Terminal", file: "wt");
        var direct = Item("Notepad");

        Assert.Equal([direct.Id, renamed.Id], ItemSearch.Rank("notepad", [renamed, byFile, direct]));
        Assert.Equal([byFile.Id], ItemSearch.Rank("wt", [renamed, byFile, direct]));
    }

    [Fact]
    public void Chinese_is_matched_as_typed_and_case_and_width_are_ignored()
    {
        var explorer = Item("檔案總管");
        var transfer = Item("檔案傳輸 (WinSCP)");
        var fullWidth = Item("ＷｉｎＳＣＰ");

        Assert.Equal([explorer.Id, transfer.Id], ItemSearch.Rank("檔案", [explorer, transfer, fullWidth]));
        Assert.Equal([explorer.Id], ItemSearch.Rank("總管", [explorer, transfer]));
        Assert.Equal([fullWidth.Id, transfer.Id], ItemSearch.Rank("winscp", [transfer, fullWidth]));
    }

    [Fact]
    public void An_empty_query_matches_nothing_and_equal_matches_keep_the_bar_s_order()
    {
        var first = Item("Mail one");
        var second = Item("Mail two");

        Assert.Empty(ItemSearch.Rank("  ", [first, second]));
        Assert.Equal([first.Id, second.Id], ItemSearch.Rank("mail", [first, second]));
        Assert.Equal([second.Id, first.Id], ItemSearch.Rank("mail", [second, first]));
    }
}
