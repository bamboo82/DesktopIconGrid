using DesktopIconGrid.Core.Models;

namespace DesktopIconGrid.Core.Tests;

/// <summary>0.12.0 WP3 (G4): the symbols a text icon can be, and how the picker's search finds them.</summary>
public class SymbolCatalogTests
{
    [Fact]
    public void The_catalogue_is_the_review_sessions_table_of_197_symbols_in_its_order()
    {
        var all = SymbolCatalog.All;

        Assert.Equal(197, all.Count);
        Assert.Equal((0xE80F, "Home", SymbolGroup.Common), (all[0].CodePoint, all[0].Name, all[0].Group));
        Assert.Equal((0xE8E0, "Dislike", SymbolGroup.ArrowsAndMarks), (all[^1].CodePoint, all[^1].Name, all[^1].Group));
        Assert.Equal((0xE720, SymbolGroup.Media), Of("Microphone"));
        Assert.Equal((0xE724, SymbolGroup.Communication), Of("Send"));
        Assert.Equal([26, 21, 14, 24, 14, 14, 30, 25, 29], Enum.GetValues<SymbolGroup>().Select(group => all.Count(symbol => symbol.Group == group)));

        static (int, SymbolGroup) Of(string name)
        {
            var symbol = SymbolCatalog.All.Single(entry => entry.Name == name);
            return (symbol.CodePoint, symbol.Group);
        }
    }

    [Fact]
    public void No_code_point_or_name_is_there_twice_and_every_symbol_is_in_the_private_use_range()
    {
        var all = SymbolCatalog.All;

        Assert.Equal(all.Count, all.Select(symbol => symbol.CodePoint).Distinct().Count());
        Assert.Equal(all.Count, all.Select(symbol => symbol.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(all, symbol => Assert.InRange(symbol.CodePoint, 0xE700, 0xF8FF));
        Assert.All(all, symbol => Assert.False(string.IsNullOrWhiteSpace(symbol.Name)));
        Assert.All(all, symbol => Assert.Equal(1, symbol.Text.Length));
    }

    [Fact]
    public void Every_group_has_symbols_and_they_are_listed_group_by_group_in_order()
    {
        var groups = SymbolCatalog.All.Select(symbol => symbol.Group).ToList();

        Assert.Equal(Enum.GetValues<SymbolGroup>(), groups.Distinct());
        Assert.Equal(groups.Order(), groups); // one run per group, so each heading appears once
    }

    [Fact]
    public void A_symbol_icon_fits_its_tile_in_the_symbol_font_and_is_found_again_by_its_character()
    {
        var mail = Assert.Single(SymbolCatalog.All, symbol => symbol.Name == "Mail");
        var icon = SymbolCatalog.Icon(mail, color: null, background: "#2D4A6E");

        Assert.Equal(new TextIcon("\uE715", "Segoe Fluent Icons, Segoe MDL2 Assets", 26, false, null, Fit: true, Background: "#2D4A6E"), icon);
        Assert.Equal(icon, icon.Normalized());
        Assert.True(SymbolCatalog.IsSymbol(icon));
        Assert.Equal(mail, SymbolCatalog.Find(icon.Text));

        Assert.False(SymbolCatalog.IsSymbol(icon with { Fit = false })); // as an older version would have it drawn
        Assert.False(SymbolCatalog.IsSymbol(new TextIcon("記", Fit: true)));
        Assert.False(SymbolCatalog.IsSymbol(null));
        Assert.Null(SymbolCatalog.Find("記"));
        Assert.Null(SymbolCatalog.Find("\uE715\uE715"));
        Assert.Null(SymbolCatalog.Find(null));
    }

    [Theory]
    [InlineData("mail", "Mail")]
    [InlineData("MAIL", "Mail open")]
    [InlineData("envel", "Mail")] // a keyword
    [InlineData("chev d", "Chevron down")] // every word begins a word
    [InlineData("  trash  ", "Delete")]
    [InlineData("wi-fi", "Wi-Fi")]
    [InlineData("3d", "Cube")]
    public void The_search_finds_a_symbol_by_the_beginning_of_any_word_of_its_name_or_keywords(string query, string name)
    {
        var found = SymbolCatalog.All.Where(symbol => SymbolCatalog.Matches(symbol, query)).Select(symbol => symbol.Name).ToList();

        Assert.Contains(name, found);
        if (query.Contains("chev", StringComparison.Ordinal))
        {
            Assert.Equal([name], found);
        }
    }

    [Fact]
    public void An_empty_search_finds_every_symbol_and_one_that_begins_no_word_finds_none()
    {
        Assert.All(SymbolCatalog.All, symbol => Assert.True(SymbolCatalog.Matches(symbol, "")));
        Assert.All(SymbolCatalog.All, symbol => Assert.True(SymbolCatalog.Matches(symbol, null)));
        Assert.DoesNotContain(SymbolCatalog.All, symbol => SymbolCatalog.Matches(symbol, "zzz"));
        Assert.False(SymbolCatalog.Matches(Assert.Single(SymbolCatalog.All, symbol => symbol.Name == "Mail"), "ail")); // not inside a word
    }
}
