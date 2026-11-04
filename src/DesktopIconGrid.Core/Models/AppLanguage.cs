using System.Globalization;

namespace DesktopIconGrid.Core.Models;

/// <summary>Turns the <see cref="LauncherSettings.Language"/> setting into a UI culture, and tells the Chinese scripts apart.</summary>
public static class AppLanguage
{
    private static readonly string[] TraditionalNames = ["zh-Hant", "zh-TW", "zh-HK", "zh-MO", "zh-CHT"];
    private static readonly string[] SimplifiedNames = ["zh-Hans", "zh-CN", "zh-SG", "zh-CHS"];

    /// <summary>
    /// The UI culture for a language setting; <see langword="null"/> for <see cref="LauncherSettings.LanguageSystem"/>
    /// (keep the Windows display language) and for a value Windows does not know.
    /// </summary>
    public static CultureInfo? CultureFor(string? language)
    {
        var name = language?.Trim();
        if (string.IsNullOrEmpty(name) || string.Equals(name, LauncherSettings.LanguageSystem, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            return CultureInfo.GetCultureInfo(name, predefinedOnly: true);
        }
        catch (CultureNotFoundException)
        {
            return null;
        }
    }

    /// <summary>True when the culture, or one of its parents, is Traditional Chinese (Taiwan, Hong Kong, Macao).</summary>
    public static bool IsTraditionalChinese(CultureInfo? culture) => Matches(culture, TraditionalNames);

    /// <summary>True for Simplified Chinese (mainland China, Singapore) and for the neutral <c>zh</c>.</summary>
    public static bool IsSimplifiedChinese(CultureInfo? culture) =>
        !IsTraditionalChinese(culture)
        && (Matches(culture, SimplifiedNames) || string.Equals(culture?.TwoLetterISOLanguageName, "zh", StringComparison.OrdinalIgnoreCase));

    private static bool Matches(CultureInfo? culture, string[] names)
    {
        for (var current = culture; current is not null && current.Name.Length > 0; current = current.Parent)
        {
            if (names.Contains(current.Name, StringComparer.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
