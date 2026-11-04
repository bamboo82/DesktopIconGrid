using System.Globalization;
using System.Windows;
using System.Windows.Markup;
using DesktopIconGrid.Core.Diagnostics;
using DesktopIconGrid.Core.Models;

namespace DesktopIconGrid.Services;

/// <summary>
/// Puts the UI into the language from the settings. Runs once at startup, before any window exists: the strings
/// are read when each window loads, so a change in Settings takes effect at the next start.
/// </summary>
public static class UiLanguage
{
    private static bool _applied;

    public static void Apply(string? language)
    {
        var culture = AppLanguage.CultureFor(language);
        if (culture is not null)
        {
            CultureInfo.DefaultThreadCurrentUICulture = culture;
            CultureInfo.CurrentUICulture = culture;
        }

        // WPF text follows the UI language: font fallback and the Traditional/Simplified forms of shared ideographs.
        var tag = CultureInfo.CurrentUICulture.IetfLanguageTag;
        if (!_applied && tag.Length > 0)
        {
            _applied = true;
            try
            {
                FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement), new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(tag)));
            }
            catch (InvalidOperationException ex)
            {
                AppLog.Default.Warning("App", $"UI language metadata not applied: {ex.Message}");
            }
        }

        AppLog.Default.Info("App", $"UI language: {CultureInfo.CurrentUICulture.Name} ({(culture is null ? "Windows display language" : "from settings")})");
    }
}
