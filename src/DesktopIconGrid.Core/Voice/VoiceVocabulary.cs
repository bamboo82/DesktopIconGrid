using DesktopIconGrid.Core.Launching;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Core.SpecialIcons;

namespace DesktopIconGrid.Core.Voice;

/// <summary>What a spoken phrase starts.</summary>
public enum VoiceTargetKind
{
    /// <summary>A shortcut or special icon of the bar, as a click on it.</summary>
    Item,

    /// <summary>A workspace, as its menu entry.</summary>
    Workspace,

    ShowBar,

    HideBar,
}

/// <summary>One thing a phrase can start: <see cref="ItemId"/> for an item, <see cref="Name"/> for a workspace; <see cref="Label"/> is what a list of choices shows.</summary>
public sealed record VoiceTarget(VoiceTargetKind Kind, string Label, Guid? ItemId = null, string? Name = null);

/// <summary>What was heard: a phrase of the vocabulary and how sure the recogniser is (0 to 1).</summary>
public readonly record struct VoiceHeard(string Phrase, double Confidence);

/// <summary>What to do with what was heard.</summary>
public enum VoiceOutcomeKind
{
    /// <summary>Nothing sure enough: say so.</summary>
    NotHeard,

    /// <summary>One target, heard clearly: start it.</summary>
    Start,

    /// <summary>Several candidates (not sure enough, or several items share the phrase): let the user pick.</summary>
    Choose,
}

public sealed record VoiceOutcome(VoiceOutcomeKind Kind, IReadOnlyList<VoiceTarget> Targets);

/// <summary>
/// The phrases voice launch listens for, and what each starts (ROADMAP #11). A phrase is an item's name on the bar or
/// one of its <see cref="LauncherItem.AlsoSay"/> phrases (the names table's column: a Chinese recogniser hears English
/// product names badly and the reverse, so "WinSCP" may also be 檔案傳輸), a workspace's name, or one of the built-in
/// phrases for showing and hiding the bar. Only shortcuts and special icons are started: a group opens a window at its
/// icon, which is of no use to someone who is not looking at the bar. The voice icon itself is left out.
/// </summary>
public sealed class VoiceVocabulary
{
    /// <summary>At or above this the recogniser's first answer is taken (the trial's hits were 0.93 to 0.97).</summary>
    public const double Sure = 0.80;

    /// <summary>Below this an answer is not offered at all.</summary>
    public const double Possible = 0.40;

    /// <summary>The most choices offered.</summary>
    public const int MaxChoices = 3;

    /// <summary>The longest phrase taken; a longer name is not something one says.</summary>
    public const int MaxPhraseLength = 60;

    /// <summary>The most phrases given to the recogniser; a bar has far fewer.</summary>
    public const int MaxPhrases = 2000;

    private static readonly char[] AlsoSaySeparators = [';', '；', '、', '\n', '\r', '|'];

    private readonly Dictionary<string, List<VoiceTarget>> _targets = new(StringComparer.OrdinalIgnoreCase);

    private VoiceVocabulary()
    {
    }

    /// <summary>The phrases to listen for.</summary>
    public IReadOnlyCollection<string> Phrases => _targets.Keys;

    /// <summary>What <paramref name="phrase"/> starts; empty for a phrase that is not in the vocabulary.</summary>
    public IReadOnlyList<VoiceTarget> TargetsOf(string phrase) =>
        Speakable(phrase) is { } key && _targets.TryGetValue(key, out var targets) ? targets : [];

    /// <param name="showBar">The built-in phrases that show the bar, in the language listened in.</param>
    /// <param name="hideBar">The built-in phrases that hide it.</param>
    public static VoiceVocabulary Build(LauncherModel model, IEnumerable<string> showBar, IEnumerable<string> hideBar)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(showBar);
        ArgumentNullException.ThrowIfNull(hideBar);
        var vocabulary = new VoiceVocabulary();

        // Items first: a name the user gave an icon wins the phrase over a built-in one only by being listed first in
        // the choices; nothing is dropped.
        foreach (var item in Items(model))
        {
            var target = new VoiceTarget(VoiceTargetKind.Item, item.Name.Trim(), item.Id);
            foreach (var phrase in PhrasesOf(item))
            {
                vocabulary.Add(phrase, target);
            }
        }

        foreach (var workspace in Workspaces.Names(model.AllShortcuts()))
        {
            vocabulary.Add(workspace, new VoiceTarget(VoiceTargetKind.Workspace, workspace, Name: workspace));
        }

        foreach (var phrase in showBar)
        {
            vocabulary.Add(phrase, new VoiceTarget(VoiceTargetKind.ShowBar, phrase.Trim()));
        }

        foreach (var phrase in hideBar)
        {
            vocabulary.Add(phrase, new VoiceTarget(VoiceTargetKind.HideBar, phrase.Trim()));
        }

