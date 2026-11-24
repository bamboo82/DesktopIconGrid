namespace DesktopIconGrid.Core.Models;

/// <summary>
/// Where a window opened at the mouse pointer goes: by default centred on the pointer with its top edge just above it,
/// or with a chosen point of it under the pointer (0.16.0 WP2), inside the work area either way.
/// </summary>
public static class PopupPlacement
{
    /// <summary>
    /// A <paramref name="width"/> × <paramref name="height"/> rectangle in device pixels around the pointer
    /// (<paramref name="x"/>, <paramref name="y"/>), then moved (and shrunk when it is bigger) to lie inside
    /// <paramref name="workArea"/>, the pointer's monitor without the taskbar; with no work area known it stays where it
    /// is put.
    /// <para>
    /// Without <paramref name="anchor"/> (or with one out of range): centred on the pointer, its top edge
    /// <paramref name="grabOffset"/> above it, as before 0.16.0. With one (0.16.0 WP2): its point at
    /// <see cref="PointerAnchor.X"/> percent of its width and <see cref="PointerAnchor.Y"/> percent of its height is put on
    /// the pointer, in whole pixels (left = x − width × X / 100, top = y − height × Y / 100, so 50 centres it as the
    /// default does), and <paramref name="grabOffset"/> is not used.
    /// </para>
    /// </summary>
    public static PixelRect AtPointer(int x, int y, int width, int height, PixelRect workArea, int grabOffset = 0, PointerAnchor? anchor = null)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        var known = workArea.Width > 0 && workArea.Height > 0;
        if (known)
        {
            width = Math.Min(width, workArea.Width);
            height = Math.Min(height, workArea.Height);
        }

        var (left, top) = anchor is { IsValid: true } point
            ? (x - (width * point.X / 100), y - (height * point.Y / 100))
            : (x - (width / 2), y - grabOffset);
        return known
            ? new PixelRect(Math.Clamp(left, workArea.Left, workArea.Right - width), Math.Clamp(top, workArea.Top, workArea.Bottom - height), width, height)
            : new PixelRect(left, top, width, height);
    }
}
