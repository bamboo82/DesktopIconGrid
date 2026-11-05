using System.Globalization;

namespace DesktopIconGrid.Core;

/// <summary>An item the bar's search looks at.</summary>
/// <param name="Id">The item.</param>
/// <param name="Name">The name given on the bar.</param>
/// <param name="OriginalName">The name derived from the location (what the names table calls the original name).</param>
/// <param name="FileName">The file name of the location without its extension.</param>
public sealed record SearchCandidate(Guid Id, string Name, string? OriginalName = null, string? FileName = null);

/// <summary>
/// Type-to-find over the bar's own items: the given name first (whole, start, start of a word, word initials such as
/// <c>vsc</c> for Visual Studio Code, anywhere), then the original name and the file name. Matching ignores case and
/// width and works on any script, so Chinese is typed directly through the IME; there is no pinyin table.
/// </summary>
public static class ItemSearch
{
    private const CompareOptions Loose = CompareOptions.IgnoreCase | CompareOptions.IgnoreWidth | CompareOptions.IgnoreKanaType;

    /// <summary>The ids of the candidates that match <paramref name="query"/>, best first; equal matches keep their order.</summary>
    public static IReadOnlyList<Guid> Rank(string? query, IEnumerable<SearchCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        var text = query?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            return [];
        }

        return candidates
            .Select((candidate, index) => (candidate.Id, Score: Score(text, candidate), index))
            .Where(r => r.Score > 0)
            .OrderByDescending(r => r.Score)
            .ThenBy(r => r.index)
            .Select(r => r.Id)
            .ToList();
    }

    /// <summary>How well <paramref name="query"/> matches; 0 for no match.</summary>
    public static int Score(string query, SearchCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        var text = query?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            return 0;
        }

        var name = candidate.Name?.Trim() ?? string.Empty;
        if (Compare(name, text) == 0)
        {
            return 100;
        }

        if (StartsWith(name, text))
        {
            return 90;
        }

        if (Words(name).Any(word => StartsWith(word, text)))
        {
            return 80;
        }

        if (text.Length >= 2 && StartsWith(Initials(name), text))
        {
            return 70;
        }

        if (Contains(name, text))
        {
            return 60;
        }

        foreach (var (other, score) in new[] { (candidate.OriginalName, 50), (candidate.FileName, 40) })
        {
            if (!string.IsNullOrWhiteSpace(other) && (Contains(other, text) || (text.Length >= 2 && StartsWith(Initials(other), text))))
            {
                return score;
            }
        }

        return 0;
    }

    /// <summary>The first letter of every word: "Visual Studio Code" → "VSC", "WinSCP" → "WS", "my-tool_v2" → "mtv2".</summary>
    public static string Initials(string name) => string.Concat(Words(name).Select(word => word[0]));

    /// <summary>
    /// The words of a name: split at spaces and punctuation, before an upper-case letter that follows a lower-case
    /// one, and around runs of digits. Characters of scripts without spaces (Chinese) each count as a word.
    /// </summary>
    public static IEnumerable<string> Words(string name)
    {
        var word = new System.Text.StringBuilder();
        char previous = '\0';
        foreach (var c in name ?? string.Empty)
        {
            var boundary = !char.IsLetterOrDigit(c)
                           || (char.IsUpper(c) && char.IsLower(previous))
                           || (char.IsDigit(c) != char.IsDigit(previous) && word.Length > 0)
                           || IsIdeograph(c)
                           || IsIdeograph(previous);
            if (boundary && word.Length > 0)
            {
                yield return word.ToString();
                word.Clear();
            }

            if (char.IsLetterOrDigit(c))
            {
                word.Append(c);
            }

            previous = c;
        }

        if (word.Length > 0)
        {
            yield return word.ToString();
        }
    }

    private static bool IsIdeograph(char c) => c is >= '㐀' and <= '鿿' or >= '豈' and <= '﫿';

    private static CompareInfo Culture => CultureInfo.CurrentCulture.CompareInfo;

    private static int Compare(string a, string b) => Culture.Compare(a, b, Loose);

    private static bool StartsWith(string value, string prefix) => Culture.IsPrefix(value, prefix, Loose);

    private static bool Contains(string value, string part) => Culture.IndexOf(value, part, Loose) >= 0;
}