        return vocabulary;
    }

    /// <summary>The shortcuts and special icons of the bar and of its groups, in the bar's order.</summary>
    private static IEnumerable<LauncherItem> Items(LauncherModel model)
    {
        foreach (var item in model.Items)
        {
            if (item is GroupItem group)
            {
                foreach (var child in group.Items.Where(IsStartable))
                {
                    yield return child;
                }
            }
            else if (IsStartable(item))
            {
                yield return item;
            }
        }
    }

    private static bool IsStartable(LauncherItem item) =>
        item is ShortcutItem || (item is SpecialIconItem special && !string.Equals(special.Kind, SpecialIconKinds.Voice, StringComparison.OrdinalIgnoreCase));

    /// <summary>An item's phrases: its name, then what <see cref="LauncherItem.AlsoSay"/> lists, without repeats.</summary>
    public static IReadOnlyList<string> PhrasesOf(LauncherItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var phrases = new List<string>();
        foreach (var text in new[] { item.Name }.Concat(SplitAlsoSay(item.AlsoSay)))
        {
            if (Speakable(text) is { } phrase && !phrases.Contains(phrase, StringComparer.OrdinalIgnoreCase))
            {
                phrases.Add(phrase);
            }
        }

        return phrases;
    }

    /// <summary>The phrases of an <em>Also say</em> text: separated by <c>;</c>, <c>；</c>, <c>、</c>, <c>|</c> or line breaks.</summary>
    public static IReadOnlyList<string> SplitAlsoSay(string? alsoSay) =>
        string.IsNullOrWhiteSpace(alsoSay)
            ? []
            : alsoSay.Split(AlsoSaySeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>An <em>Also say</em> text as it is stored: its phrases joined by "; ", or <see langword="null"/> when there are none.</summary>
    public static string? NormalizeAlsoSay(string? alsoSay)
    {
        var phrases = SplitAlsoSay(alsoSay).Select(Speakable).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return phrases.Count == 0 ? null : string.Join("; ", phrases);
    }

    /// <summary>
    /// The phrase as the recogniser gets it, or <see langword="null"/> for one it cannot be given: nothing to say (no
    /// letter or digit), too long, or with characters that are not shown (the rule of everything this app shows before
    /// it acts on it, <see cref="CommandTileSettings.IsShowable"/>). Runs of spaces become one.
    /// </summary>
    public static string? Speakable(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var phrase = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return phrase.Length <= MaxPhraseLength && phrase.Any(char.IsLetterOrDigit) && CommandTileSettings.IsShowable(phrase) ? phrase : null;
    }

    private void Add(string? text, VoiceTarget target)
    {
        if (Speakable(text) is not { } phrase)
        {
            return;
        }

        if (!_targets.TryGetValue(phrase, out var targets))
        {
            if (_targets.Count >= MaxPhrases)
            {
                return;
            }

            _targets[phrase] = targets = [];
        }

        if (!targets.Contains(target))
        {
            targets.Add(target);
        }
    }

    /// <summary>
    /// What to do with the recogniser's answers, best first. A first answer at <see cref="Sure"/> or above that belongs
    /// to one target starts it. Otherwise the targets of the answers at <see cref="Possible"/> or above are offered, at
    /// most <see cref="MaxChoices"/>; a sure phrase that several items share offers those items. Nothing above
    /// <see cref="Possible"/> is nothing heard: starting a program is never a guess.
    /// </summary>
    public VoiceOutcome Decide(IReadOnlyList<VoiceHeard> heard)
    {
        ArgumentNullException.ThrowIfNull(heard);
        var candidates = new List<VoiceTarget>();
        foreach (var answer in heard.Where(h => h.Confidence >= Possible).OrderByDescending(h => h.Confidence))
        {
            foreach (var target in TargetsOf(answer.Phrase))
            {
                if (!candidates.Contains(target))
                {
                    candidates.Add(target);
                }
            }
        }

        if (candidates.Count == 0)
        {
            return new VoiceOutcome(VoiceOutcomeKind.NotHeard, []);
        }

        var best = heard.MaxBy(h => h.Confidence);
        var bestTargets = TargetsOf(best.Phrase);
        if (best.Confidence >= Sure && bestTargets.Count == 1)
        {
            return new VoiceOutcome(VoiceOutcomeKind.Start, [bestTargets[0]]);
        }

        if (best.Confidence >= Sure)
        {
            candidates = [.. bestTargets]; // heard clearly; the phrase is shared
        }

        return new VoiceOutcome(VoiceOutcomeKind.Choose, candidates.Take(MaxChoices).ToList());
    }
}

/// <summary>Which of the PC's recognisers voice launch listens with.</summary>
public static class VoiceLanguage
{
    /// <summary>
    /// The culture name to listen in, or <see langword="null"/> when this PC has no recogniser. The setting wins when
    /// this PC has a recogniser for it (it may come from another PC through sync, which has others). Otherwise the
    /// bar's language: the same culture, else the usual region of its script for Chinese (zh-Hant: zh-TW, then zh-HK;
    /// zh-Hans: zh-CN, then zh-SG), else any region of the same language; and when there is none of those, the first
    /// recogniser the PC lists.
    /// </summary>
    public static string? Pick(string? setting, string? barLanguage, IReadOnlyList<string> installed)
    {
        ArgumentNullException.ThrowIfNull(installed);
        if (installed.Count == 0)
        {
            return null;
        }

        string? Find(string? name) =>
            string.IsNullOrWhiteSpace(name) ? null : installed.FirstOrDefault(i => string.Equals(i, name.Trim(), StringComparison.OrdinalIgnoreCase));

        if (Find(setting) is { } chosen)
        {
            return chosen;
        }

        var bar = barLanguage?.Trim() ?? string.Empty;
        string[] wanted = bar.ToUpperInvariant() switch
        {
            "ZH-HANT" or "ZH-TW" or "ZH-HK" or "ZH-MO" or "ZH-CHT" => [bar, "zh-TW", "zh-HK"],
            "ZH-HANS" or "ZH-CN" or "ZH-SG" or "ZH-CHS" or "ZH" => [bar, "zh-CN", "zh-SG"],
            _ => [bar],
        };
        if (wanted.Select(Find).FirstOrDefault(found => found is not null) is { } same)
        {
            return same;
        }

        var language = bar.Split('-')[0];
        var chinese = language.Equals("zh", StringComparison.OrdinalIgnoreCase);
        return (language.Length > 0 && !chinese
                   ? installed.FirstOrDefault(i => i.Split('-')[0].Equals(language, StringComparison.OrdinalIgnoreCase))
                   : null)
               ?? installed[0];
    }
}
