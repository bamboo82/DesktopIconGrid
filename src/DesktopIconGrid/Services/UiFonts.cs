using System.IO;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using DesktopIconGrid.Core.Diagnostics;
using DesktopIconGrid.Core.Fonts;

namespace DesktopIconGrid.Services;

/// <summary>
/// The font of the app's own text (0.12.0 WP5). It is the Latin font each window has had, then, for what that font
/// lacks (Chinese, full-width punctuation, bopomofo, kana), Taipei Sans TC Light from its file, when this PC has it and
/// the setting wants it. Latin letters and digits stay as they were; sizes do not change.
/// <list type="bullet">
/// <item>Since 0.14.0 (WP2) there are two: the bar's and the dialogs'. The bar window, its group popups (they inherit
/// from it) and the bar shown at the pointer take theirs from <see cref="BarFamilyKey"/> (<see cref="ApplyBar"/>); every
/// other window from <see cref="FamilyKey"/> (<see cref="Apply"/>), so the bar follows a change at once and a dialog
/// when it is next opened. Each is the family chosen for it, the Chinese font, then Windows' own.</item>
/// <item>Menus, context menus (the bar's too), tooltips (over the bar too) and the tray menu are windows of their own:
/// their styles, classic and Fluent, read the system's menu, status and message font keys, which are set beside them in
/// the app's resources from the dialogs' family and size.</item>
/// <item>The font is referred to by its file, never by a family name: a PC that also has the Regular or Bold of the
/// family installed would otherwise answer a normal-weight request with Regular.</item>
/// <item>Consolas text (the logs, the details of an approval) takes the font after Consolas, from
/// <see cref="CodeFamilyKey"/> and <see cref="LogFamilyKey"/> (0.12.1, review 28 F2: the plan said "the chain after it
/// for Chinese", and their Chinese was Windows' font).</item>
/// </list>
/// Left alone: text icons, gauges' numbers and symbol icons, which set fonts of their own, and Windows' own message
/// boxes, which the app does not draw. The bar's text size is not here: the bar's view model reads it from the settings
/// (names, captions, cells, the empty bar's hint), as since 0.13.0.
/// </summary>
public static class UiFonts
{
    /// <summary>The resource the font of every window but the bar's refers to: the dialogs' (0.14.0 WP2).</summary>
    public const string FamilyKey = "App.UiFontFamily";

    /// <summary>The resource the bar window's font, and so its group popups', refers to, and the bar's at the pointer (0.14.0 WP2).</summary>
    public const string BarFamilyKey = "App.BarFontFamily";

    /// <summary>Consolas, then Courier New: commands and their details.</summary>
    public const string CodeFamilyKey = "App.CodeFontFamily";

    /// <summary>Consolas, then Segoe UI: the log's lines.</summary>
    public const string LogFamilyKey = "App.LogFontFamily";

    private static readonly FontFamily Consolas = new("Consolas");

    /// <summary>The Taipei Sans file in use, or <see langword="null"/> when Chinese is drawn as Windows draws it.</summary>
    public static string? ChineseFontFile { get; private set; }

    /// <summary>The Chinese font as WPF refers to it (its file), for the sample in Settings; <see langword="null"/> without one.</summary>
    public static string? ChineseFamilySource { get; private set; }

    /// <summary>The dialogs' family, chosen in Settings and found on this PC (0.13.0 WP4); <see langword="null"/> for Windows' own.</summary>
    public static string? UiFamily { get; private set; }

    /// <summary>The bar's family, chosen in Settings and found on this PC (0.14.0 WP2); <see langword="null"/> for Windows' own.</summary>
    public static string? BarFamily { get; private set; }

    /// <summary>
    /// The size of the dialogs' and menus' text against Windows' (0.13.0 WP4, the dialogs' own since 0.14.0 WP2): 1 for
    /// 100 %. Dialogs made from now on are scaled by it.
    /// </summary>
    public static double TextFactor { get; private set; } = 1;

    /// <summary>
    /// Draws Chinese with the font in <paramref name="fontFile"/>, or, with <see langword="null"/> or a file without a
    /// font, as before 0.12.0; Windows' own font for the rest, at Windows' size.
    /// </summary>
    public static void Configure(string? fontFile, ResourceDictionary? resources = null) => Configure(fontFile, null, null, 1, resources);

