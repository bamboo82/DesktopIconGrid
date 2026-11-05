namespace DesktopIconGrid.Core.Models;

/// <summary>Where a window opened at the mouse pointer goes: centred on the pointer, its top edge just above it, inside the work area.</summary>
public static class PopupPlacement
{
    /// <summary>
    /// A <paramref name="width"/> × <paramref name="height"/> rectangle in device pixels, centred on the pointer
    /// (<paramref name="x"/>, <paramref name="y"/>) with its top edge <paramref name="grabOffset"/> above it, then moved
    /// (and shrunk when it is bigger) to lie inside <paramref name="workArea"/>, the pointer's monitor without the taskbar.
    /// </summary>
    public static PixelRect AtPointer(int x, int y, int width, int height, PixelRect workArea, int grabOffset = 0)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        if (workArea.Width <= 0 || workArea.Height <= 0)
        {
            return new PixelRect(x - width / 2, y - grabOffset, width, height);
        }

        width = Math.Min(width, workArea.Width);
        height = Math.Min(height, workArea.Height);
        var left = Math.Clamp(x - width / 2, workArea.Left, workArea.Right - width);
        var top = Math.Clamp(y - grabOffset, workArea.Top, workArea.Bottom - height);
        return new PixelRect(left, top, width, height);
    }
}
