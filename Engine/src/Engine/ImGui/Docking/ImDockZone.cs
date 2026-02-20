namespace DerpLib.ImGui.Docking;

/// <summary>
/// Dock zone for drop preview - where a window can be docked within a leaf.
/// </summary>
public enum ImDockZone
{
    None,
    Center,  // Add as tab to existing leaf
    Left,
    Right,
    Top,
    Bottom
}

/// <summary>
/// Split direction for dock splits.
/// </summary>
public enum ImSplitDirection
{
    Horizontal,  // Side by side (left/right)
    Vertical     // Stacked (top/bottom)
}

/// <summary>
/// Zero-allocation extension methods for ImDockZone.
/// </summary>
public static class ImDockZoneExtensions
{
    private static readonly string[] Names = ["None", "Center", "Left", "Right", "Top", "Bottom"];

    /// <summary>
    /// Gets the name of the dock zone without allocating (returns cached string).
    /// </summary>
    public static string ToName(this ImDockZone zone) => Names[(int)zone];
}
