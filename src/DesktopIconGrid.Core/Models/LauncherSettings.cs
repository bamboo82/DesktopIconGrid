using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using DesktopIconGrid.Core.Storage;

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

    /// <summary>The bar is painted in the theme colour.</summary>
    public const string BackdropSolid = "solid";

    /// <summary>Windows 11 Mica: the wallpaper shows through, blurred and tinted with the theme colour.</summary>
    public const string BackdropMica = "mica";

    /// <summary>Windows 11 Acrylic: whatever is behind the bar shows through, blurred and tinted.</summary>
    public const string BackdropAcrylic = "acrylic";

    public static readonly IReadOnlyList<string> Backdrops = [BackdropSolid, BackdropMica, BackdropAcrylic];

    public const string DockNone = "none";
    public const string DockTop = "top";
    public const string DockBottom = "bottom";
    public const string DockLeft = "left";
    public const string DockRight = "right";

    public static readonly IReadOnlyList<string> DockEdges = [DockNone, DockTop, DockBottom, DockLeft, DockRight];

    /// <summary>The UI follows the Windows display language.</summary>
    public const string LanguageSystem = "system";
    public const string LanguageEnglish = "en";
    public const string LanguageTraditionalChinese = "zh-TW";
    public const string LanguageSimplifiedChinese = "zh-CN";

    public static readonly IReadOnlyList<string> Languages = [LanguageSystem, LanguageEnglish, LanguageTraditionalChinese, LanguageSimplifiedChinese];

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

    /// <summary>What the bar is painted on: the theme colour, or a Windows 11 backdrop tinted with it (one of <see cref="Backdrops"/>).</summary>
    public string BarBackdrop { get; set; } = BackdropSolid;

    /// <summary>Dialogs use the Windows 11 (Fluent) look and follow the system light/dark mode.</summary>
    public bool FluentDialogs { get; set; } = true;

    /// <summary>The close button hides the bar to the notification area instead of exiting.</summary>
    public bool CloseToTray { get; set; } = true;

    /// <summary>Screen edge the bar is docked to as a desktop toolbar (maximised windows stay clear of it), one of <see cref="DockEdges"/>.</summary>
    public string DockEdge { get; set; } = DockNone;

    /// <summary>With a dock edge: collapse to a thin strip when the pointer leaves, come back when it touches the edge.</summary>
    public bool AutoHide { get; set; }

    /// <summary>Register the app in the current user's Run key so it starts at sign-in.</summary>
    public bool StartWithWindows { get; set; }

    /// <summary>When started with Windows, keep the bar hidden until it is shown from the tray or the hotkey.</summary>
    public bool StartHidden { get; set; }

    /// <summary>Bar opacity in percent, <see cref="MinOpacity"/>–100 (solid background only).</summary>
    public int Opacity { get; set; } = MaxOpacity;

    /// <summary>System-wide key combination that shows or hides the bar, e.g. <c>Ctrl+Alt+Space</c>; <see langword="null"/> for none.</summary>
    public string? ToggleHotkey { get; set; }

    /// <summary>
    /// System-wide key combination that opens the bar's icons in a small window under the mouse pointer, with the
    /// search box ready; <see langword="null"/> for none (the default).
    /// </summary>
    public string? ShowAtPointerHotkey { get; set; }

    /// <summary>
    /// The bar window's keys that differ from their defaults (<see cref="Models.WindowKeys"/>), by action name; an empty
    /// text turns a key off. <see langword="null"/> when every key is its default.
    /// </summary>
    public Dictionary<string, string>? WindowKeys { get; set; }

    /// <summary>UI language: <see cref="LanguageSystem"/> or a culture name from <see cref="Languages"/>. Read once at startup.</summary>
    public string Language { get; set; } = LanguageSystem;

    /// <summary>Properties written by a newer version that this one does not know; kept so saving never drops them.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    public LauncherSettings Clone()
    {
        var copy = (LauncherSettings)MemberwiseClone();
        copy.ExtensionData = UnknownProperties.Copy(ExtensionData);
        copy.WindowKeys = WindowKeys is null ? null : new Dictionary<string, string>(WindowKeys);
        return copy;
    }

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
        BarBackdrop = source.BarBackdrop;
        FluentDialogs = source.FluentDialogs;
        ToggleHotkey = source.ToggleHotkey;
        ShowAtPointerHotkey = source.ShowAtPointerHotkey;
        WindowKeys = source.WindowKeys is null ? null : new Dictionary<string, string>(source.WindowKeys);
        Opacity = source.Opacity;
        CloseToTray = source.CloseToTray;
        StartWithWindows = source.StartWithWindows;
        StartHidden = source.StartHidden;
        DockEdge = source.DockEdge;
        AutoHide = source.AutoHide;
        Language = source.Language;
        ExtensionData = UnknownProperties.Copy(source.ExtensionData);
        Normalize();
    }

    /// <summary>Forces every value into its supported range. Returns <see langword="true"/> when anything changed.</summary>
    public bool Normalize()
    {
        var changed = false;

        if (WindowKeys is { } windowKeys && windowKeys.Any(pair => pair.Key is null || pair.Value is null))
        {
            // Null values (or names) come only from a damaged or hand-edited file; they are no overrides.
            WindowKeys = windowKeys.Where(pair => pair.Key is not null && pair.Value is not null).ToDictionary(pair => pair.Key, pair => pair.Value);
            changed = true;
        }

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

        var backdrop = BarBackdrop?.Trim().ToLowerInvariant() ?? string.Empty;
        if (Backdrops.Contains(backdrop))
        {
            BarBackdrop = backdrop;
        }
        else
        {
            BarBackdrop = BackdropSolid;
            changed = true;
        }

        var edge = DockEdge?.Trim().ToLowerInvariant() ?? string.Empty;
        if (DockEdges.Contains(edge))
        {
            DockEdge = edge;
        }
        else
        {
            DockEdge = DockNone;
            changed = true;
        }

        ToggleHotkey = string.IsNullOrWhiteSpace(ToggleHotkey) ? null : ToggleHotkey.Trim();
        ShowAtPointerHotkey = string.IsNullOrWhiteSpace(ShowAtPointerHotkey) ? null : ShowAtPointerHotkey.Trim();

        var language = Languages.FirstOrDefault(l => string.Equals(l, Language?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (language is null)
        {
            Language = LanguageSystem;
            changed = true;
        }
        else
        {
            Language = language;
        }

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
