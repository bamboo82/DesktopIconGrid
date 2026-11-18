using System.Globalization;

namespace DesktopIconGrid.Core.Models;

/// <summary>Which font draws the app's Chinese text (0.12.0 WP5).</summary>
public static class ChineseFonts
{
    /// <summary>
    /// Whether Chinese text is drawn in Taipei Sans TC Light: only when this PC has the font
    /// (<paramref name="fontFound"/>), and then as <paramref name="setting"/> says, or by default for every UI language
    /// but Simplified Chinese (the font draws the glyph forms of Taiwan).
    /// </summary>
    public static bool UsesTaipei(string? setting, CultureInfo? uiCulture, bool fontFound) => fontFound && setting switch
    {
        LauncherSettings.ChineseFontSystem => false,
        LauncherSettings.ChineseFontTaipei => true,
        _ => !AppLanguage.IsSimplifiedChinese(uiCulture),
    };
}
