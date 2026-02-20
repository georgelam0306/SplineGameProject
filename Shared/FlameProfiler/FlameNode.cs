using System.Runtime.InteropServices;

namespace FlameProfiler;

/// <summary>
/// Represents a single node in the flame graph tree for one frame.
/// Nodes form a tree via ParentIndex, FirstChildIndex, and NextSiblingIndex.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct FlameNode
{
    /// <summary>Which scope this node represents (index into scope names array).</summary>
    public int ScopeId;

    /// <summary>Index of parent node in the frame's node array. -1 for root nodes.</summary>
    public int ParentIndex;

    /// <summary>Index of first child node. -1 if this is a leaf node.</summary>
    public int FirstChildIndex;

    /// <summary>Index of next sibling node. -1 if this is the last sibling.</summary>
    public int NextSiblingIndex;

    /// <summary>Timestamp when this scope started (Stopwatch ticks).</summary>
    public long StartTicks;

    /// <summary>Timestamp when this scope ended (Stopwatch ticks). 0 if not yet ended.</summary>
    public long EndTicks;

    /// <summary>
    /// Total time in this scope (EndTicks - StartTicks).
    /// Computed when the scope ends.
    /// </summary>
    public long TotalTicks => EndTicks - StartTicks;

    /// <summary>
    /// Self time (exclusive time not spent in child scopes).
    /// Computed after all children have ended: TotalTicks - sum of children's TotalTicks.
    /// </summary>
    public long SelfTicks;

    /// <summary>Depth in the call stack (0 = root level).</summary>
    public int Depth;

    /// <summary>
    /// Resets this node to default state for reuse.
    /// </summary>
    public void Reset()
    {
        ScopeId = 0;
        ParentIndex = -1;
        FirstChildIndex = -1;
        NextSiblingIndex = -1;
        StartTicks = 0;
        EndTicks = 0;
        SelfTicks = 0;
        Depth = 0;
    }
}
