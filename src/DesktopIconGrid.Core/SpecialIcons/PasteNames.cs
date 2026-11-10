using System.Globalization;

namespace DesktopIconGrid.Core.SpecialIcons;

/// <summary>File names for what is pasted onto a bucket without being a file: a picture or text from the clipboard.</summary>
public static class PasteNames
{
    /// <summary>The largest text pasted as a file; the clipboard can hold far more than anyone means to upload.</summary>
    public const int MaxTextChars = 10 * 1024 * 1024;

    /// <summary><c>Screenshot 2026-11-06 143005.png</c>, as Windows names its own.</summary>
    public static string Image(DateTime now) => $"Screenshot {Stamp(now)}.png";

    /// <summary><c>Pasted text 2026-11-06 143005.txt</c>.</summary>
    public static string Text(DateTime now) => $"Pasted text {Stamp(now)}.txt";

    /// <summary>The longest start of a pasted text shown in the question before it is uploaded.</summary>
    public const int PreviewChars = 200;

    /// <summary>The start of <paramref name="text"/> on one line, for the question: line breaks as spaces, cut with an ellipsis.</summary>
    public static string Preview(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var head = text.Length > PreviewChars ? text[..PreviewChars] : text;
        var line = new string(head.Select(c => char.IsControl(c) ? ' ' : c).ToArray()).Trim();
        return text.Length > PreviewChars ? line + "…" : line;
    }

    private static string Stamp(DateTime now) => now.ToString("yyyy-MM-dd HHmmss", CultureInfo.InvariantCulture);
}
