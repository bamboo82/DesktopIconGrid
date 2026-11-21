using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace DesktopIconGrid.Core.Models;

/// <summary>
/// The custom theme's background picture (0.15.0 WP5, the owner's request): a picture the program decoded and wrote anew
/// into the data folder's <c>backgrounds</c> folder, named by its content; the point of it that sits at the bar's centre
/// and how far it is zoomed (<see cref="BackgroundFit"/>); how strongly it lies over the theme's colour; and its average
/// colour, worked out when it was stored, so that the light or dark text is known before the picture is drawn. Stored with
/// the theme in <c>layout.json</c> and synced; the file travels as <c>icons</c> do (the import and the data-location copy,
/// not sync), and a PC without it shows the colour alone.
/// </summary>
public sealed partial record BackgroundImage
{
    public const string FolderName = "backgrounds";
    public const double DefaultFocus = 0.5;
    public const int MinZoom = 100;
    public const int MaxZoom = 400;
    public const int DefaultZoom = 100;
    public const int MinStrength = 20;
    public const int MaxStrength = 100;
    public const int DefaultStrength = 100;

    /// <summary>The stored picture's name in <c>backgrounds</c>: never a path (<see cref="IsStoredName"/>).</summary>
    public string File { get; init; } = string.Empty;

    /// <summary>The picture's point at the bar's centre, from its left (0) to its right (1).</summary>
    public double FocusX { get; init; } = DefaultFocus;

    /// <summary>The same from its top (0) to its bottom (1).</summary>
    public double FocusY { get; init; } = DefaultFocus;

    /// <summary>How far the picture is zoomed beyond just covering the bar, 100 to 400 percent.</summary>
    public int Zoom { get; init; } = DefaultZoom;

    /// <summary>How strongly the picture lies over the theme's colour, 20 to 100 percent.</summary>
    public int Strength { get; init; } = DefaultStrength;

    /// <summary>The picture's average colour, <c>#RRGGBB</c>; none when unknown.</summary>
    public string? Average { get; init; }

    /// <summary>
    /// Whether <paramref name="name"/> is one this program gives a stored picture: 32 hex digits and <c>.jpg</c> or
    /// <c>.png</c>. Nothing else is ever read from <c>backgrounds</c>, so a layout from elsewhere cannot point the program
    /// at another file.
    /// </summary>
    public static bool IsStoredName(string? name) => name is not null && StoredName().IsMatch(name);

    /// <summary>The name for a picture's encoded bytes: the start of their SHA-256, so the same picture is stored once.</summary>
    public static string NameFor(ReadOnlySpan<byte> encoded, bool png) =>
        Convert.ToHexString(SHA256.HashData(encoded))[..32].ToLowerInvariant() + (png ? ".png" : ".jpg");

    /// <summary>
    /// This picture with values out of range put back to their defaults (not to the nearer end); none when its file is
    /// no stored name.
    /// </summary>
    public BackgroundImage? Normalized() => IsStoredName(File)
        ? this with
        {
            FocusX = FocusX is >= 0 and <= 1 ? FocusX : DefaultFocus,
            FocusY = FocusY is >= 0 and <= 1 ? FocusY : DefaultFocus,
            Zoom = Zoom is >= MinZoom and <= MaxZoom ? Zoom : DefaultZoom,
            Strength = Strength is >= MinStrength and <= MaxStrength ? Strength : DefaultStrength,
            Average = LauncherSettings.IsValidColor(Average) ? Average!.ToUpperInvariant() : null,
        }
        : null;

    /// <summary>
    /// The pictures in <c>backgrounds</c> that no setting names any more, deleted at the next start: stored names only,
    /// never another file that is there.
    /// </summary>
    public static IReadOnlyList<string> Unused(IEnumerable<string> fileNames, string? inUse) =>
        fileNames.Where(IsStoredName).Where(name => !string.Equals(name, inUse, StringComparison.OrdinalIgnoreCase)).ToList();

    [GeneratedRegex("^[0-9a-f]{32}\\.(jpg|png)$")]
    private static partial Regex StoredName();
}

/// <summary>
/// Where the background picture lies on the bar (0.15.0 WP5 and WP6), pure maths. At zoom 100 % it is scaled so that it
/// just covers the bar, the larger of the two ratios, whether the bar is larger or smaller than it; the zoom multiplies
/// that. The focus point is the picture's point at the bar's centre; where that would show an empty edge the picture is
/// pushed back just enough, so there is never a gap. Resizing the bar applies the rule again; nothing else is stored.
/// </summary>
public static class BackgroundFit
{
    /// <summary>The part of the picture the bar shows, in the picture's pixels: never past its edges.</summary>
    public static (double X, double Y, double Width, double Height) SourceRect(
        double pictureWidth, double pictureHeight, double barWidth, double barHeight, double focusX, double focusY, int zoom)
    {
        if (pictureWidth <= 0 || pictureHeight <= 0 || barWidth <= 0 || barHeight <= 0)
        {
            return (0, 0, Math.Max(0, pictureWidth), Math.Max(0, pictureHeight));
        }

        var scale = Math.Max(barWidth / pictureWidth, barHeight / pictureHeight) * Math.Clamp(zoom, BackgroundImage.MinZoom, BackgroundImage.MaxZoom) / 100.0;
        var width = Math.Min(pictureWidth, barWidth / scale);
        var height = Math.Min(pictureHeight, barHeight / scale);
        var x = Math.Clamp((Math.Clamp(focusX, 0, 1) * pictureWidth) - (width / 2), 0, pictureWidth - width);
        var y = Math.Clamp((Math.Clamp(focusY, 0, 1) * pictureHeight) - (height / 2), 0, pictureHeight - height);
        return (x, y, width, height);
    }
}
