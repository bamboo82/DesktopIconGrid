namespace DesktopIconGrid.Core.Models;

/// <summary>What a notice reports: a failure or a problem (a warning), or something that went as it should.</summary>
public enum NoticeKind
{
    Information,
    Warning,
}

/// <summary>Where a click on a notice leads (0.12.0 WP1).</summary>
public enum NoticeTarget
{
    /// <summary>Nowhere: the notice says all there is to say.</summary>
    None,

    /// <summary>The log viewer, where a problem's details are.</summary>
    Log,

    /// <summary><see cref="Notice.Path"/> in File Explorer: the file selected, else its folder.</summary>
    Folder,

    /// <summary>The Sync window.</summary>
    Sync,

    /// <summary>Settings.</summary>
    Settings,

    /// <summary>The bar, shown as a click on the tray icon shows it.</summary>
    Bar,
}

/// <summary>
/// A notice shown without interrupting (a tray balloon): its text, its kind, and where a click on it leads. One record
/// goes from the caller to the tray, so that 1.0.0's Windows toast with actions only changes the last step.
/// </summary>
/// <param name="Path">For <see cref="NoticeTarget.Folder"/>: the file (or folder) the click shows.</param>
public sealed record Notice(string Title, string Message, NoticeKind Kind, NoticeTarget Target = NoticeTarget.None, string? Path = null)
{
    /// <summary>A failure or a problem: a warning, and a click opens the log, where its details are.</summary>
    public static Notice Warning(string title, string message) => new(title, message, NoticeKind.Warning, NoticeTarget.Log);

    /// <summary>Something that went as it should: information, and a click leads nowhere.</summary>
    public static Notice Information(string title, string message) => new(title, message, NoticeKind.Information);
}

/// <summary>How a notice is put into a tray balloon (<c>NOTIFYICONDATA</c>), apart from the call that shows it.</summary>
public static class NoticeBalloon
{
    public const uint NiifInfo = 0x01;
    public const uint NiifWarning = 0x02;

    /// <summary>
    /// In quiet time (the first hour of a new account, a presentation) Windows does not show the balloon at all: it is
    /// dropped, not held back.
    /// </summary>
    public const uint NiifRespectQuietTime = 0x80;

    /// <summary>The most characters of the text <c>szInfo</c> takes.</summary>
    public const int MaxText = 255;

    /// <summary>How far back from the cut a line end is looked for.</summary>
    private const int LineSearch = 80;

    /// <summary>
    /// The balloon's icon by kind. Information respects quiet time: during it Windows does not show the notice at all,
    /// and it is only in the log, which a "received" notice can afford. A warning always shows at once, as every notice
    /// did before 0.12.0 (review of WP1, F3).
    /// </summary>
    public static uint Flags(NoticeKind kind) => kind == NoticeKind.Information ? NiifInfo | NiifRespectQuietTime : NiifWarning;

    /// <summary>
    /// The text as the balloon takes it: at most <see cref="MaxText"/> characters. A longer one is cut at a line end when
    /// there is one in the last 80 characters kept, so that a list of failures ends with a whole line and then "…" on
    /// a line of its own; otherwise it is cut there, never inside a surrogate pair, and ends in "…".
    /// </summary>
    public static string Text(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length <= MaxText)
        {
            return text;
        }

        var keep = MaxText - 1; // and the "…"
        var lineEnd = text.LastIndexOf('\n', keep - 1, LineSearch);
        if (lineEnd > 0)
        {
            return text[..lineEnd].TrimEnd('\r') + "\n…";
        }

        if (char.IsHighSurrogate(text[keep - 1]))
        {
            keep--;
        }

        return text[..keep] + "…";
    }
}

/// <summary>The notice a click on the tray's balloon is about: the last one shown, until it is clicked once (WP1 N3).</summary>
public sealed class NoticeMemory
{
    private Notice? _last;

    public void Shown(Notice notice) => _last = notice ?? throw new ArgumentNullException(nameof(notice));

    /// <summary>The balloon was clicked: the notice it showed, forgotten from now on; <see langword="null"/> when there is none.</summary>
    public Notice? Clicked()
    {
        var notice = _last;
        _last = null;
        return notice;
    }
}

/// <summary>What a click on a <see cref="NoticeTarget.Folder"/> notice shows (WP1 N3).</summary>
public static class NoticeFolder
{
    /// <summary>
    /// The file to select in its folder when it is there; else the folder to open (the path itself when it is a folder,
    /// else the file's folder) when that is there; both <see langword="null"/> when neither is, and nothing is opened.
    /// </summary>
    public static (string? Select, string? Open) Resolve(string? path, Func<string, bool> fileExists, Func<string, bool> directoryExists)
    {
        ArgumentNullException.ThrowIfNull(fileExists);
        ArgumentNullException.ThrowIfNull(directoryExists);
        if (string.IsNullOrWhiteSpace(path))
        {
            return (null, null);
        }

        if (fileExists(path))
        {
            return (path, null);
        }

        if (directoryExists(path))
        {
            return (null, path);
        }

        var folder = Path.GetDirectoryName(path);
        return !string.IsNullOrEmpty(folder) && directoryExists(folder) ? (null, folder) : (null, null);
    }
}
