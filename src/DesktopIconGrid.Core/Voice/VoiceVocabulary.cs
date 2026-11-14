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

/// <summary>One thing a phrase can start: <see cref="ItemId"/> for an item, <see cref="Name"/> for a workspace; <see cref="Label"/> is its name.</summary>
public sealed record VoiceTarget(VoiceTargetKind Kind, string Label, Guid? ItemId = null, string? Name = null);

/// <summary>
/// One answer of the recogniser: which phrase of <see cref="VoiceVocabulary.Phrases"/> (its index, the grammar's semantic
/// value; -1 for an answer that is none of them) and how sure it is (0 to 1). Answers come back by index, not by text, so
/// how the recogniser spells or punctuates a phrase does not matter (review 27 B3a).
/// </summary>
public readonly record struct VoiceHeard(int Phrase, double Confidence);

/// <summary>What to do with what was heard.</summary>
public enum VoiceOutcomeKind
{
    /// <summary>Nothing sure enough: say so.</summary>
    NotHeard,

    /// <summary>One target, heard clearly by a phrase that may start it: start it.</summary>
    Start,

    /// <summary>Candidates to pick from: not sure enough, shared, close to another, or by a phrase not approved here.</summary>
    Choose,
}

/// <summary>
/// A candidate: its target, the phrase it was heard by (folded, <see cref="VoiceVocabulary.Fold"/>), and whether that
/// phrase is an <em>Also say</em> phrase that is not approved on this PC (it came from another PC; picking it approves it).
/// </summary>
public sealed record VoiceChoice(VoiceTarget Target, string Phrase, bool Unapproved);

public sealed record VoiceOutcome(VoiceOutcomeKind Kind, IReadOnlyList<VoiceChoice> Choices)
{
    public IReadOnlyList<VoiceTarget> Targets => [.. Choices.Select(choice => choice.Target)];
}

/// <summary>
/// The phrases voice launch listens for, and what each starts (ROADMAP #11). A phrase is an item's name on the bar or
/// one of its <see cref="LauncherItem.AlsoSay"/> phrases (the names table's column: a Chinese recogniser hears English
/// product names badly and the reverse, so "WinSCP" may also be 檔案傳輸), a workspace's name, or one of the built-in
/// phrases for showing and hiding the bar. Only shortcuts and special icons are started: a group opens a window at its
/// icon, which is of no use to someone who is not looking at the bar. The voice icon itself is left out.
/// Phrases are told apart folded (<see cref="Fold"/>): "Hide bar.", "hide  bar" and a full-width spelling are one phrase.
/// An item's own name, a workspace and the built-in phrases may start at once; an <em>Also say</em> phrase only when it
/// is approved on this PC (review 27 B3b), since it may have arrived with a synced bar.
/// </summary>
public sealed class VoiceVocabulary
{
    /// <summary>At or above this the recogniser's first answer may be taken (the trial's hits were 0.93 to 0.97).</summary>
    public const double Sure = 0.80;

    /// <summary>Below this an answer is not offered at all.</summary>
    public const double Possible = 0.40;

    /// <summary>An answer for another target this close to the best one makes it a choice (Word 0.85, WordPad 0.84).</summary>
    public const double CloseCall = 0.10;

    /// <summary>The most choices offered.</summary>
    public const int MaxChoices = 3;

    /// <summary>The longest phrase taken; a longer name is not something one says.</summary>
    public const int MaxPhraseLength = 60;

    /// <summary>The most <em>Also say</em> phrases an item keeps (review 27 V3).</summary>
    public const int MaxAlsoSayPhrases = 8;

    /// <summary>The most phrases given to the recogniser; a bar has far fewer.</summary>
    public const int MaxPhrases = 2000;

    private static readonly char[] AlsoSaySeparators = [';', '；', '、', '\n', '\r', '|'];

    private readonly List<string> _phrases = [];
    private readonly Dictionary<string, int> _indexes = new(StringComparer.Ordinal);
    private readonly List<List<Entry>> _entries = [];

