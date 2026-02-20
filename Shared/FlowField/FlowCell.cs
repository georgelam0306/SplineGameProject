using Core;

namespace FlowField;

/// <summary>
/// A single cell in a flow field containing the flow direction and distance to target.
/// </summary>
public struct FlowCell
{
    /// <summary>
    /// Normalized direction pointing toward the target (or zero if unreachable).
    /// </summary>
    public Fixed64Vec2 Direction;

    /// <summary>
    /// Distance cost to reach the target from this cell.
    /// </summary>
    public Fixed64 Distance;
}
