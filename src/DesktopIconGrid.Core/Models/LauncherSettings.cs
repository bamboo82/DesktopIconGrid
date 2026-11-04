using System.Text.RegularExpressions;

namespace DesktopIconGrid.Core.Models;

/// <summary>User preferences that affect how the bar looks and reacts.</summary>
public sealed partial class LauncherSettings
{
    public static readonly IReadOnlyList<int> IconSizes = [16, 24, 32, 48, 64];

    public const int DefaultIconSize = 32;
    public const int MinCellPadding = 2;
    public const int MaxCellPadding = 32;
    public const int DefaultCellPadding = 10;
    public const string DefaultBackgroundColor = "#73806E";

    /// <summary>Icon edge length in device-independent pixels; one of <see cref="IconSizes"/>.</summary>
    public int IconSize { get; set; } = DefaultIconSize;

    /// <summary>Space around each icon; a cell is <c>IconSize + 2 × CellPadding</c>.</summary>
    public int CellPadding { get; set; } = DefaultCellPadding;

    public bool ShowLabels { get; set; }

    public bool DoubleClickToLaunch { get; set; }

    public bool AlwaysOnTop { get; set; }

    /// <summary>Bar background as <c>#RRGGBB</c>.</summary>
    public string BackgroundColor { get; set; } = DefaultBackgroundColor;

    public LauncherSettings Clone() => (LauncherSettings)MemberwiseClone();

    public void CopyFrom(LauncherSettings source)
    {
        ArgumentNullException.ThrowIfNull(source);
        IconSize = source.IconSize;
        CellPadding = source.CellPadding;
        ShowLabels = source.ShowLabels;
        DoubleClickToLaunch = source.DoubleClickToLaunch;
        AlwaysOnTop = source.AlwaysOnTop;
        BackgroundColor = source.BackgroundColor;
        Normalize();
    }

    /// <summary>Forces every value into its supported range. Returns <see langword="true"/> when anything changed.</summary>
    public bool Normalize()
    {
        var changed = false;

        var nearest = NearestIconSize(IconSize);
        if (nearest != IconSize)
        {
            IconSize = nearest;
            changed = true;
        }

        var padding = Math.Clamp(CellPadding, MinCellPadding, MaxCellPadding);
        if (padding != CellPadding)
        {
            CellPadding = padding;
            changed = true;
        }

        if (!IsValidColor(BackgroundColor))
        {
            BackgroundColor = DefaultBackgroundColor;
            changed = true;
        }
        else if (BackgroundColor != BackgroundColor.ToUpperInvariant())
        {
            BackgroundColor = BackgroundColor.ToUpperInvariant();
        }

        return changed;
    }

    public static int NearestIconSize(int requested) =>
        IconSizes.MinBy(size => (Math.Abs(size - requested), size));

    public static bool IsValidColor(string? value) =>
        value is not null && ColorPattern().IsMatch(value);

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex ColorPattern();
}
