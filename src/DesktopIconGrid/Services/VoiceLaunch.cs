using System.Globalization;
using DesktopIconGrid.Core.Diagnostics;
using DesktopIconGrid.Core.Launching;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Core.SpecialIcons;
using DesktopIconGrid.Core.Voice;
using DesktopIconGrid.Resources;
using DesktopIconGrid.SpecialIcons;
using DesktopIconGrid.ViewModels;

namespace DesktopIconGrid.Services;

/// <summary>What voice launch shows while it works (a small window; tests record the calls).</summary>
public interface IVoiceView
{
    /// <summary>Listening has begun, in <paramref name="language"/>; <paramref name="cancel"/> stops it without a word.</summary>
    void Listening(string language, Action cancel);

    /// <summary>A last word (what is started, or what went wrong); goes away by itself.</summary>
    void Say(string message);

    /// <summary>Nothing more to show: the window goes away without a word (cancelled, or let go before anything was said).</summary>
    void Done();

    /// <summary>Lets the user pick one of <paramref name="choices"/>; <see langword="null"/> when they pick none.</summary>
    Task<VoiceChoiceView?> ChooseAsync(IReadOnlyList<VoiceChoiceView> choices);
}

/// <summary>
/// One button of the choice window (review 27, the design): <see cref="Name"/> in bold, <see cref="Detail"/> saying what
/// it is and what it starts, and a warning line when the phrase came from another PC (<see cref="CameFromAnotherPc"/>).
/// The two texts are also the button's snapshot: what it starts must still read so when it is clicked.
/// </summary>
public sealed record VoiceChoiceView(VoiceChoice Choice, string Name, string Detail)
{
    public bool CameFromAnotherPc => Choice.Unapproved;
}

/// <summary>
/// Voice launch (ROADMAP #11): on its hotkey, a click on the Voice icon or <c>--voice</c>, the bar listens for one
/// spoken name and starts what is called so, as a click on it would. It never listens on its own or all the time, and
/// it only matches the names on the bar (<see cref="VoiceVocabulary"/>): nothing that is said is typed, sent or kept.
/// A name heard clearly starts; one heard less clearly, close to another, or by a phrase not approved on this PC is
/// offered with the other candidates; anything less starts nothing. The hotkey is push to talk when held
/// (<see cref="Release"/>), and a plain trigger when tapped.
/// </summary>
public sealed class VoiceLaunch(LauncherViewModel bar, IVoiceEar ear, IVoiceView view, Action showBar, Action hideBar)
{
    /// <summary>The longest one listening lasts, whatever the recogniser does.</summary>
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(15);

    /// <summary>What the recogniser still gets after the hotkey is let go: the end of a word said while letting go.</summary>
    public static readonly TimeSpan ReleaseGrace = TimeSpan.FromMilliseconds(700);

    /// <summary>The longest location a button shows; a longer one is trimmed in the middle.</summary>
    private const int MaxLocationShown = 60;

    /// <summary>The longest command line a button shows.</summary>
    private const int MaxCommandShown = 80;

    /// <summary>How many members of a workspace its button names.</summary>
    private const int MembersNamed = 3;

    private bool _busy;
    private CancellationTokenSource? _listening;
    private bool _released;
    private bool _cancelled;

    /// <summary>Whether it is listening now.</summary>
    public bool IsListening => _listening is not null;

    /// <summary>
    /// The held hotkey was let go (push to talk): listening ends after <see cref="ReleaseGrace"/>. What was heard by
    /// then is acted on as usual; when nothing was, the window just goes away, since letting go is how one cancels.
    /// </summary>
    public void Release()
    {
        if (_listening is { } listening)
        {
            _released = true;
            try
            {
                listening.CancelAfter(ReleaseGrace);
            }
            catch (ObjectDisposedException)
            {
                // It ended this very moment.
            }
        }
    }

