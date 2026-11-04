using System.Globalization;
using System.Windows.Markup;
using System.Windows.Media;
using DesktopIconGrid.Core.Models;

namespace DesktopIconGrid.Services;

/// <summary>An installed font family with the name to show in menus.</summary>
public sealed record FontChoice(string Source, string DisplayName, FontFamily Family);

/// <summary>Font lookup for text icons: installed families, the preferred default (宋體) and fallback chains.</summary>
public static class TextIconFonts
{
    private static readonly string[] PreferredDefaults = ["SimSun", "宋体", "宋體", "PMingLiU", "新細明體", "Microsoft JhengHei", "Microsoft YaHei"];

    private static IReadOnlyList<FontChoice>? _installed;

    /// <summary>Installed families sorted by display name. Enumerated once.</summary>
    public static IReadOnlyList<FontChoice> Installed => _installed ??= LoadInstalled();

    /// <summary>The family a new text icon starts with: 宋體 when present, otherwise the Windows UI font.</summary>
    public static FontChoice DefaultChoice()
    {
        foreach (var preferred in PreferredDefaults)
        {
            var match = Installed.FirstOrDefault(f =>
                string.Equals(f.Source, preferred, StringComparison.OrdinalIgnoreCase)
                || string.Equals(f.DisplayName, preferred, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return match;
            }
        }

        return Installed.FirstOrDefault(f => f.Source.Equals("Segoe UI", StringComparison.OrdinalIgnoreCase))
               ?? Installed.FirstOrDefault()
               ?? new FontChoice("Segoe UI", "Segoe UI", new FontFamily("Segoe UI"));
    }

    /// <summary>A family for rendering: the chosen name first, then the default chain so missing fonts degrade gracefully.</summary>
    public static FontFamily Resolve(string? familyName)
    {
        var chain = string.IsNullOrWhiteSpace(familyName)
            ? TextIcon.DefaultFontFamily
            : $"{familyName.Trim()}, {TextIcon.DefaultFontFamily}";
        try
        {
            return new FontFamily(chain);
        }
        catch (ArgumentException)
        {
            return new FontFamily(TextIcon.DefaultFontFamily);
        }
    }

    public static FontChoice? Find(string? source) =>
        string.IsNullOrWhiteSpace(source)
            ? null
            : Installed.FirstOrDefault(f => string.Equals(f.Source, source.Trim(), StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<FontChoice> LoadInstalled()
    {
        var language = XmlLanguage.GetLanguage(CultureInfo.CurrentUICulture.IetfLanguageTag);
        var english = XmlLanguage.GetLanguage("en-us");
        var choices = new List<FontChoice>();
        foreach (var family in Fonts.SystemFontFamilies)
        {
            var source = family.Source;
            if (string.IsNullOrWhiteSpace(source))
            {
                continue;
            }

            var display = family.FamilyNames.TryGetValue(language, out var localized) ? localized
                : family.FamilyNames.TryGetValue(english, out var englishName) ? englishName
                : source;
            choices.Add(new FontChoice(source, display, family));
        }

        return choices.OrderBy(c => c.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
    }
}
