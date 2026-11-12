using DesktopIconGrid.Core.Commands;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Core.SpecialIcons;
using DesktopIconGrid.Core.Voice;

namespace DesktopIconGrid.Core.Tests;

/// <summary>Voice launch: which phrases are listened for, what each starts, and what is done with what was heard.</summary>
public class VoiceVocabularyTests
{
    private static readonly ShortcutItem Notepad = new() { Name = "記事本", Location = @"C:\Windows\notepad.exe" };
    private static readonly ShortcutItem WinScp = new() { Name = "WinSCP", Location = @"C:\Tools\WinSCP.exe", AlsoSay = "檔案傳輸; win S C P" };
    private static readonly ShortcutItem Calculator = new() { Name = "小算盤", Location = "calc.exe", Workspaces = ["Morning"] };

    private static VoiceVocabulary Build(params LauncherItem[] items)
    {
        var layout = new LauncherLayout { Items = [.. items] };
        return VoiceVocabulary.Build(LauncherModel.FromLayout(layout), ["顯示列"], ["隱藏列"]);
    }

    [Fact]
    public void Names_also_say_phrases_workspaces_and_the_built_in_phrases_are_listened_for()
    {
        var group = new GroupItem { Name = "Tools", Items = [WinScp.Clone(), new SeparatorItem()] };
        var voice = new SpecialIconItem { Name = "Voice", Kind = SpecialIconKinds.Voice };
        var clock = new SpecialIconItem { Name = "Clock", Kind = "clock" };

        var vocabulary = Build(Notepad.Clone(), group, Calculator.Clone(), voice, clock);

        Assert.Equal(
            new[] { "記事本", "WinSCP", "檔案傳輸", "win S C P", "小算盤", "Clock", "Morning", "顯示列", "隱藏列" }.Order(),
            vocabulary.Phrases.Order());
        Assert.Equal(WinScp.Id, Assert.Single(vocabulary.TargetsOf("檔案傳輸")).ItemId);
        Assert.Equal(WinScp.Id, Assert.Single(vocabulary.TargetsOf("WINSCP")).ItemId); // the recogniser's spelling of case does not matter
        Assert.Equal(VoiceTargetKind.Workspace, Assert.Single(vocabulary.TargetsOf("Morning")).Kind);
        Assert.Equal(VoiceTargetKind.HideBar, Assert.Single(vocabulary.TargetsOf("隱藏列")).Kind);
        Assert.Empty(vocabulary.TargetsOf("Tools")); // a group opens at its icon: not started by voice
        Assert.Empty(vocabulary.TargetsOf("Voice")); // nor the voice icon itself
    }

    [Theory]
    [InlineData("  Visual   Studio  ", "Visual Studio")]
    [InlineData("---", null)] // nothing to say
    [InlineData("", null)]
    [InlineData("a\u202Eb", null)] // not shown as written
    public void A_phrase_is_what_can_be_said_and_shown(string text, string? phrase)
    {
        Assert.Equal(phrase, VoiceVocabulary.Speakable(text));
        Assert.Null(VoiceVocabulary.Speakable(new string('x', VoiceVocabulary.MaxPhraseLength + 1)));
    }

    [Fact]
    public void Also_say_is_stored_tidy_and_kept_through_a_copy_and_the_file()
    {
        Assert.Equal("檔案傳輸; win S C P", VoiceVocabulary.NormalizeAlsoSay(" 檔案傳輸；win S C P ; 檔案傳輸、 "));
        Assert.Null(VoiceVocabulary.NormalizeAlsoSay(" ; "));

        var edited = (ShortcutItem)WinScp.Clone();
        edited.AlsoSay = "傳檔|傳檔";
        var target = (ShortcutItem)WinScp.Clone();
        target.CopyContentFrom(edited);
        Assert.Equal("傳檔", target.AlsoSay);

        var json = System.Text.Json.JsonSerializer.Serialize(new LauncherLayout { Items = [target] }, Storage.LayoutStorage.JsonOptions);
        Assert.Contains("\"alsoSay\"", json);
        Assert.Equal("傳檔", System.Text.Json.JsonSerializer.Deserialize<LauncherLayout>(json, Storage.LayoutStorage.JsonOptions)!.Items.Single().AlsoSay);
    }

