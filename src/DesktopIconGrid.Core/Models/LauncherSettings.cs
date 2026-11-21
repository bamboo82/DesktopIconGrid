using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using DesktopIconGrid.Core.Fonts;
using DesktopIconGrid.Core.Storage;

namespace DesktopIconGrid.Core.Models;

/// <summary>User preferences that affect how the bar looks and reacts.</summary>
public sealed partial class LauncherSettings
{
    /// <summary>
    /// The icon sizes Settings lists (0.15.0 WP8, the owner's request; before, 16, 24, 32, 48 and 64). Its box also takes any
    /// whole number from <see cref="MinIconSize"/> to <see cref="MaxIconSize"/>.
    /// </summary>
    public static readonly IReadOnlyList<int> IconSizes = [16, 20, 24, 28, 32, 40, 48, 56, 64, 72, 80, 96, 128];

    public const int MinIconSize = 16;
    public const int MaxIconSize = 256;
    public const int DefaultIconSize = 32;
    public const int MinCellPadding = 2;
    public const int MaxCellPadding = 32;
    public const int DefaultCellPadding = 10;
    public const string DefaultBackgroundColor = "#73806E";
    public const int MinOpacity = 20;
    public const int MinHoverStrength = 10;
    public const int MaxHoverStrength = 100;
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

    /// <summary>
    /// Icon edge length in device-independent pixels, any whole number from <see cref="MinIconSize"/> to
    /// <see cref="MaxIconSize"/> (0.15.0 WP8). A version before 0.15.0 snaps a size it does not list to its nearest.
    /// </summary>
    public int IconSize { get; set; } = DefaultIconSize;

    /// <summary>Space around each icon; a cell is <c>IconSize + 2 × CellPadding</c>.</summary>
    public int CellPadding { get; set; } = DefaultCellPadding;

    public bool ShowLabels { get; set; }

    public bool DoubleClickToLaunch { get; set; }

    public bool AlwaysOnTop { get; set; }

    /// <summary>
    /// Nothing on the grid can be moved by dragging (0.15.0 WP1, the owner's request): no icon, group, special icon or
    /// separator, in the bar, its groups or the window at the pointer. Files dropped from elsewhere are still taken, and
    /// the bar itself still moves. Off by default; it syncs, as <see cref="AlwaysOnTop"/> does.
    /// </summary>
    public bool LockGrid { get; set; }

    /// <summary>
    /// Id of a theme in <see cref="ThemeCatalog"/>, or <c>custom</c> to derive one from <see cref="BackgroundColor"/>.
    /// <see langword="null"/> only occurs for layouts saved before themes existed; <see cref="Normalize"/> migrates it.
    /// </summary>
    public string? Theme { get; set; } = ThemeCatalog.ClassicId;

    /// <summary>Bar background as <c>#RRGGBB</c>, used by the custom theme.</summary>
    public string BackgroundColor { get; set; } = DefaultBackgroundColor;

    /// <summary>
    /// The custom theme's colour under the pointer (0.15.0 WP4b, the owner's request): <c>#RRGGBB</c>, or none for the
    /// overlay derived from the background, as before. Anything else is dropped at load; it syncs with the theme.
    /// </summary>
    public string? HoverColor { get; set; }

    /// <summary>
    /// How strongly <see cref="HoverColor"/> lies over an item, <see cref="MinHoverStrength"/> to
    /// <see cref="MaxHoverStrength"/> percent; none for the derived overlay's own. Not used without a hover colour.
    /// </summary>
    public int? HoverStrength { get; set; }

    /// <summary>
    /// The custom theme's keyboard focus border colour (0.15.0 WP4b): <c>#RRGGBB</c>, or none for the accent derived
    /// from the background, as before. Anything else is dropped at load; it syncs with the theme.
    /// </summary>
    public string? FocusColor { get; set; }

    /// <summary>What the bar is painted on: the theme colour, or a Windows 11 backdrop tinted with it (one of <see cref="Backdrops"/>).</summary>
    public string BarBackdrop { get; set; } = BackdropSolid;

    /// <summary>Dialogs use the Windows 11 (Fluent) look and follow the system light/dark mode.</summary>
    public bool FluentDialogs { get; set; } = true;

    /// <summary>Chinese text in Taipei Sans TC Light when this PC has it (0.12.0 WP5).</summary>
    public const string ChineseFontTaipei = "taipei-sans-light";

    /// <summary>Chinese text in the font Windows gives it, as before 0.12.0.</summary>
    public const string ChineseFontSystem = "system";

