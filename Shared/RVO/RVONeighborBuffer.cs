using System.Runtime.CompilerServices;
using Core;

namespace RVO;

/// <summary>
/// Pre-allocated buffer for gathering RVO neighbors.
/// Provides zero-allocation neighbor collection for hot paths.
/// </summary>
public sealed class RVONeighborBuffer
{
    private readonly RVOAgentSnapshot[] _buffer;
    private int _count;

    public RVONeighborBuffer(int maxNeighbors = 8)
    {
        _buffer = new RVOAgentSnapshot[maxNeighbors];
        _count = 0;
    }

    /// <summary>
    /// Maximum capacity of the buffer.
    /// </summary>
    public int Capacity => _buffer.Length;

    /// <summary>
    /// Current number of neighbors in the buffer.
    /// </summary>
    public int Count => _count;

    /// <summary>
    /// Whether the buffer has reached capacity.
    /// </summary>
    public bool IsFull => _count >= _buffer.Length;

    /// <summary>
    /// Clears the buffer for reuse.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Clear()
    {
        _count = 0;
    }

    /// <summary>
    /// Adds a neighbor to the buffer if not full.
    /// </summary>
    /// <returns>True if added, false if buffer was full</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryAdd(Fixed64Vec2 position, Fixed64Vec2 velocity, Fixed64 radius)
    {
        if (_count >= _buffer.Length)
        {
            return false;
        }

        _buffer[_count] = new RVOAgentSnapshot(position, velocity, radius);
        _count++;
        return true;
    }

    /// <summary>
    /// Adds a neighbor to the buffer if not full.
    /// </summary>
    /// <returns>True if added, false if buffer was full</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryAdd(in RVOAgentSnapshot snapshot)
    {
        if (_count >= _buffer.Length)
        {
            return false;
        }

        _buffer[_count] = snapshot;
        _count++;
        return true;
    }

    /// <summary>
    /// Gets a read-only span of the gathered neighbors.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySpan<RVOAgentSnapshot> AsSpan()
    {
        return new ReadOnlySpan<RVOAgentSnapshot>(_buffer, 0, _count);
    }

    /// <summary>
    /// Gets a neighbor by index.
    /// </summary>
    public ref readonly RVOAgentSnapshot this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => ref _buffer[index];
    }
}

/// <summary>
/// Delegate for spatial queries that enumerate neighbors within a radius.
/// Used to abstract away the specific spatial data structure.
/// </summary>
/// <param name="position">Center position for the query</param>
/// <param name="radiusSq">Squared radius for the query</param>
/// <returns>Enumerable of (position, velocity, radius) tuples for neighbors</returns>
public delegate IEnumerable<RVOAgentSnapshot> RVOSpatialQuery(Fixed64Vec2 position, Fixed64 radiusSq);

/// <summary>
/// Extension methods for RVO neighbor gathering.
/// </summary>
public static class RVONeighborExtensions
{
    /// <summary>
    /// Gathers neighbors using a spatial query delegate.
    /// Fills the buffer up to its capacity.
    /// </summary>
    /// <param name="buffer">Buffer to fill</param>
    /// <param name="query">Spatial query delegate</param>
    /// <param name="position">Center position</param>
    /// <param name="radiusSq">Squared query radius</param>
    /// <param name="skipSelf">Optional predicate to skip self (e.g., by position)</param>
    /// <returns>Number of neighbors gathered</returns>
    public static int Gather(
        this RVONeighborBuffer buffer,
        RVOSpatialQuery query,
        Fixed64Vec2 position,
        Fixed64 radiusSq,
        Func<RVOAgentSnapshot, bool>? skipSelf = null)
    {
        buffer.Clear();

        foreach (var neighbor in query(position, radiusSq))
        {
            if (skipSelf != null && skipSelf(neighbor))
            {
                continue;
            }

            if (!buffer.TryAdd(neighbor))
            {
                break; // Buffer full
            }
        }

        return buffer.Count;
    }
}
