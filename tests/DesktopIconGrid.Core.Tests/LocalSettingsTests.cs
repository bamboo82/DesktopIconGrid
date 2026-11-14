using System.Text.Json;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Core.Storage;

namespace DesktopIconGrid.Core.Tests;

/// <summary>Review 27 B4: the voice hotkey and language are this PC's, in a file nothing synced or restored touches.</summary>
public sealed class LocalSettingsTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("dig-local-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void This_PCs_settings_round_trip_and_keep_what_a_newer_version_wrote()
    {
        var store = new LocalSettingsStore(Path.Combine(_folder, LocalSettings.FileName));
        Assert.Null(store.Load().VoiceHotkey); // no file yet

        File.WriteAllText(store.FilePath!, """{"voiceHotkey":" Ctrl+Alt+V ","voiceLanguage":"zh-TW","futureSetting":42}""");
        var settings = store.Load();
        Assert.Equal(("Ctrl+Alt+V", "zh-TW"), (settings.VoiceHotkey, settings.VoiceLanguage));

        settings.VoiceLanguage = null;
        Assert.True(store.Save(settings));
        var again = store.Load();
        Assert.Equal(("Ctrl+Alt+V", (string?)null), (again.VoiceHotkey, again.VoiceLanguage));
        Assert.Equal(42, again.ExtensionData!["futureSetting"].GetInt32());
    }

    [Fact]
    public void A_broken_or_oversized_file_reads_as_no_settings()
    {
        var store = new LocalSettingsStore(Path.Combine(_folder, LocalSettings.FileName));

        File.WriteAllText(store.FilePath!, "{ not json");
        Assert.Null(store.Load().VoiceHotkey);

        File.WriteAllText(store.FilePath!, "{\"voiceHotkey\":\"Ctrl+V\",\"x\":\"" + new string('a', (int)LocalSettings.MaxFileBytes) + "\"}");
        Assert.Null(store.Load().VoiceHotkey);

        File.WriteAllText(store.FilePath!, "{\"voiceHotkey\":\"" + new string('a', 101) + "\",\"voiceLanguage\":\"" + new string('b', 21) + "\"}");
        var odd = store.Load();
        Assert.Equal(((string?)null, (string?)null), (odd.VoiceHotkey, odd.VoiceLanguage));
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