    private VoiceVocabulary()
    {
    }

    /// <summary>The phrases to listen for, as written; an answer names one by its index here.</summary>
    public IReadOnlyList<string> Phrases => _phrases;

    /// <summary>How many phrases were left out by <see cref="MaxPhrases"/> (the built-in phrases and the names go in first).</summary>
    public int LeftOut { get; private set; }

    /// <summary>The index of <paramref name="phrase"/> (folded), or -1 when it is not in the vocabulary.</summary>
    public int IndexOf(string? phrase) => phrase is not null && _indexes.TryGetValue(Fold(phrase), out var index) ? index : -1;

    /// <summary>What <paramref name="phrase"/> starts; empty for a phrase that is not in the vocabulary.</summary>
    public IReadOnlyList<VoiceTarget> TargetsOf(string phrase) => [.. EntriesOf(IndexOf(phrase)).Select(entry => entry.Target)];

    /// <summary>Whether <paramref name="phrase"/> may start <paramref name="target"/> without a question.</summary>
    public bool MayStart(string phrase, VoiceTarget target) => EntriesOf(IndexOf(phrase)).Any(entry => entry.Target == target && entry.MayStart);

    private IReadOnlyList<Entry> EntriesOf(int index) => index >= 0 && index < _entries.Count ? _entries[index] : [];

    /// <param name="model">The bar.</param>
    /// <param name="showBar">The built-in phrases that show the bar, in the language listened in.</param>
    /// <param name="hideBar">The built-in phrases that hide it.</param>
    /// <param name="isApproved">Whether an item's <em>Also say</em> phrase (folded) is approved on this PC; none is without it.</param>
    public static VoiceVocabulary Build(LauncherModel model, IEnumerable<string> showBar, IEnumerable<string> hideBar, Func<Guid, string, bool>? isApproved = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(showBar);
        ArgumentNullException.ThrowIfNull(hideBar);
        var builtIn = new List<Candidate>();
        builtIn.AddRange(showBar.Select(phrase => new Candidate(phrase, new VoiceTarget(VoiceTargetKind.ShowBar, phrase.Trim()), MayStart: true)));
        builtIn.AddRange(hideBar.Select(phrase => new Candidate(phrase, new VoiceTarget(VoiceTargetKind.HideBar, phrase.Trim()), MayStart: true)));
        var builtInKeys = builtIn.Select(candidate => Speakable(candidate.Text)).OfType<string>().Select(Fold).ToHashSet(StringComparer.Ordinal);

        var names = new List<Candidate>();
        var alsoSay = new List<Candidate>();
        foreach (var item in Items(model))
        {
            var target = new VoiceTarget(VoiceTargetKind.Item, item.Name.Trim(), item.Id);
            names.Add(new Candidate(item.Name, target, MayStart: true));
            foreach (var phrase in AlsoSayOf(item).Where(phrase => !builtInKeys.Contains(Fold(phrase)))) // never a built-in phrase (review 27 B3a)
            {
                alsoSay.Add(new Candidate(phrase, target, MayStart: isApproved?.Invoke(item.Id, Fold(phrase)) == true));
            }
        }

        var workspaces = Workspaces.Names(model.AllShortcuts())
            .Select(workspace => new Candidate(workspace, new VoiceTarget(VoiceTargetKind.Workspace, workspace, Name: workspace), MayStart: true))
            .ToList();

        // Which phrases get in when there are too many: the built-in ones, then names, then Also say (review 27 V3).
        var vocabulary = new VoiceVocabulary();
        var admitted = new HashSet<string>(StringComparer.Ordinal);
        var refused = new HashSet<string>(StringComparer.Ordinal);
        foreach (var candidate in builtIn.Concat(names).Concat(workspaces).Concat(alsoSay))
        {
            if (Speakable(candidate.Text) is { } phrase && !admitted.Contains(Fold(phrase)))
            {
                (admitted.Count < MaxPhrases ? admitted : refused).Add(Fold(phrase));
            }
        }

        vocabulary.LeftOut = refused.Count;

        // In the bar's order, which is the order of the choices: items (name, then Also say), workspaces, the bar.
        var itemOrder = names.Select((name, index) => (name.Target, index)).ToDictionary(entry => entry.Target, entry => entry.index);
        var itemCandidates = names.Concat(alsoSay).OrderBy(candidate => itemOrder[candidate.Target]); // stable: a name before its Also say
        foreach (var candidate in itemCandidates.Concat(workspaces).Concat(builtIn))
        {
            if (Speakable(candidate.Text) is { } phrase && admitted.Contains(Fold(phrase)))
            {
                vocabulary.Add(phrase, candidate.Target, candidate.MayStart);
            }
        }

        return vocabulary;
    }

