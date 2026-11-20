namespace DesktopIconGrid.Core.Fonts;

/// <summary>
/// The families the app's own text is drawn with, in order (0.13.0 WP4): the font chosen in Settings, else Windows' own
/// for that kind of text (messages, menus, the status line); then the Chinese font when it is used, for what the first
/// lacks; then, after a chosen font, Windows' own, for what neither has.
/// </summary>
public static class UiFontChain
{
    /// <summary>The longest family name taken from a layout; a real one is far shorter.</summary>
    public const int MaxNameLength = 100;

    /// <param name="chosen">The family chosen in Settings; <see langword="null"/> for Windows' own.</param>
    /// <param name="chinese">The Chinese font's family, as WPF refers to it (its file); <see langword="null"/> when Chinese is drawn as Windows draws it.</param>
    /// <param name="windows">Windows' family for this kind of text (<c>Segoe UI</c>).</param>
    public static string Build(string? chosen, string? chinese, string windows)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(windows);
        var families = new List<string> { string.IsNullOrWhiteSpace(chosen) ? windows : chosen.Trim() };
        if (!string.IsNullOrWhiteSpace(chinese))
        {
            families.Add(chinese);
        }

        families.Add(windows);
        return string.Join(", ", families.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Whether <paramref name="name"/> can be a family's name as Settings stores it: not empty, not over
    /// <see cref="MaxNameLength"/>, and nothing WPF would read as another family or a file (<c>,</c>, <c>#</c>, a path,
    /// a control character). A layout that comes from another PC must not make this one load a font from a file.
    /// </summary>
    public static bool IsFamilyName(string? name) =>
        !string.IsNullOrWhiteSpace(name)
        && name.Trim().Length <= MaxNameLength
        && !name.Any(c => c < ' ' || c is ',' or '#' or '/' or '\\' or ':' or '"');
}
