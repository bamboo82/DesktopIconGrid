using DesktopIconGrid.Core.Models;

namespace DesktopIconGrid.Core.Fonts;

/// <summary>Whose text a font is chosen for (0.14.0 WP2).</summary>
public enum FontUse
{
    /// <summary>The bar's names and captions, and the bar shown at the pointer.</summary>
    Bar,

    /// <summary>The other windows, the menus and the tooltips.</summary>
    Dialogs,
}

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

    /// <summary>
    /// The families the bar and the dialogs are drawn with on this PC (0.14.0 WP2): the ones the settings name, a family
    /// this PC lacks being Windows' own (<see langword="null"/>). Dialogs that take the bar's font take it as it is found
    /// here, so a missing one is told of once, for the bar.
    /// </summary>
    /// <param name="isInstalled">Whether this PC has a family.</param>
    /// <param name="missing">Told of each family that is chosen and missing, and for whose text.</param>
    public static (string? Bar, string? Dialogs) Found(LauncherSettings settings, Func<string, bool> isInstalled, Action<string, FontUse>? missing = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(isInstalled);
        string? Here(string? family, FontUse use)
        {
            if (family is null || isInstalled(family))
            {
                return family;
            }

            missing?.Invoke(family, use);
            return null;
        }

        var bar = Here(settings.UiFont, FontUse.Bar);
        return (bar, settings.DialogFont is null ? bar : Here(settings.DialogFamily, FontUse.Dialogs));
    }
}
