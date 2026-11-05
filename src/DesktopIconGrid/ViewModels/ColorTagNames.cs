using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Resources;

namespace DesktopIconGrid.ViewModels;

/// <summary>The colour tags' names in the UI language.</summary>
public static class ColorTagNames
{
    /// <summary>The name of a known tag; empty for none or for a tag this version does not know.</summary>
    public static string Of(string? tag) => ColorTags.Find(tag)?.Name switch
    {
        ColorTags.Red => Strings.Tag_Red,
        ColorTags.Orange => Strings.Tag_Orange,
        ColorTags.Yellow => Strings.Tag_Yellow,
        ColorTags.Green => Strings.Tag_Green,
        ColorTags.Blue => Strings.Tag_Blue,
        ColorTags.Purple => Strings.Tag_Purple,
        ColorTags.Gray => Strings.Tag_Gray,
        _ => string.Empty,
    };
}
