using System.Globalization;
using System.Speech.Recognition;
using DesktopIconGrid.Core.Diagnostics;
using DesktopIconGrid.Core.Voice;

namespace DesktopIconGrid.Services;

/// <summary>What one listening gave: the answers, best first, or why it could not listen.</summary>
public sealed record VoiceListenResult(IReadOnlyList<VoiceHeard> Heard, string? Problem = null);

/// <summary>The recogniser behind voice launch, so tests can stand in for it.</summary>
public interface IVoiceEar
{
    /// <summary>The culture names (<c>zh-TW</c>) of the recognisers this PC has.</summary>
    IReadOnlyList<string> InstalledLanguages();

    /// <summary>Listens for one of <paramref name="phrases"/>, once, until something was said, the silence limit, or <paramref name="cancellation"/>.</summary>
    Task<VoiceListenResult> ListenAsync(string language, IReadOnlyCollection<string> phrases, CancellationToken cancellation);
}

/// <summary>
/// Windows' desktop speech recognisers (SAPI, through <c>System.Speech</c>): on the device, no online service, one
/// utterance from the default recording device, matched against a list of phrases only (FEATURES §6.1 has the trial
/// that chose it). Everything runs off the UI thread: making the recogniser and loading the list take a moment.
/// </summary>
public sealed class VoiceEar : IVoiceEar
{
    /// <summary>How long it waits for speech to begin.</summary>
    private static readonly TimeSpan SilenceLimit = TimeSpan.FromSeconds(6);

    /// <summary>The most answers taken from one utterance.</summary>
    private const int MaxAlternates = 5;

    private static readonly object CacheGate = new();
    private static IReadOnlyList<string>? _installed;
    private static DateTime _installedAt;

    /// <summary>Read from Windows at most once a minute: a tooltip asks too, and speech packs come and go rarely.</summary>
    public IReadOnlyList<string> InstalledLanguages()
    {
        lock (CacheGate)
        {
            if (_installed is null || DateTime.UtcNow - _installedAt > TimeSpan.FromMinutes(1))
            {
                _installed = ReadInstalledLanguages();
                _installedAt = DateTime.UtcNow;
            }

            return _installed;
        }
    }

    private static IReadOnlyList<string> ReadInstalledLanguages()
    {
        try
        {
            return SpeechRecognitionEngine.InstalledRecognizers().Select(r => r.Culture.Name).Where(name => name.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // No SAPI on this Windows, or its registry entries are damaged: voice launch says there is no recogniser.
            AppLog.Default.Warning("Voice", $"The speech recognisers could not be listed: {ex.Message}");
            return [];
        }
    }

    public Task<VoiceListenResult> ListenAsync(string language, IReadOnlyCollection<string> phrases, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(language);
        ArgumentNullException.ThrowIfNull(phrases);
        return Task.Run(() => ListenCoreAsync(language, phrases, cancellation), CancellationToken.None);
    }

    private static async Task<VoiceListenResult> ListenCoreAsync(string language, IReadOnlyCollection<string> phrases, CancellationToken cancellation)
    {
        try
        {
            using var engine = new SpeechRecognitionEngine(CultureInfo.GetCultureInfo(language));
            var culture = engine.RecognizerInfo.Culture;
            var loaded = Load(engine, culture, phrases);
            if (loaded == 0)
            {
                return new VoiceListenResult([], "The recogniser took none of the names.");
            }

            engine.SetInputToDefaultAudioDevice();
            engine.MaxAlternates = MaxAlternates;
            engine.InitialSilenceTimeout = SilenceLimit;
            var done = new TaskCompletionSource<RecognitionResult?>(TaskCreationOptions.RunContinuationsAsynchronously);
            engine.RecognizeCompleted += (_, e) =>
            {
                if (e.Error is not null)
                {
                    done.TrySetException(e.Error);
                }
                else
                {
                    done.TrySetResult(e.Cancelled ? null : e.Result);
                }
            };
            engine.RecognizeAsync(RecognizeMode.Single);
            RecognitionResult? result;
            using (cancellation.Register(() =>
            {
                try
                {
                    engine.RecognizeAsyncCancel();
                }
                catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
                {
                    // Already finished.
                }
            }))
            {
                result = await done.Task.ConfigureAwait(false);
            }

            if (result is null)
            {
                return new VoiceListenResult([]);
            }

            // The alternates include the best answer; without any, the answer itself.
            var answers = result.Alternates.Count > 0
                ? result.Alternates.Select(a => new VoiceHeard(a.Text, a.Confidence))
                : [new VoiceHeard(result.Text, result.Confidence)];
            var heard = answers
                .GroupBy(a => a.Phrase, StringComparer.OrdinalIgnoreCase)
                .Select(group => new VoiceHeard(group.Key, group.Max(a => a.Confidence)))
                .OrderByDescending(a => a.Confidence)
                .ToList();
            AppLog.Default.Info("Voice", $"Heard {heard.Count} answer(s) in {culture.Name}, the best at {(heard.Count > 0 ? heard[0].Confidence : 0):0.00}");
            return new VoiceListenResult(heard);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // No recogniser for the language after all, no recording device, the audio service stopped, a COM failure:
            // said to the user as it is, never thrown at the bar.
            AppLog.Default.Warning("Voice", $"Listening failed: {ex.GetType().Name}: {ex.Message}");
            return new VoiceListenResult([], ex.Message);
        }
    }

    /// <summary>
    /// Gives the recogniser the phrases as one list; when it refuses the list (a phrase it has no pronunciation for, as
    /// a recogniser of one language may with another language's words), each phrase on its own, leaving out the ones it
    /// refuses. Returns how many it took.
    /// </summary>
    private static int Load(SpeechRecognitionEngine engine, CultureInfo culture, IReadOnlyCollection<string> phrases)
    {
        if (phrases.Count == 0)
        {
            return 0;
        }

        try
        {
            engine.LoadGrammar(new Grammar(new GrammarBuilder(new Choices([.. phrases])) { Culture = culture }));
            return phrases.Count;
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException or ArgumentException or System.Runtime.InteropServices.COMException)
        {
            AppLog.Default.Debug("Voice", $"The list as a whole was refused ({ex.Message}); loading the names one by one");
        }

        engine.UnloadAllGrammars();
        var taken = 0;
        foreach (var phrase in phrases)
        {
            try
            {
                engine.LoadGrammar(new Grammar(new GrammarBuilder(phrase) { Culture = culture }));
                taken++;
            }
            catch (Exception ex) when (ex is InvalidOperationException or FormatException or ArgumentException or System.Runtime.InteropServices.COMException)
            {
                // Not sayable in this language: it needs an Also say phrase.
            }
        }

        AppLog.Default.Info("Voice", $"{taken} of {phrases.Count} names can be heard in {culture.Name}");
        return taken;
    }
}
