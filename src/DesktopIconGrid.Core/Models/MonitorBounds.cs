namespace DesktopIconGrid.Core.Models;

/// <summary>A monitor as Windows reports it: its device name (<c>\\.\DISPLAY1</c>) and its bounds in device pixels.</summary>
public sealed record MonitorBounds(string DeviceName, int Left, int Top, int Width, int Height)
{
    public int Right => Left + Width;

    public int Bottom => Top + Height;
}

/// <summary>A rectangle in device pixels.</summary>
public readonly record struct PixelRect(int Left, int Top, int Width, int Height)
{
    public int Right => Left + Width;

    public int Bottom => Top + Height;
}