    /// <summary>The bar and the dialogs alike, as 0.13.0 drew them: <paramref name="uiFamily"/> at <paramref name="textFactor"/>.</summary>
    public static void Configure(string? fontFile, string? uiFamily, double textFactor, ResourceDictionary? resources = null) =>
        Configure(fontFile, uiFamily, uiFamily, textFactor, resources);

    /// <summary>
    /// The app's text (0.13.0 WP4, two families since 0.14.0 WP2): a family first (Windows' own when none), Chinese in the
    /// font of <paramref name="fontFile"/> when there is one, then Windows' own (<see cref="UiFontChain"/>);
    /// <paramref name="barFamily"/> for the bar, <paramref name="dialogFamily"/> for the other windows, the menus, the
    /// tooltips and the tray menu; and the size of menus, tooltips and messages by <paramref name="dialogFactor"/>. Code and
    /// log text keep Consolas first. Without a family, a font file or a factor, nothing is left set: as before.
    /// </summary>
    /// <param name="barFamily">A family this PC has; the caller has checked (<see cref="IsInstalled"/>).</param>
    /// <param name="dialogFamily">The same for the dialogs; the bar's when they take the bar's.</param>
    public static void Configure(string? fontFile, string? barFamily, string? dialogFamily, double dialogFactor, ResourceDictionary? resources = null)
    {
        resources ??= Application.Current?.Resources;
        if (resources is null)
        {
            return;
        }

        var chinese = fontFile is null ? null : FromFile(fontFile);
        ChineseFontFile = chinese is null ? null : fontFile;
        ChineseFamilySource = chinese?.Source;
        BarFamily = string.IsNullOrWhiteSpace(barFamily) ? null : barFamily.Trim();
        UiFamily = string.IsNullOrWhiteSpace(dialogFamily) ? null : dialogFamily.Trim();
        TextFactor = double.IsFinite(dialogFactor) && dialogFactor > 0 ? dialogFactor : 1;
        resources[BarFamilyKey] = chinese is null && BarFamily is null ? SystemFonts.MessageFontFamily : Family(BarFamily, SystemFonts.MessageFontFamily, chinese);
        var plain = chinese is null && UiFamily is null;
        resources[FamilyKey] = plain ? SystemFonts.MessageFontFamily : Family(SystemFonts.MessageFontFamily, chinese);
        Set(resources, SystemFonts.MessageFontFamilyKey, plain ? null : Family(SystemFonts.MessageFontFamily, chinese));
        Set(resources, SystemFonts.MenuFontFamilyKey, plain ? null : Family(SystemFonts.MenuFontFamily, chinese));
        Set(resources, SystemFonts.StatusFontFamilyKey, plain ? null : Family(SystemFonts.StatusFontFamily, chinese));
        var scaled = Math.Abs(TextFactor - 1) > 0.001;
        SetSize(resources, SystemFonts.MessageFontSizeKey, scaled ? SystemFonts.MessageFontSize * TextFactor : null);
        SetSize(resources, SystemFonts.MenuFontSizeKey, scaled ? SystemFonts.MenuFontSize * TextFactor : null);
        SetSize(resources, SystemFonts.StatusFontSizeKey, scaled ? SystemFonts.StatusFontSize * TextFactor : null);
        resources[CodeFamilyKey] = Monospaced(chinese, "Courier New");
        resources[LogFamilyKey] = Monospaced(chinese, "Segoe UI");
    }

    /// <summary>The dialogs' chain for one kind of text, Windows' family for it being <paramref name="windows"/>.</summary>
    private static FontFamily Family(FontFamily windows, FontFamily? chinese) => Family(UiFamily, windows, chinese);

    private static FontFamily Family(string? chosen, FontFamily windows, FontFamily? chinese) => new(UiFontChain.Build(chosen, chinese?.Source, windows.Source));

