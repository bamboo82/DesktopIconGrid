using System.IO;
using System.Windows.Threading;
using DesktopIconGrid.Core;
using DesktopIconGrid.Core.Launching;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Core.SpecialIcons;
using DesktopIconGrid.Core.Storage;
using DesktopIconGrid.Core.Voice;
using DesktopIconGrid.Resources;
using DesktopIconGrid.Services;
using DesktopIconGrid.SpecialIcons;
using DesktopIconGrid.ViewModels;

namespace DesktopIconGrid.App.Tests;

/// <summary>
/// Voice launch on a stand-in recogniser: what is started, what is offered, and what is only said. In the collection
/// of <see cref="LauncherViewModelTests"/>, which changes <c>ThisDevice.Id</c> for a moment: approvals are kept per device.
/// </summary>
[Collection("Shared icon worker")]
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

    /// <summary>Hears what a test says it hears, by text, and answers with the index of that phrase in what it was given.</summary>
    private sealed class FakeEar(params string[] installed) : IVoiceEar
    {
        public (string Text, double Confidence)[] Heard { get; set; } = [];

        public string? Problem { get; set; }

        public List<(string Language, string[] Phrases)> Listened { get; } = [];

        public IReadOnlyList<string> InstalledLanguages() => installed;

        public Task<VoiceListenResult> ListenAsync(string language, IReadOnlyList<string> phrases, CancellationToken cancellation)
        {
            Listened.Add((language, [.. phrases]));
            var list = phrases.ToList();
            var heard = Heard.Select(h => new VoiceHeard(list.FindIndex(p => VoiceVocabulary.Fold(p) == VoiceVocabulary.Fold(h.Text)), h.Confidence));
            return Task.FromResult(new VoiceListenResult([.. heard], Problem));
        }
    }

    /// <summary>An ear that listens until it is cancelled, as a real one does while nobody speaks, then answers <see cref="AfterCancel"/>.</summary>
    private sealed class WaitingEar : IVoiceEar
    {
        public TaskCompletionSource Started { get; } = new();

        public VoiceListenResult AfterCancel { get; set; } = new([]);

        public IReadOnlyList<string> InstalledLanguages() => ["en-US"];

        public async Task<VoiceListenResult> ListenAsync(string language, IReadOnlyList<string> phrases, CancellationToken cancellation)
        {
            Started.TrySetResult();
            var cancelled = new TaskCompletionSource();
            using (cancellation.Register(() => cancelled.TrySetResult()))
            {
                await cancelled.Task;
            }

            return AfterCancel;
        }
    }

    private sealed class RecordingView : IVoiceView
    {
        public List<string> Said { get; } = [];

        public int ListeningShown { get; private set; }

        public Action? CancelListening { get; private set; }

        public IReadOnlyList<VoiceChoiceView>? Offered { get; private set; }

        public int Chooses { get; private set; }

        public Func<IReadOnlyList<VoiceChoiceView>, VoiceChoiceView?> Pick { get; set; } = _ => null;

        public int DoneShown { get; private set; }

        public void Listening(string language, Action cancel)
        {
            ListeningShown++;
            CancelListening = cancel;
        }

        public void Say(string message) => Said.Add(message);

        public void Done() => DoneShown++;

        public Task<VoiceChoiceView?> ChooseAsync(IReadOnlyList<VoiceChoiceView> choices)
        {
            Chooses++;
            Offered = choices;
            return Task.FromResult(Pick(choices));
        }
    }

    private static LauncherModel Bar() => LauncherModel.FromLayout(new LauncherLayout
    {
        Items =
        [
            new ShortcutItem { Name = "記事本", Location = @"C:\Windows\notepad.exe" },
            new ShortcutItem { Name = "WinSCP", Location = @"C:\Tools\WinSCP.exe", AlsoSay = "檔案傳輸" }, // as if it came with a synced bar
            new ShortcutItem { Name = "Editor", Location = @"C:\Tools\editor.exe", Workspaces = ["Morning"] },
        ],
    });

    private static void ListenIn(TestBar bar, string language) => bar.ViewModel.ApplyLocalSettings(new LocalSettings { VoiceLanguage = language });

    [Fact]
    public async Task A_name_heard_clearly_is_started_as_a_click_would_and_said_after_it_started()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var launcher = new RecordingLauncher { Result = LaunchResult.Started };
        using var bar = new TestBar(_directory, Bar(), launcher);
        var ear = new FakeEar("en-US", "zh-TW") { Heard = [("WinSCP", 0.95)] };
        var view = new RecordingView();
        ListenIn(bar, "zh-TW"); // this PC's setting (review 27 B4)
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

        // A start that fails is not announced as started: the bar says why itself.
        launcher.Result = LaunchResult.Failure("nope");
        await Task.Delay(450); // past the double-click guard for the same icon
        await new VoiceLaunch(bar.ViewModel, ear, view, () => { }, () => { }).ListenAsync();
        Assert.Single(view.Said);
        Assert.Equal(1, view.DoneShown);
    }

    [Fact]
    public async Task A_name_heard_less_clearly_is_offered_with_what_each_starts_and_nothing_starts_unless_picked()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var launcher = new RecordingLauncher { Result = LaunchResult.Started };
        using var bar = new TestBar(_directory, Bar(), launcher);
        var ear = new FakeEar("zh-TW") { Heard = [("記事本", 0.6), ("WinSCP", 0.5)] };
        var view = new RecordingView();
        var voice = new VoiceLaunch(bar.ViewModel, ear, view, () => { }, () => { });

        await voice.ListenAsync();

        Assert.Equal(["記事本", "WinSCP"], view.Offered!.Select(c => c.Name));
        Assert.Equal(string.Format(Strings.Voice_Shortcut, @"C:\Windows\notepad.exe"), view.Offered![0].Detail);
        Assert.False(view.Offered![1].CameFromAnotherPc); // heard by its own name
        Assert.Empty(launcher.Launches); // nobody picked, and Cancel says nothing
        Assert.Empty(view.Said);

        view.Pick = choices => choices[1];
        await voice.ListenAsync();

        Assert.Equal(@"C:\Tools\WinSCP.exe", Assert.Single(launcher.Launches).Location);
    }

    /// <summary>Review 27 B3b and the test the plan asks for.</summary>
    [Fact]
    public async Task An_arrived_phrase_is_offered_picked_and_starts_at_once_the_next_time()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var launcher = new RecordingLauncher { Result = LaunchResult.Started };
        using var bar = new TestBar(_directory, Bar(), launcher);
        var ear = new FakeEar("zh-TW") { Heard = [("檔案傳輸", 0.95)] };
        var view = new RecordingView { Pick = choices => choices[0] };
        var voice = new VoiceLaunch(bar.ViewModel, ear, view, () => { }, () => { });

        await voice.ListenAsync();

        var offered = Assert.Single(view.Offered!);
        Assert.True(offered.CameFromAnotherPc); // heard alone and sure, but by a phrase not approved here
        Assert.Single(launcher.Launches);
        var winScp = bar.Model.AllShortcuts().Single(s => s.Name == "WinSCP");
        Assert.True(bar.ViewModel.Trust.IsApproved(VoiceVocabulary.ApprovalHash(winScp.Id, "檔案傳輸")));

        await Task.Delay(450); // past the double-click guard for the same icon
        await voice.ListenAsync();

        Assert.Equal(1, view.Chooses); // no window the second time
        Assert.Equal(2, launcher.Launches.Count);
    }

    [Fact]
    public async Task A_choice_whose_item_changed_while_it_was_shown_starts_nothing()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var launcher = new RecordingLauncher { Result = LaunchResult.Started };
        using var bar = new TestBar(_directory, Bar(), launcher);
        var ear = new FakeEar("zh-TW") { Heard = [("記事本", 0.6), ("WinSCP", 0.5)] };
        var view = new RecordingView
        {
            Pick = choices =>
            {
                // A sync load while the window waits: the same id now starts something else.
                bar.Model.AllShortcuts().Single(s => s.Name == "WinSCP").Location = @"\\nas\run.cmd";
                return choices[1];
            },
        };

        await new VoiceLaunch(bar.ViewModel, ear, view, () => { }, () => { }).ListenAsync();

        Assert.Empty(launcher.Launches);
        Assert.Equal(Strings.Voice_BarChanged, Assert.Single(view.Said));
    }

    /// <summary>The plan's test: an unapproved command tile by voice opens its approval window and runs nothing.</summary>
    [Fact]
    public async Task An_unapproved_command_tile_said_by_voice_asks_for_its_approval_and_runs_nothing()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var trust = new CommandTrust(null);
        var asked = 0;
        var tiles = new CommandTileHandler(trust, () => null, Dispatcher.CurrentDispatcher, (_, _) =>
        {
            asked++;
            return false; // No
        });
        var tile = tiles.CreateItem();
        tile.Name = "Backup";
        var settings = CommandTileSettings.Default with { Program = @"C:\Tools\backup.exe", Arguments = "--all" };
        settings.ApplyTo(tile);
        var model = Bar();
        model.Insert(tile, null, int.MaxValue);
        using var bar = new TestBar(_directory, model, handlers: [tiles], trust: trust);
        var view = new RecordingView();

        await new VoiceLaunch(bar.ViewModel, new FakeEar("en-US") { Heard = [("Backup", 0.97)] }, view, () => { }, () => { }).ListenAsync();

        Assert.Equal(1, asked);
        Assert.False(trust.IsApproved(settings.Hash()));
        Assert.Empty(view.Said); // nothing started, nothing announced
        Assert.Equal(1, view.DoneShown);
    }

    [Fact]
    public async Task A_workspace_is_started_by_voice()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var launcher = new RecordingLauncher { Result = LaunchResult.Started };
        using var bar = new TestBar(_directory, Bar(), launcher);
        bar.ViewModel.WorkspaceStep = TimeSpan.FromMilliseconds(5);
        var view = new RecordingView();

        await new VoiceLaunch(bar.ViewModel, new FakeEar("en-US") { Heard = [("Morning", 0.9)] }, view, () => { }, () => { }).ListenAsync();

        Assert.Equal(@"C:\Tools\editor.exe", Assert.Single(launcher.Launches).Location);
        Assert.Equal(string.Format(Strings.Voice_Starting, "Morning"), Assert.Single(view.Said));
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

        await new VoiceLaunch(bar.ViewModel, new FakeEar("en-US") { Heard = [("WinSCP", 0.2)] }, view, () => { }, () => { }).ListenAsync();
        await new VoiceLaunch(bar.ViewModel, new FakeEar(), view, () => { }, () => { }).ListenAsync();
        await new VoiceLaunch(bar.ViewModel, new FakeEar("en-US") { Problem = "no microphone" }, view, () => { }, () => { }).ListenAsync();

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
        var hidden = 0;

        await new VoiceLaunch(bar.ViewModel, new FakeEar("en-US") { Heard = [("Hide bar.", 0.9)] }, new RecordingView(), () => { }, () => hidden++).ListenAsync();

        Assert.Equal(1, hidden); // "Hide bar." is the phrase "Hide bar", folded
    }

    [Fact]
    public async Task Letting_the_hotkey_go_ends_the_listening_without_a_word()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var bar = new TestBar(_directory, Bar());
        var ear = new WaitingEar();
        var view = new RecordingView();
        var voice = new VoiceLaunch(bar.ViewModel, ear, view, () => { }, () => { });
        Assert.False(voice.IsListening);

        var listening = voice.ListenAsync();
        await ear.Started.Task;
        Assert.True(voice.IsListening);
        voice.Release();
        await listening.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.False(voice.IsListening);
        Assert.Equal(1, view.DoneShown);
        Assert.Empty(view.Said); // cancelled by letting go: not "Didn't catch that"
    }

    [Fact]
    public async Task Cancel_says_nothing_and_drops_an_answer_that_comes_after_it()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var launcher = new RecordingLauncher { Result = LaunchResult.Started };
        using var bar = new TestBar(_directory, Bar(), launcher);
        var ear = new WaitingEar();
        var view = new RecordingView();
        var voice = new VoiceLaunch(bar.ViewModel, ear, view, () => { }, () => { });
        var listening = voice.ListenAsync();
        await ear.Started.Task;
        ear.AfterCancel = new([new VoiceHeard(0, 0.99)]); // the recogniser answers anyway, racing the cancel

        view.CancelListening!(); // Cancel in the window (a second press of the hotkey calls the same)
        await listening.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Empty(launcher.Launches);
        Assert.Empty(view.Said);
        Assert.Equal(1, view.DoneShown);
    }

    [Fact]
    public void A_long_location_is_trimmed_in_the_middle()
    {
        Assert.Equal("ab…ij", VoiceLaunch.TrimMiddle("abcdefghij", 5));
        Assert.Equal("short", VoiceLaunch.TrimMiddle("short", 5));
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
    public void The_voice_hotkey_must_differ_from_the_other_two_and_is_saved_as_this_PCs()
    {
        var before = SettingsViewModel.InstalledVoiceLanguages;
        SettingsViewModel.InstalledVoiceLanguages = () => ["zh-TW", "en-US"];
        try
        {
            var settings = new SettingsViewModel(new LauncherSettings { ToggleHotkey = "Ctrl+Alt+Space" }, new LocalSettings { VoiceLanguage = "en-US" });
            Assert.Equal("en-US", settings.SelectedVoiceLanguage.Id);
            Assert.False(settings.HasNoVoiceRecogniser);

            settings.VoiceHotkey = "Alt+Ctrl+Space"; // the same, spelled differently
            Assert.True(settings.IsVoiceHotkeySame);
            Assert.False(settings.CanSave);

            settings.VoiceHotkey = "Ctrl+Alt+V";
            settings.SelectedVoiceLanguage = settings.VoiceLanguages[0];
            Assert.True(settings.CanSave);
            var local = settings.ToLocalSettings();
            Assert.Equal("Ctrl+Alt+V", local.VoiceHotkey);
            Assert.Null(local.VoiceLanguage); // "Same as the bar"
            Assert.DoesNotContain("voiceHotkey", System.Text.Json.JsonSerializer.Serialize(settings.ToSettings(), LayoutStorage.JsonOptions), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            SettingsViewModel.InstalledVoiceLanguages = before;
        }
    }

    [Fact]
    public void The_names_table_edits_the_phrases_and_approves_only_what_was_typed_here()
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
        var winScp = table.Rows.Single(r => r.GivenName == "WinSCP");
        var group = table.Rows.Single(r => r.GivenName == "Tools");

        notepad.AlsoSay = " notepad；筆記 ";
        winScp.AlsoSay = "檔案傳輸; 傳檔"; // keeps the phrase that arrived, adds one
        group.AlsoSay = "tools"; // a group is not started by voice
        Assert.Equal(string.Empty, group.AlsoSay);
        Assert.Equal(2, table.ChangedCount);

        Assert.Equal(2, table.Apply());
        var shortcuts = model.AllShortcuts().ToList();
        Assert.Equal("notepad; 筆記", shortcuts[0].AlsoSay);
        Assert.True(bar.ViewModel.Trust.IsApproved(VoiceVocabulary.ApprovalHash(shortcuts[0].Id, "筆記")));
        Assert.True(bar.ViewModel.Trust.IsApproved(VoiceVocabulary.ApprovalHash(shortcuts[1].Id, "傳檔")));
        Assert.False(bar.ViewModel.Trust.IsApproved(VoiceVocabulary.ApprovalHash(shortcuts[1].Id, "檔案傳輸"))); // it was there: not typed here
    }
}