    private sealed record Candidate(string? Text, VoiceTarget Target, bool MayStart);

    private sealed record Entry(VoiceTarget Target, bool MayStart);

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

    /// <summary>An item's phrases: its name, then what <see cref="LauncherItem.AlsoSay"/> lists, without repeats (folded).</summary>
    public static IReadOnlyList<string> PhrasesOf(LauncherItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var phrases = new List<string>();
        foreach (var text in new[] { item.Name }.Concat(AlsoSayOf(item)))
        {
            if (Speakable(text) is { } phrase && !phrases.Any(other => Fold(other) == Fold(phrase)))
            {
                phrases.Add(phrase);
            }
        }

        return phrases;
    }

    /// <summary>An item's <em>Also say</em> phrases, tidied: speakable, no repeats, none that is its name, at most <see cref="MaxAlsoSayPhrases"/>.</summary>
    private static IEnumerable<string> AlsoSayOf(LauncherItem item) =>
        SplitAlsoSay(VoiceVocabulary.NormalizeAlsoSay(item.AlsoSay)).Where(phrase => Speakable(item.Name) is not { } name || Fold(name) != Fold(phrase));

    /// <summary>The phrases of an <em>Also say</em> text: separated by <c>;</c>, <c>；</c>, <c>、</c>, <c>|</c> or line breaks.</summary>
    public static IReadOnlyList<string> SplitAlsoSay(string? alsoSay) =>
        string.IsNullOrWhiteSpace(alsoSay)
            ? []
            : alsoSay.Split(AlsoSaySeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>
    /// An <em>Also say</em> text as it is stored (on every load and copy): its speakable phrases without repeats (folded),
    /// the first <see cref="MaxAlsoSayPhrases"/>, joined by "; "; <see langword="null"/> when there are none.
    /// </summary>
    public static string? NormalizeAlsoSay(string? alsoSay)
    {
        var phrases = new List<string>();
        foreach (var phrase in SplitAlsoSay(alsoSay).Select(Speakable).OfType<string>())
        {
            if (phrases.Count < MaxAlsoSayPhrases && !phrases.Any(other => Fold(other) == Fold(phrase)))
            {
                phrases.Add(phrase);
            }
        }

        return phrases.Count == 0 ? null : string.Join("; ", phrases);
    }

    /// <summary>
    /// The phrase as the recogniser gets it, or <see langword="null"/> for one it cannot be given: nothing to say (no
    /// letter or digit), too long, a <c>"</c> (System.Speech refuses a list with one, review 27 V2), or characters that
    /// are not shown (the rule of everything this app shows before it acts on it, <see cref="CommandTileSettings.IsShowable"/>).
    /// Runs of spaces become one.
    /// </summary>
    public static string? Speakable(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var phrase = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return phrase.Length <= MaxPhraseLength && phrase.Any(char.IsLetterOrDigit) && !phrase.Contains('"', StringComparison.Ordinal) && CommandTileSettings.IsShowable(phrase)
            ? phrase
            : null;
    }

    /// <summary>
    /// How phrases are compared (review 27 B3a): NFKC (full-width letters become plain ones), lower case, and only letters
    /// and digits, with the spaces between words kept as one; anything else (punctuation, symbols) is dropped.
    /// </summary>
    public static string Fold(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var normalized = text.Normalize(System.Text.NormalizationForm.FormKC).ToLowerInvariant();
        var builder = new System.Text.StringBuilder(normalized.Length);
        var space = false;
        foreach (var rune in normalized.EnumerateRunes())
        {
            if (System.Text.Rune.IsLetterOrDigit(rune))
            {
                if (space && builder.Length > 0)
                {
                    builder.Append(' ');
                }

                builder.Append(rune.ToString());
                space = false;
            }
            else if (System.Text.Rune.IsWhiteSpace(rune))
            {
                space = true;
            }
        }

        return builder.ToString();
    }

    /// <summary>The mark of an <em>Also say</em> phrase approved on this PC, for <see cref="CommandTrust"/> (review 27 B3b).</summary>
    public static string ApprovalHash(Guid itemId, string phrase) =>
        Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"voice-phrase/1\n{itemId:N}\n{Fold(phrase)}")));

    private void Add(string phrase, VoiceTarget target, bool mayStart)
    {
        var key = Fold(phrase);
        if (!_indexes.TryGetValue(key, out var index))
        {
            index = _phrases.Count;
            _indexes[key] = index;
            _phrases.Add(phrase);
            _entries.Add([]);
        }

        var entries = _entries[index];
        var at = entries.FindIndex(entry => entry.Target == target);
        if (at < 0)
        {
            entries.Add(new Entry(target, mayStart));
        }
        else if (mayStart && !entries[at].MayStart)
        {
            entries[at] = entries[at] with { MayStart = true };
        }
    }

    /// <summary>
    /// What to do with the recogniser's answers (review 27 B3c). The best answer starts its target only when it is at
    /// <see cref="Sure"/> or above, is a phrase of the vocabulary with exactly one target, may start it (not an unapproved
    /// <em>Also say</em> phrase), and no answer for another target is within <see cref="CloseCall"/> of it. Otherwise the
    /// targets of the answers at <see cref="Possible"/> or above are offered, best first, at most <see cref="MaxChoices"/>;
    /// with none, nothing was heard. Starting a program is never a guess, and a choice always has something to pick.
    /// </summary>
    public VoiceOutcome Decide(IReadOnlyList<VoiceHeard> heard)
    {
        ArgumentNullException.ThrowIfNull(heard);
        var answers = heard.OrderByDescending(answer => answer.Confidence).ToList();
        if (answers.Count == 0)
        {
            return new VoiceOutcome(VoiceOutcomeKind.NotHeard, []);
        }

        var best = answers[0];
        if (best.Confidence >= Sure
            && EntriesOf(best.Phrase) is [{ MayStart: true } only]
            && !answers.Skip(1).Any(other => other.Confidence >= best.Confidence - CloseCall && EntriesOf(other.Phrase).Any(entry => entry.Target != only.Target)))
        {
            return new VoiceOutcome(VoiceOutcomeKind.Start, [new VoiceChoice(only.Target, Fold(_phrases[best.Phrase]), Unapproved: false)]);
        }

        var choices = new List<VoiceChoice>();
        foreach (var answer in answers.Where(answer => answer.Confidence >= Possible))
        {
            foreach (var entry in EntriesOf(answer.Phrase))
            {
                if (!choices.Any(choice => choice.Target == entry.Target))
                {
                    choices.Add(new VoiceChoice(entry.Target, Fold(_phrases[answer.Phrase]), Unapproved: !entry.MayStart));
                }
            }
        }

        return choices.Count == 0
            ? new VoiceOutcome(VoiceOutcomeKind.NotHeard, [])
            : new VoiceOutcome(VoiceOutcomeKind.Choose, [.. choices.Take(MaxChoices)]);
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
