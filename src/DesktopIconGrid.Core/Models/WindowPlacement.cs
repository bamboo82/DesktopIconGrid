namespace DesktopIconGrid.Core.Models;

/// <summary>Last known position and size of the bar, in device-independent pixels.</summary>
public sealed class WindowPlacement
{
    public double Left { get; set; }
    public double Top { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }

    public bool IsUsable => Width > 0 && Height > 0
        && double.IsFinite(Left) && double.IsFinite(Top)
        && double.IsFinite(Width) && double.IsFinite(Height);
}
