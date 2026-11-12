using System.Globalization;
using DesktopIconGrid.Core.Diagnostics;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Core.Voice;
using DesktopIconGrid.Resources;
using DesktopIconGrid.ViewModels;

namespace DesktopIconGrid.Services;

/// <summary>What voice launch shows while it works (a small window; tests record the calls).</summary>
public interface IVoiceView
{
    /// <summary>Listening has begun, in <paramref name="language"/>; <paramref name="cancel"/> stops it.</summary>
    void Listening(string language, Action cancel);

    /// <summary>A last word (what is started, or what went wrong); goes away by itself.</summary>
    void Say(string message);

    /// <summary>Lets the user pick one of <paramref name="targets"/>; <see langword="null"/> when they pick none.</summary>
    Task<VoiceTarget?> ChooseAsync(IReadOnlyList<VoiceTarget> targets);
}

/// <summary>
/// Voice launch (ROADMAP #11): on its hotkey, a click on the Voice icon or <c>--voice</c>, the bar listens for one
/// spoken name and starts what is called so, as a click on it would. It never listens on its own or all the time, and
/// it only matches the names on the bar (<see cref="VoiceVocabulary"/>): nothing that is said is typed, sent or kept.
/// A name heard clearly starts; one heard less clearly is offered with the other candidates; anything less starts
/// nothing.
/// </summary>
public sealed class VoiceLaunch(LauncherViewModel bar, IVoiceEar ear, IVoiceView view, Action showBar, Action hideBar)
{
    /// <summary>The longest one listening lasts, whatever the recogniser does.</summary>
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(15);

    private bool _busy;

    /// <summary>The recogniser language in use with the current settings, or <see langword="null"/> when this PC has none.</summary>
    public string? Language => VoiceLanguage.Pick(bar.Model.Settings.VoiceLanguage, CultureInfo.CurrentUICulture.Name, ear.InstalledLanguages());

    /// <summary>Listens once and acts on it. A second call while one is under way does nothing.</summary>
    public async Task ListenAsync()
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        try
        {
            if (Language is not { } language)
            {
                view.Say(Strings.Voice_NoRecogniser);
                return;
            }

            var vocabulary = VoiceVocabulary.Build(bar.Model, BuiltIn("Voice_ShowBarPhrases", language), BuiltIn("Voice_HideBarPhrases", language));
            if (vocabulary.Phrases.Count == 0)
            {
                view.Say(Strings.Voice_NothingToSay);
                return;
            }

            using var cancellation = new CancellationTokenSource(Limit);
            view.Listening(language, cancellation.Cancel);
            var result = await ear.ListenAsync(language, vocabulary.Phrases, cancellation.Token);
            if (result.Problem is not null)
            {
                view.Say(string.Format(Strings.Voice_Problem, result.Problem));
                return;
            }

            var outcome = vocabulary.Decide(result.Heard);
            switch (outcome.Kind)
            {
                case VoiceOutcomeKind.Start:
                    Start(outcome.Targets[0]);
                    break;
                case VoiceOutcomeKind.Choose:
                    if (await view.ChooseAsync(outcome.Targets) is { } chosen)
                    {
                        Start(chosen);
                    }

                    break;
                default:
                    view.Say(Strings.Voice_NotHeard);
                    break;
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Started from a hotkey or a click with nobody awaiting it: nothing may escape.
            AppLog.Default.Error("Voice", "Voice launch failed", ex);
            view.Say(string.Format(Strings.Voice_Problem, ex.Message));
        }
        finally
        {
            _busy = false;
        }
    }

    private void Start(VoiceTarget target)
    {
        AppLog.Default.Info("Voice", $"Starting by voice: {target.Kind}");
        view.Say(string.Format(Strings.Voice_Starting, target.Label));
        switch (target.Kind)
        {
            case VoiceTargetKind.Item when target.ItemId is { } id && bar.Model.Find(id) is { } item and not GroupItem:
                bar.Activate(bar.GetOrCreateViewModel(item), anchor: null); // as a click: approvals and questions included
                break;
            case VoiceTargetKind.Workspace when target.Name is { } workspace:
                _ = bar.StartWorkspaceAsync(workspace);
                break;
            case VoiceTargetKind.ShowBar:
                showBar();
                break;
            case VoiceTargetKind.HideBar:
                hideBar();
                break;
        }
    }

    /// <summary>The built-in phrases in the language listened in (not the bar's): separated by <c>;</c> in the resources.</summary>
    private static string[] BuiltIn(string key, string language)
    {
        CultureInfo culture;
        try
        {
            culture = CultureInfo.GetCultureInfo(language);
        }
        catch (CultureNotFoundException)
        {
            culture = CultureInfo.InvariantCulture;
        }

        return (Strings.ResourceManager.GetString(key, culture) ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
