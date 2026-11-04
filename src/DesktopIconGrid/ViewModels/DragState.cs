namespace DesktopIconGrid.ViewModels;

/// <summary>Tracks the item being dragged within the app so drop targets can validate without round-tripping the data object.</summary>
public static class DragState
{
    public const string ItemFormat = "DesktopIconGrid.ItemId";

    public static ItemViewModel? Current { get; set; }

    public static bool IsDragging => Current is not null;
}