    [Fact]
    public void A_clear_answer_starts_one_item_and_anything_less_is_offered_or_dropped()
    {
        var twin = new ShortcutItem { Name = "記事本", Location = @"D:\other\notepad.exe" };
        var vocabulary = Build(Notepad.Clone(), WinScp.Clone(), Calculator.Clone(), twin);

        var sure = vocabulary.Decide([new VoiceHeard("小算盤", 0.96), new VoiceHeard("WinSCP", 0.5)]);
        Assert.Equal(VoiceOutcomeKind.Start, sure.Kind);
        Assert.Equal(Calculator.Id, Assert.Single(sure.Targets).ItemId);

        var unsure = vocabulary.Decide([new VoiceHeard("小算盤", 0.6), new VoiceHeard("WinSCP", 0.5), new VoiceHeard("Morning", 0.2)]);
        Assert.Equal(VoiceOutcomeKind.Choose, unsure.Kind);
        Assert.Equal([Calculator.Id, WinScp.Id], unsure.Targets.Select(t => t.ItemId!.Value)); // best first; 0.2 is not offered

        var shared = vocabulary.Decide([new VoiceHeard("記事本", 0.97), new VoiceHeard("小算盤", 0.5)]);
        Assert.Equal(VoiceOutcomeKind.Choose, shared.Kind); // two items are called this
        Assert.Equal([Notepad.Id, twin.Id], shared.Targets.Select(t => t.ItemId!.Value));

        Assert.Equal(VoiceOutcomeKind.NotHeard, vocabulary.Decide([new VoiceHeard("小算盤", 0.3)]).Kind);
        Assert.Equal(VoiceOutcomeKind.NotHeard, vocabulary.Decide([]).Kind);
        Assert.Equal(VoiceOutcomeKind.NotHeard, vocabulary.Decide([new VoiceHeard("not in the list", 0.99)]).Kind);
    }

    [Fact]
    public void The_command_line_can_start_listening()
    {
        var (commands, error) = BarCommands.Parse(["--voice"]);

        Assert.Null(error);
        Assert.Equal(BarCommandKind.Voice, Assert.Single(commands).Kind);
        Assert.Equal(BarCommandKind.Voice, BarCommands.Deserialize(BarCommands.Serialize(commands))!.Single().Kind);
    }

    [Theory]
    [InlineData("en-US", "zh-Hant", "zh-TW,en-US", "en-US")] // the setting
    [InlineData("ja-JP", "zh-Hant", "zh-TW,en-US", "zh-TW")] // a setting from another PC: the bar's language
    [InlineData(null, "zh-Hant", "en-US,zh-TW", "zh-TW")]
    [InlineData(null, "zh-Hans", "en-US,zh-TW", "en-US")] // Traditional is not offered for Simplified: the first one
    [InlineData(null, "zh-Hans", "en-US,zh-CN", "zh-CN")]
    [InlineData(null, "en-GB", "zh-TW,en-US", "en-US")] // another region of the language
    [InlineData(null, "", "zh-TW,en-US", "zh-TW")]
    [InlineData(null, "de-DE", "zh-TW,en-US", "zh-TW")]
    [InlineData("en-US", "en-US", "", null)] // no recogniser on this PC
    public void The_recogniser_is_the_settings_then_the_bars_language(string? setting, string bar, string installed, string? expected)
    {
        Assert.Equal(expected, VoiceLanguage.Pick(setting, bar, installed.Split(',', StringSplitOptions.RemoveEmptyEntries)));
    }
}
