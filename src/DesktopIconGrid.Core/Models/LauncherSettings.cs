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
    public const int MinOpacity = 20;
    public const int MaxOpacity = 100;

    /// <summary>Icon edge length in device-independent pixels; one of <see cref="IconSizes"/>.</summary>
    public int IconSize { get; set; } = DefaultIconSize;

    /// <summary>Space around each icon; a cell is <c>IconSize + 2 × CellPadding</c>.</summary>
    public int CellPadding { get; set; } = DefaultCellPadding;

    public bool ShowLabels { get; set; }

    public bool DoubleClickToLaunch { get; set; }

    public bool AlwaysOnTop { get; set; }

    /// <summary>
    /// Id of a theme in <see cref="ThemeCatalog"/>, or <c>custom</c> to derive one from <see cref="BackgroundColor"/>.
    /// <see langword="null"/> only occurs for layouts saved before themes existed; <see cref="Normalize"/> migrates it.
    /// </summary>
    public string? Theme { get; set; } = ThemeCatalog.ClassicId;

    /// <summary>Bar background as <c>#RRGGBB</c>, used by the custom theme.</summary>
    public string BackgroundColor { get; set; } = DefaultBackgroundColor;

    /// <summary>Bar opacity in percent, <see cref="MinOpacity"/>–100.</summary>
    public int Opacity { get; set; } = MaxOpacity;

    /// <summary>System-wide key combination that shows or hides the bar, e.g. <c>Ctrl+Alt+Space</c>; <see langword="null"/> for none.</summary>
    public string? ToggleHotkey { get; set; }

    public LauncherSettings Clone() => (LauncherSettings)MemberwiseClone();

    public void CopyFrom(LauncherSettings source)
    {
        ArgumentNullException.ThrowIfNull(source);
        IconSize = source.IconSize;
        CellPadding = source.CellPadding;
        ShowLabels = source.ShowLabels;
        DoubleClickToLaunch = source.DoubleClickToLaunch;
        AlwaysOnTop = source.AlwaysOnTop;
        Theme = source.Theme;
        BackgroundColor = source.BackgroundColor;
        ToggleHotkey = source.ToggleHotkey;
        Opacity = source.Opacity;
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

        ToggleHotkey = string.IsNullOrWhiteSpace(ToggleHotkey) ? null : ToggleHotkey.Trim();

        var opacity = Math.Clamp(Opacity, MinOpacity, MaxOpacity);
        if (opacity != Opacity)
        {
            Opacity = opacity;
            changed = true;
        }

        if (Theme is null)
        {
            // Layout saved before themes existed: keep a changed colour as the custom theme.
            Theme = BackgroundColor == DefaultBackgroundColor ? ThemeCatalog.ClassicId : ThemeCatalog.CustomId;
        }
        else if (!ThemeCatalog.IsKnown(Theme))
        {
            Theme = ThemeCatalog.ClassicId;
            changed = true;
        }
        else
        {
            Theme = Theme.Trim().ToLowerInvariant();
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