    /// <summary>
    /// The font of Chinese text: <see cref="ChineseFontTaipei"/>, <see cref="ChineseFontSystem"/>, or
    /// <see langword="null"/> for the default (<see cref="ChineseFonts.UsesTaipei"/>). It syncs with the other
    /// appearance settings; a PC without the font draws Chinese as before whatever it says.
    /// </summary>
    public string? ChineseFont { get; set; }

    /// <summary>
    /// The font of the app's own text, by family name (0.13.0 WP4); <see langword="null"/> for Windows' own. It syncs, as
    /// <see cref="ChineseFont"/> does; a PC that does not have the family draws the text in Windows' own. Since 0.14.0 it is
    /// the bar's, and the dialogs' while <see cref="DialogFont"/> is not set.
    /// </summary>
    public string? UiFont { get; set; }

    /// <summary>The sizes Settings offers for the text, in percent.</summary>
    public static readonly IReadOnlyList<int> TextScales = [90, 100, 110, 125, 150];

    public const int MinTextScale = 90;

    public const int MaxTextScale = 150;

    /// <summary>
    /// The size of the app's text in percent of Windows' (0.13.0 WP4): dialogs are scaled whole, the bar's labels and their
    /// cells follow, icons keep their size. <see langword="null"/> is 100; a value outside 90–150 is taken to its end. Since
    /// 0.14.0 it is the bar's, and the dialogs' while <see cref="DialogTextScale"/> is not set.
    /// </summary>
    public int? TextScale { get; set; }

    /// <summary><see cref="TextScale"/> as a factor: 1 for 100 %.</summary>
    [JsonIgnore]
    public double TextFactor => (TextScale ?? 100) / 100.0;

    /// <summary>
    /// The font of the dialogs and menus (0.14.0 WP2), when it is not the bar's: <see langword="null"/> takes
    /// <see cref="UiFont"/>, as 0.13.0 did for both; an empty string is Windows' own while the bar has a font of its own; else
    /// a family's name, like <see cref="UiFont"/>. <see cref="UiFont"/> and <see cref="TextScale"/> are the bar's since then.
    /// </summary>
    public string? DialogFont { get; set; }

    /// <summary>
    /// The size of the dialogs' and menus' text in percent (0.14.0 WP2): <see langword="null"/> takes <see cref="TextScale"/>;
    /// 100 is kept, since it says 100 % whatever the bar's size is. A value outside 90–150 is taken to its end.
    /// </summary>
    public int? DialogTextScale { get; set; }

    /// <summary>The family the dialogs and menus ask for: <see langword="null"/> for Windows' own.</summary>
    [JsonIgnore]
    public string? DialogFamily => DialogFont is null ? UiFont : DialogFont.Length == 0 ? null : DialogFont;

    /// <summary>The dialogs' and menus' size as a factor: 1 for 100 %.</summary>
    [JsonIgnore]
    public double DialogTextFactor => (DialogTextScale ?? TextScale ?? 100) / 100.0;

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

    /// <summary>
    /// The opacity of the bar's background in percent, <see cref="MinOpacity"/>–100: a tint over Mica or Acrylic, or, with
    /// a solid background, how much of it hides the desktop. Icons, names and the rest of the bar stay solid (0.14.0;
    /// before, without a backdrop, the whole bar faded). Stored and synced as before.
    /// </summary>
    public int Opacity { get; set; } = MaxOpacity;

    /// <summary>System-wide key combination that shows or hides the bar, e.g. <c>Ctrl+Alt+Space</c>; <see langword="null"/> for none.</summary>
    public string? ToggleHotkey { get; set; }

    /// <summary>
    /// System-wide key combination that opens the bar's icons in a small window under the mouse pointer, with the
    /// search box ready; <see langword="null"/> for none (the default).
    /// </summary>
    public string? ShowAtPointerHotkey { get; set; }

    /// <summary>
    /// The system-wide combination that shows the bar at the mouse pointer while it is held and hides it when the first
    /// of its keys comes up (0.15.0 WP2, the owner's request); <see langword="null"/> for none (the default). It syncs, as
    /// <see cref="ShowAtPointerHotkey"/> does.
    /// </summary>
    public string? HoldAtPointerHotkey { get; set; }

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
        LockGrid = source.LockGrid;
        Theme = source.Theme;
        BackgroundColor = source.BackgroundColor;
        HoverColor = source.HoverColor;
        HoverStrength = source.HoverStrength;
        FocusColor = source.FocusColor;
        BarBackdrop = source.BarBackdrop;
        FluentDialogs = source.FluentDialogs;
        ChineseFont = source.ChineseFont;
        UiFont = source.UiFont;
        TextScale = source.TextScale;
        DialogFont = source.DialogFont;
        DialogTextScale = source.DialogTextScale;
        ToggleHotkey = source.ToggleHotkey;
        ShowAtPointerHotkey = source.ShowAtPointerHotkey;
        HoldAtPointerHotkey = source.HoldAtPointerHotkey;
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