    /// <summary>The chain the sample in Settings shows: <paramref name="uiFamily"/>, then <paramref name="chineseSource"/>, then Windows' own.</summary>
    public static FontFamily SampleFamily(string? uiFamily, string? chineseSource) =>
        new(UiFontChain.Build(uiFamily, chineseSource, SystemFonts.MessageFontFamily.Source));

    private static HashSet<string>? _installed;

    /// <summary>The families installed on this PC.</summary>
    public static IReadOnlyList<FontFamily> InstalledFamilies() => [.. Fonts.SystemFontFamilies];

    /// <summary>
    /// Whether this PC has the family <paramref name="name"/> (by its name in any language). The list is kept, and read
    /// again once when a name is not in it: a font installed since it was read is found.
    /// </summary>
    public static bool IsInstalled(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (_installed is { } known && known.Contains(name.Trim()))
        {
            return true;
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var family in Fonts.SystemFontFamilies)
        {
            names.Add(family.Source);
            foreach (var local in family.FamilyNames.Values)
            {
                names.Add(local);
            }
        }

        _installed = names;
        return names.Contains(name.Trim());
    }

    /// <summary>Consolas, the Chinese font for what Consolas lacks, then <paramref name="fallback"/> as before.</summary>
    public static FontFamily Monospaced(FontFamily? chinese, string fallback) =>
        new(chinese is null ? $"{Consolas.Source}, {fallback}" : $"{Consolas.Source}, {chinese.Source}, {fallback}");

    /// <summary>Gives <paramref name="window"/> (or any root element) the dialogs' font; call it once, when it is made.</summary>
    public static void Apply(FrameworkElement window)
    {
        ArgumentNullException.ThrowIfNull(window);
        window.SetResourceReference(TextElement.FontFamilyProperty, FamilyKey);
    }

    /// <summary>
    /// The bar's font as the bar draws with it now (<see cref="BarFamilyKey"/>), Windows' own without an application (tests):
    /// what a caption or a widget's text without a font of its own is drawn in (0.15.0 WP7b).
    /// </summary>
    public static FontFamily BarFontFamily() =>
        Application.Current?.TryFindResource(BarFamilyKey) as FontFamily ?? SystemFonts.MessageFontFamily;

    /// <summary>Gives the bar's window (or the bar at the pointer) the bar's font (0.14.0 WP2); call it once, when it is made.</summary>
    public static void ApplyBar(FrameworkElement window)
    {
        ArgumentNullException.ThrowIfNull(window);
        window.SetResourceReference(TextElement.FontFamilyProperty, BarFamilyKey);
    }

    /// <summary>
    /// The family in <paramref name="fontFile"/>, named as <see cref="Fonts.GetFontFamilies(string)"/> reads it from the
    /// file and referred to by that file; <see langword="null"/> when the file holds no font.
    /// </summary>
    public static FontFamily? FromFile(string fontFile)
    {
        try
        {
            var family = Fonts.GetFontFamilies(fontFile).FirstOrDefault();
            if (family is null)
            {
                return null;
            }

            var name = family.FamilyNames.TryGetValue(XmlLanguage.GetLanguage("en-us"), out var english) ? english : family.FamilyNames.Values.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            // The file itself, not its folder: a comma in the path would end the reference in a chain.
            var file = new Uri(Path.GetFullPath(fontFile)).AbsoluteUri.Replace(",", "%2C", StringComparison.Ordinal);
            return new FontFamily($"{file}#{name}");
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UriFormatException or UnauthorizedAccessException or NotSupportedException)
        {
            AppLog.Default.Info("Fonts", $"No font could be read from {fontFile}: {ex.Message}");
            return null;
        }
    }

    /// <summary>The Latin font first, then the Chinese one for every character it does not have.</summary>
    public static FontFamily Chain(FontFamily latin, FontFamily chinese) => new($"{latin.Source}, {chinese.Source}");

    private static void SetSize(ResourceDictionary resources, object key, double? value)
    {
        if (value is { } size)
        {
            resources[key] = size;
        }
        else
        {
            resources.Remove(key);
        }
    }

    private static void Set(ResourceDictionary resources, object key, FontFamily? value)
    {
        if (value is null)
        {
            resources.Remove(key);
        }
        else
        {
            resources[key] = value;
        }
    }
}