    /// <summary>
    /// Stops the listening now, without a word, and drops whatever answer it may still bring (review 27): Cancel in the
    /// window, a second press of the hotkey, the app closing.
    /// </summary>
    public void Cancel()
    {
        if (_listening is { } listening)
        {
            _cancelled = true;
            try
            {
                listening.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // It ended this very moment.
            }
        }
    }

    /// <summary>The recogniser language in use with this PC's settings, or <see langword="null"/> when this PC has none.</summary>
    public string? Language => VoiceLanguage.Pick(bar.Local.VoiceLanguage, CultureInfo.CurrentUICulture.Name, ear.InstalledLanguages());

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

            var vocabulary = VoiceVocabulary.Build(
                bar.Model,
                BuiltIn("Voice_ShowBarPhrases", language),
                BuiltIn("Voice_HideBarPhrases", language),
                (id, phrase) => bar.Trust.IsApproved(VoiceVocabulary.ApprovalHash(id, phrase)));
            if (vocabulary.LeftOut > 0)
            {
                AppLog.Default.Info("Voice", $"{vocabulary.LeftOut} phrase(s) left out: the recogniser gets at most {VoiceVocabulary.MaxPhrases}, the bar's own and the names first");
            }

            if (vocabulary.Phrases.Count == 0)
            {
                view.Say(Strings.Voice_NothingToSay);
                return;
            }

            using var cancellation = new CancellationTokenSource(Limit);
            _released = false;
            _cancelled = false;
            _listening = cancellation;
            view.Listening(language, Cancel);
            VoiceListenResult result;
            try
            {
                result = await ear.ListenAsync(language, vocabulary.Phrases, cancellation.Token);
            }
            finally
            {
                _listening = null;
            }

            if (_cancelled)
            {
                view.Done(); // nothing is said, and an answer that came after all is dropped
                return;
            }

            if (result.Problem is not null)
            {
                view.Say(string.Format(Strings.Voice_Problem, result.Problem));
                return;
            }