        var iconSize = ClampIconSize(IconSize);
        if (iconSize != IconSize)
        {
            IconSize = iconSize;
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

        // The custom theme's own hover and focus colours (0.15.0 WP4b): #RRGGBB or none; the strength 10–100 or none.
        var hover = OwnColour(HoverColor);
        var focus = OwnColour(FocusColor);
        var strength = HoverStrength is { } percent ? Math.Clamp(percent, MinHoverStrength, MaxHoverStrength) : (int?)null;
        if (hover != HoverColor || focus != FocusColor || strength != HoverStrength)
        {
            changed |= !string.Equals(hover, HoverColor, StringComparison.OrdinalIgnoreCase) || !string.Equals(focus, FocusColor, StringComparison.OrdinalIgnoreCase) || strength != HoverStrength;
            (HoverColor, FocusColor, HoverStrength) = (hover, focus, strength);
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

        var chinese = ChineseFont?.Trim().ToLowerInvariant();
        if (chinese is not null && chinese is not (ChineseFontTaipei or ChineseFontSystem))
        {
            ChineseFont = null; // no such choice: the default
            changed = true;
        }
        else if (chinese != ChineseFont)
        {
            ChineseFont = chinese;
        }

        if (UiFont is not null)
        {
            var family = UiFontChain.IsFamilyName(UiFont) ? UiFont.Trim() : null; // no family's name: Windows' own
            changed |= family != UiFont;
            UiFont = family;
        }

        if (TextScale is { } scale)
        {
            var clamped = Math.Clamp(scale, MinTextScale, MaxTextScale);
            changed |= clamped != scale; // out of range: repaired; 100 is only the default written out
            TextScale = clamped == 100 ? null : clamped;
        }

        if (DialogFont is not null)
        {
            // An empty string says Windows' own, and so does what is no family's name, as for the bar's font.
            var family = UiFontChain.IsFamilyName(DialogFont) ? DialogFont.Trim() : string.Empty;
            changed |= family != DialogFont;
            DialogFont = family;
        }

        if (DialogTextScale is { } dialogScale)
        {
            var clamped = Math.Clamp(dialogScale, MinTextScale, MaxTextScale);
            changed |= clamped != dialogScale;
            DialogTextScale = clamped; // 100 too: it is not the bar's size
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
        HoldAtPointerHotkey = string.IsNullOrWhiteSpace(HoldAtPointerHotkey) ? null : HoldAtPointerHotkey.Trim();

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

    /// <summary>An icon size held to 16–256: since 0.15.0 (WP8) any whole number in that range is kept, not snapped to the list.</summary>
    public static int ClampIconSize(int requested) => Math.Clamp(requested, MinIconSize, MaxIconSize);

    /// <summary>
    /// What the icon size box makes of <paramref name="text"/> (0.15.0 WP8): a whole number, with or without "px" after it,
    /// held to 16–256 (so 300 is 256 and 0 or −5 is 16); anything else gives <paramref name="previous"/> back. Full-width
    /// digits, as a Chinese input method types them, count.
    /// </summary>
    public static int ParseIconSize(string? text, int previous)
    {
        var value = (text ?? string.Empty).Trim();
        if (value.EndsWith("px", StringComparison.OrdinalIgnoreCase))
        {
            value = value[..^2].TrimEnd();
        }

        var negative = value.StartsWith('-') || value.StartsWith('－') || value.StartsWith('−');
        if (negative || value.StartsWith('+') || value.StartsWith('＋'))
        {
            value = value[1..].TrimStart();
        }

        if (value.Length == 0 || !value.All(char.IsDigit))
        {
            return previous;
        }

        long number = 0;
        foreach (var digit in value)
        {
            number = Math.Min(number * 10 + (long)char.GetNumericValue(digit), MaxIconSize + 1);
        }

        return negative ? MinIconSize : ClampIconSize((int)number);
    }

    /// <summary>A colour of the theme's own, <c>#RRGGBB</c> in capitals, or none for anything else.</summary>
    private static string? OwnColour(string? value) => IsValidColor(value?.Trim()) ? value!.Trim().ToUpperInvariant() : null;

    public static bool IsValidColor(string? value) =>
        value is not null && ColorPattern().IsMatch(value);

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex ColorPattern();
}
