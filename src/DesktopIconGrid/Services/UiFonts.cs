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
/// <item>Every window takes its font from the resource <see cref="FamilyKey"/> (<see cref="Apply"/>), so the bar
/// follows a change at once and a dialog when it is next opened.</item>
/// <item>Menus, context menus, tooltips and the tray menu are windows of their own: their styles, classic and Fluent,
/// read the system's menu, status and message font keys, which are set beside it in the app's resources.</item>
/// <item>The font is referred to by its file, never by a family name: a PC that also has the Regular or Bold of the
/// family installed would otherwise answer a normal-weight request with Regular.</item>
/// <item>Consolas text (the logs, the details of an approval) takes the font after Consolas, from
/// <see cref="CodeFamilyKey"/> and <see cref="LogFamilyKey"/> (0.12.1, review 28 F2: the plan said "the chain after it
/// for Chinese", and their Chinese was Windows' font).</item>
/// </list>
/// Left alone: text icons, gauges' numbers and symbol icons, which set fonts of their own, and Windows' own message
/// boxes, which the app does not draw.
/// </summary>
public static class UiFonts
{
    /// <summary>The resource every window's font refers to.</summary>
    public const string FamilyKey = "App.UiFontFamily";

    /// <summary>Consolas, then Courier New: commands and their details.</summary>
    public const string CodeFamilyKey = "App.CodeFontFamily";

    /// <summary>Consolas, then Segoe UI: the log's lines.</summary>
    public const string LogFamilyKey = "App.LogFontFamily";

    private static readonly FontFamily Consolas = new("Consolas");

    /// <summary>The Taipei Sans file in use, or <see langword="null"/> when Chinese is drawn as Windows draws it.</summary>
    public static string? ChineseFontFile { get; private set; }

    /// <summary>The Chinese font as WPF refers to it (its file), for the sample in Settings; <see langword="null"/> without one.</summary>
    public static string? ChineseFamilySource { get; private set; }

    /// <summary>The family chosen in Settings and found on this PC (0.13.0 WP4); <see langword="null"/> for Windows' own.</summary>
    public static string? UiFamily { get; private set; }

    /// <summary>The size of the app's text against Windows' (0.13.0 WP4): 1 for 100 %. Dialogs made from now on are scaled by it.</summary>
    public static double TextFactor { get; private set; } = 1;

    /// <summary>
    /// Draws Chinese with the font in <paramref name="fontFile"/>, or, with <see langword="null"/> or a file without a
    /// font, as before 0.12.0; Windows' own font for the rest, at Windows' size.
    /// </summary>
    public static void Configure(string? fontFile, ResourceDictionary? resources = null) => Configure(fontFile, null, 1, resources);

    /// <summary>
    /// The app's text (0.13.0 WP4): <paramref name="uiFamily"/> first (Windows' own when none), Chinese in the font of
    /// <paramref name="fontFile"/> when there is one, then Windows' own (<see cref="UiFontChain"/>), for the windows, the
    /// menus, the tooltips and the tray menu; and the size of menus, tooltips and messages by <paramref name="textFactor"/>.
    /// Code and log text keep Consolas first. Without a family, a font file or a factor, nothing is left set: as before.
    /// </summary>
    /// <param name="uiFamily">A family this PC has; the caller has checked (<see cref="IsInstalled"/>).</param>
    public static void Configure(string? fontFile, string? uiFamily, double textFactor, ResourceDictionary? resources = null)
    {
        resources ??= Application.Current?.Resources;
        if (resources is null)
        {
            return;
        }

        var chinese = fontFile is null ? null : FromFile(fontFile);
        ChineseFontFile = chinese is null ? null : fontFile;
        ChineseFamilySource = chinese?.Source;
        UiFamily = string.IsNullOrWhiteSpace(uiFamily) ? null : uiFamily.Trim();
        TextFactor = double.IsFinite(textFactor) && textFactor > 0 ? textFactor : 1;
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

    /// <summary>The chain for one kind of text, Windows' family for it being <paramref name="windows"/>.</summary>
    private static FontFamily Family(FontFamily windows, FontFamily? chinese) => new(UiFontChain.Build(UiFamily, chinese?.Source, windows.Source));

    /// <summary>The chain the sample in Settings shows: <paramref name="uiFamily"/>, then <paramref name="chineseSource"/>, then Windows' own.</summary>
    public static FontFamily SampleFamily(string? uiFamily, string? chineseSource) =>
        new(UiFontChain.Build(uiFamily, chineseSource, SystemFonts.MessageFontFamily.Source));

    private static HashSet<string>? _installed;

    /// <summary>The families installed on this PC.</summary>
    public static IReadOnlyList<FontFamily> InstalledFamilies() => [.. Fonts.SystemFontFamilies];

    /// <summary>Whether this PC has the family <paramref name="name"/> (by its name in any language).</summary>
    public static bool IsInstalled(string name)
    {
        if (_installed is null)
        {
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
        }

        return _installed.Contains(name.Trim());
    }

    /// <summary>Consolas, the Chinese font for what Consolas lacks, then <paramref name="fallback"/> as before.</summary>
    public static FontFamily Monospaced(FontFamily? chinese, string fallback) =>
        new(chinese is null ? $"{Consolas.Source}, {fallback}" : $"{Consolas.Source}, {chinese.Source}, {fallback}");

    /// <summary>Gives <paramref name="window"/> (or any root element) the app's font; call it once, when it is made.</summary>
    public static void Apply(FrameworkElement window)
    {
        ArgumentNullException.ThrowIfNull(window);
        window.SetResourceReference(TextElement.FontFamilyProperty, FamilyKey);
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
