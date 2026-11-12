using System.IO;
using DesktopIconGrid.Core;
using DesktopIconGrid.Core.Launching;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Core.Voice;
using DesktopIconGrid.Resources;
using DesktopIconGrid.Services;
using DesktopIconGrid.ViewModels;

namespace DesktopIconGrid.App.Tests;

/// <summary>Voice launch on a stand-in recogniser: what is started, what is offered, and what is only said.</summary>
public sealed class VoiceLaunchTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "dig-voice-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private sealed class FakeEar(params string[] installed) : IVoiceEar
    {
        public VoiceListenResult Answer { get; set; } = new([]);

        public List<(string Language, string[] Phrases)> Listened { get; } = [];

        public IReadOnlyList<string> InstalledLanguages() => installed;

        public Task<VoiceListenResult> ListenAsync(string language, IReadOnlyCollection<string> phrases, CancellationToken cancellation)
        {
            Listened.Add((language, [.. phrases]));
            return Task.FromResult(Answer);
        }
    }

    private sealed class RecordingView : IVoiceView
    {
        public List<string> Said { get; } = [];

        public int ListeningShown { get; private set; }

        public IReadOnlyList<VoiceTarget>? Offered { get; private set; }

        public Func<IReadOnlyList<VoiceTarget>, VoiceTarget?> Pick { get; set; } = _ => null;

        public void Listening(string language, Action cancel) => ListeningShown++;

        public void Say(string message) => Said.Add(message);

        public Task<VoiceTarget?> ChooseAsync(IReadOnlyList<VoiceTarget> targets)
        {
            Offered = targets;
            return Task.FromResult(Pick(targets));
        }
    }

    private static LauncherModel Bar() => LauncherModel.FromLayout(new LauncherLayout
    {
        Items =
        [
            new ShortcutItem { Name = "記事本", Location = @"C:\Windows\notepad.exe" },
            new ShortcutItem { Name = "WinSCP", Location = @"C:\Tools\WinSCP.exe", AlsoSay = "檔案傳輸" },
        ],
    });

    [Fact]
    public async Task A_name_heard_clearly_is_started_as_a_click_would()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var launcher = new RecordingLauncher { Result = LaunchResult.Started };
        using var bar = new TestBar(_directory, Bar(), launcher);
        var ear = new FakeEar("en-US", "zh-TW") { Answer = new([new VoiceHeard("檔案傳輸", 0.95)]) };
        var view = new RecordingView();
        bar.Model.Settings.VoiceLanguage = "zh-TW";
        var shown = 0;

        await new VoiceLaunch(bar.ViewModel, ear, view, () => shown++, () => { }).ListenAsync();

        Assert.Equal(@"C:\Tools\WinSCP.exe", Assert.Single(launcher.Launches).Location);
        var (language, phrases) = Assert.Single(ear.Listened);
        Assert.Equal("zh-TW", language);
        Assert.Contains("檔案傳輸", phrases);
        Assert.Contains("顯示工具列", phrases); // the built-in phrases in the language listened in
        Assert.Equal(1, view.ListeningShown);
        Assert.Equal(string.Format(Strings.Voice_Starting, "WinSCP"), Assert.Single(view.Said));
        Assert.Equal(0, shown);
    }

    [Fact]
    public async Task A_name_heard_less_clearly_is_offered_and_nothing_starts_unless_picked()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var launcher = new RecordingLauncher { Result = LaunchResult.Started };
        using var bar = new TestBar(_directory, Bar(), launcher);
        var ear = new FakeEar("zh-TW") { Answer = new([new VoiceHeard("記事本", 0.6), new VoiceHeard("WinSCP", 0.5)]) };
        var view = new RecordingView();
        var voice = new VoiceLaunch(bar.ViewModel, ear, view, () => { }, () => { });

        await voice.ListenAsync();

        Assert.Equal(["記事本", "WinSCP"], view.Offered!.Select(t => t.Label));
        Assert.Empty(launcher.Launches); // nobody picked

        view.Pick = targets => targets[1];
        await voice.ListenAsync();

        Assert.Equal(@"C:\Tools\WinSCP.exe", Assert.Single(launcher.Launches).Location);
    }

    [Fact]
    public async Task Nothing_heard_no_recogniser_and_a_failure_are_said_and_start_nothing()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var launcher = new RecordingLauncher { Result = LaunchResult.Started };
        using var bar = new TestBar(_directory, Bar(), launcher);
        var view = new RecordingView();

        await new VoiceLaunch(bar.ViewModel, new FakeEar("en-US") { Answer = new([new VoiceHeard("WinSCP", 0.2)]) }, view, () => { }, () => { }).ListenAsync();
        await new VoiceLaunch(bar.ViewModel, new FakeEar(), view, () => { }, () => { }).ListenAsync();
        await new VoiceLaunch(bar.ViewModel, new FakeEar("en-US") { Answer = new([], "no microphone") }, view, () => { }, () => { }).ListenAsync();

        Assert.Equal([Strings.Voice_NotHeard, Strings.Voice_NoRecogniser, string.Format(Strings.Voice_Problem, "no microphone")], view.Said);
        Assert.Empty(launcher.Launches);
    }

    [Fact]
    public async Task The_built_in_phrases_show_and_hide_the_bar()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var bar = new TestBar(_directory, Bar());
        var ear = new FakeEar("en-US") { Answer = new([new VoiceHeard("Hide bar", 0.9)]) };
        var hidden = 0;

        await new VoiceLaunch(bar.ViewModel, ear, new RecordingView(), () => { }, () => hidden++).ListenAsync();

        Assert.Equal(1, hidden);
    }

    [Fact]
    public void Settings_offer_this_PCs_recognisers_and_keep_one_from_another_PC()
    {
        var options = SettingsViewModel.VoiceLanguageOptions(["zh-TW", "en-US"], "ja-JP");

        Assert.Equal(["", "zh-TW", "en-US", "ja-JP"], options.Select(o => o.Id));
        Assert.Equal(Strings.Settings_VoiceLanguageAuto, options[0].Name);
        Assert.EndsWith("?", options[3].Name);
    }

    [Fact]
    public void The_voice_hotkey_must_differ_from_the_other_two_and_is_saved()
    {
        var before = SettingsViewModel.InstalledVoiceLanguages;
        SettingsViewModel.InstalledVoiceLanguages = () => ["zh-TW", "en-US"];
        try
        {
            var settings = new SettingsViewModel(new LauncherSettings { ToggleHotkey = "Ctrl+Alt+Space", VoiceLanguage = "en-US" });
            Assert.Equal("en-US", settings.SelectedVoiceLanguage.Id);
            Assert.False(settings.HasNoVoiceRecogniser);

            settings.VoiceHotkey = "Alt+Ctrl+Space"; // the same, spelled differently
            Assert.True(settings.IsVoiceHotkeySame);
            Assert.False(settings.CanSave);

            settings.VoiceHotkey = "Ctrl+Alt+V";
            settings.SelectedVoiceLanguage = settings.VoiceLanguages[0];
            Assert.True(settings.CanSave);
            var saved = settings.ToSettings();
            Assert.Equal("Ctrl+Alt+V", saved.VoiceHotkey);
            Assert.Null(saved.VoiceLanguage); // "Same as the bar"
        }
        finally
        {
            SettingsViewModel.InstalledVoiceLanguages = before;
        }
    }

    [Fact]
    public void The_names_table_edits_the_phrases_of_what_can_be_said()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var model = Bar();
        model.Insert(new GroupItem { Name = "Tools" }, null, int.MaxValue);
        using var bar = new TestBar(_directory, model);
        var table = new NameTableViewModel(bar.ViewModel);
        var notepad = table.Rows.Single(r => r.GivenName == "記事本");
        var group = table.Rows.Single(r => r.GivenName == "Tools");

        notepad.AlsoSay = " notepad；筆記 ";
        group.AlsoSay = "tools"; // a group is not started by voice
        Assert.Equal(string.Empty, group.AlsoSay);
        Assert.Equal(1, table.ChangedCount);

        Assert.Equal(1, table.Apply());
        Assert.Equal("notepad; 筆記", model.AllShortcuts().First().AlsoSay);
    }
}