            var outcome = vocabulary.Decide(result.Heard);
            switch (outcome.Kind)
            {
                case VoiceOutcomeKind.Start:
                    Start(outcome.Choices[0].Target);
                    break;
                case VoiceOutcomeKind.Choose:
                    var buttons = outcome.Choices.Select(Show).OfType<VoiceChoiceView>().ToList();
                    if (buttons.Count == 0)
                    {
                        view.Say(Strings.Voice_NotHeard);
                    }
                    else if (await view.ChooseAsync(buttons) is { } picked)
                    {
                        Pick(picked);
                    }

                    break;
                default:
                    if (_released)
                    {
                        view.Done(); // let go without a name: cancelled, nothing to explain
                    }
                    else
                    {
                        view.Say(Strings.Voice_NotHeard);
                    }

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

    /// <summary>
    /// A button was clicked. The window waited up to 20 seconds, and the bar may have changed meanwhile (a sync load): what
    /// the button said must still be so, or nothing starts. Picking a phrase that came from another PC, after it was
    /// heard, approves it here (review 27 B3b).
    /// </summary>
    private void Pick(VoiceChoiceView picked)
    {
        if (Show(picked.Choice) is not { } now || now.Name != picked.Name || now.Detail != picked.Detail)
        {
            AppLog.Default.Info("Voice", "The bar changed while the choices were shown; nothing was started");
            view.Say(Strings.Voice_BarChanged);
            return;
        }

        if (picked.Choice.Unapproved && picked.Choice.Target.ItemId is { } id)
        {
            bar.Trust.Approve(VoiceVocabulary.ApprovalHash(id, picked.Choice.Phrase));
        }

        Start(picked.Choice.Target);
    }

    /// <summary>Starts the target as a click does, and says so only when something started (an approval may be cancelled).</summary>
    private void Start(VoiceTarget target)
    {
        AppLog.Default.Info("Voice", $"Starting by voice: {target.Kind}");
        var started = target.Kind switch
        {
            VoiceTargetKind.Item => target.ItemId is { } id && bar.Model.Find(id) is { } item and not GroupItem
                && bar.ActivateAndReport(bar.GetOrCreateViewModel(item), anchor: null, ActivationModifiers.None) == LaunchOutcome.Started,
            VoiceTargetKind.Workspace => target.Name is { } workspace && Run(() => _ = bar.StartWorkspaceAsync(workspace)),
            VoiceTargetKind.ShowBar => Run(showBar),
            VoiceTargetKind.HideBar => Run(hideBar),
            _ => false,
        };

        if (started)
        {
            view.Say(string.Format(Strings.Voice_Starting, target.Label));
        }
        else
        {
            view.Done(); // cancelled at an approval or a question, or failed (the bar says why)
        }
    }

    private static bool Run(Action action)
    {
        action();
        return true;
    }

    /// <summary>The button for a choice: its name and what it is and starts; <see langword="null"/> when it is no longer on the bar.</summary>
    internal VoiceChoiceView? Show(VoiceChoice choice)
    {
        var target = choice.Target;
        switch (target.Kind)
        {
            case VoiceTargetKind.Item:
                if (target.ItemId is not { } id || bar.Model.Find(id, out var group) is not { } item || item is GroupItem)
                {
                    return null;
                }

                return new VoiceChoiceView(choice, Shown(item.Name), DetailOf(item, group));
            case VoiceTargetKind.Workspace:
                var members = target.Name is null ? [] : Workspaces.Members(bar.Model.AllShortcuts(), target.Name);
                if (members.Count == 0)
                {
                    return null;
                }

                var named = string.Join(Strings.Voice_ListSeparator, members.Take(MembersNamed).Select(member => Shown(member.Name)))
                            + (members.Count > MembersNamed ? "…" : string.Empty);
                return new VoiceChoiceView(choice, Shown(target.Label), string.Format(Strings.Voice_Workspace, members.Count, named));
            default:
                return new VoiceChoiceView(choice, Shown(target.Label), Strings.Voice_TheBar);
        }
    }

    private string DetailOf(LauncherItem item, GroupItem? group) => item switch
    {
        ShortcutItem shortcut when group is null => string.Format(Strings.Voice_Shortcut, Shown(TrimMiddle(shortcut.EffectiveLocation, MaxLocationShown))),
        ShortcutItem shortcut => string.Format(Strings.Voice_ShortcutInGroup, Shown(group.Name), Shown(TrimMiddle(shortcut.EffectiveLocation, MaxLocationShown))),
        SpecialIconItem tile when string.Equals(tile.Kind, SpecialIconKinds.CommandTile, StringComparison.OrdinalIgnoreCase) => CommandDetail(tile),
        SpecialIconItem special => bar.GetOrCreateViewModel(special).SpecialHandler.DisplayName,
        _ => string.Empty,
    };

    /// <summary>A command tile's line: its program and arguments, and that it waits for an approval here while it does.</summary>
    private string CommandDetail(SpecialIconItem tile)
    {
        var settings = CommandTileSettings.From(tile);
        var line = $"{settings.Program} {settings.Arguments}".Trim();
        var detail = string.Format(Strings.Voice_CommandTile, Shown(line.Length <= MaxCommandShown ? line : line[..(MaxCommandShown - 1)] + "…"));
        return bar.Trust.IsApproved(settings.Hash()) ? detail : detail + Strings.Voice_NotApprovedHere;
    }

    /// <summary>A name or location as a button shows it: as written when it can be shown, else "(…)".</summary>
    private static string Shown(string? text) =>
        !string.IsNullOrWhiteSpace(text) && CommandTileSettings.IsShowable(text) ? text.Trim() : "(…)";

    /// <summary><paramref name="text"/>, or its start and end with "…" between when it is longer than <paramref name="max"/>.</summary>
    internal static string TrimMiddle(string? text, int max)
    {
        var value = text ?? string.Empty;
        if (value.Length <= max)
        {
            return value;
        }

        var head = (max - 1) / 2;
        return value[..head] + "…" + value[^(max - 1 - head)..];
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
