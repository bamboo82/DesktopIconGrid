using System.Text.Json;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Core.Storage;

namespace DesktopIconGrid.Core.Tests;

/// <summary>
/// Review 27 B4: the voice hotkey and language are this PC's, in a file nothing synced or restored touches, one set per
/// PC (the review's answer on WP3).
/// </summary>
[Collection("This device")] // sets ThisDevice.Id, which is process-wide
public sealed class LocalSettingsTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("dig-local-").FullName;
    private readonly string? _device = ThisDevice.Id;

    public LocalSettingsTests() => ThisDevice.Id = "pc-a";

    public void Dispose()
    {
        ThisDevice.Id = _device;
        ThisDevice.IsOfThisMachine = true;
        Directory.Delete(_folder, recursive: true);
    }

    private LocalSettingsStore Store() => new(Path.Combine(_folder, LocalSettings.FileName));

    [Fact]
    public void This_PCs_settings_round_trip_and_keep_what_a_newer_version_wrote()
    {
        var store = Store();
        Assert.Null(store.Load().VoiceHotkey); // no file yet

        File.WriteAllText(store.FilePath!, """{"pcs":{"pc-a":{"voiceHotkey":" Ctrl+Alt+V ","voiceLanguage":"zh-TW","futureSetting":42}},"futureTop":1}""");
        var settings = store.Load();
        Assert.Equal(("Ctrl+Alt+V", "zh-TW"), (settings.VoiceHotkey, settings.VoiceLanguage));

        settings.VoiceLanguage = null;
        Assert.True(store.Save(settings));
        var again = store.Load();
        Assert.Equal(("Ctrl+Alt+V", (string?)null), (again.VoiceHotkey, again.VoiceLanguage));
        Assert.Equal(42, again.ExtensionData!["futureSetting"].GetInt32());
        Assert.Contains("futureTop", File.ReadAllText(store.FilePath!));
    }

    [Fact]
    public void Each_PC_that_uses_the_file_has_its_own_settings()
    {
        // A portable copy carried to another PC must not register its microphone hotkey there.
        var store = Store();
        Assert.True(store.Save(new LocalSettings { VoiceHotkey = "Ctrl+Alt+V" }));

        ThisDevice.Id = "pc-b";
        Assert.Null(store.Load().VoiceHotkey);
        Assert.True(store.Save(new LocalSettings { VoiceLanguage = "en-US" }));

        ThisDevice.Id = "pc-a";
        var mine = store.Load();
        Assert.Equal(("Ctrl+Alt+V", (string?)null), (mine.VoiceHotkey, mine.VoiceLanguage));
    }

    [Fact]
    public void Without_this_PCs_own_id_nothing_is_read_or_written()
    {
        var store = Store();
        store.Save(new LocalSettings { VoiceHotkey = "Ctrl+Alt+V" });

        ThisDevice.IsOfThisMachine = false; // the id is the data folder's, which travels: as approvals, this run only
        Assert.Null(store.Load().VoiceHotkey);
        Assert.False(store.Save(new LocalSettings { VoiceHotkey = "Ctrl+Alt+X" }));

        ThisDevice.IsOfThisMachine = true;
        Assert.Equal("Ctrl+Alt+V", store.Load().VoiceHotkey); // untouched
    }

    [Fact]
    public void A_broken_or_oversized_file_or_one_set_for_every_PC_reads_as_no_settings()
    {
        var store = Store();

        File.WriteAllText(store.FilePath!, "{ not json");
        Assert.Null(store.Load().VoiceHotkey);

        File.WriteAllText(store.FilePath!, "{\"pcs\":{\"pc-a\":{\"voiceHotkey\":\"Ctrl+V\"}},\"x\":\"" + new string('a', (int)LocalSettings.MaxFileBytes) + "\"}");
        Assert.Null(store.Load().VoiceHotkey);

        File.WriteAllText(store.FilePath!, "{\"pcs\":{\"pc-a\":{\"voiceHotkey\":\"" + new string('a', 101) + "\",\"voiceLanguage\":\"" + new string('b', 21) + "\"}}}");
        var odd = store.Load();
        Assert.Equal(((string?)null, (string?)null), (odd.VoiceHotkey, odd.VoiceLanguage));

        // A development build of 0.11.0 kept one set for every PC: it is no PC's, and goes at the next save.
        File.WriteAllText(store.FilePath!, """{"voiceHotkey":"Ctrl+V"}""");
        Assert.Null(store.Load().VoiceHotkey);
        Assert.True(store.Save(new LocalSettings()));
        Assert.DoesNotContain("voiceHotkey", File.ReadAllText(store.FilePath!));
    }

    [Fact]
    public void A_layout_that_carries_a_voice_hotkey_does_not_set_one()
    {
        // A synced or restored copy written by a development build of 0.11.0 had the hotkey in its settings.
        var settings = JsonSerializer.Deserialize<LauncherSettings>("""{"voiceHotkey":"Ctrl+V","voiceLanguage":"en-US"}""", LayoutStorage.JsonOptions)!;

        Assert.Null(typeof(LauncherSettings).GetProperty("VoiceHotkey"));
        Assert.True(settings.ExtensionData!.ContainsKey("voiceHotkey")); // kept as an unknown property, used by nothing
    }
}
