using System;

namespace DieDrifterDie.Simulation.Systems.SimTable;

/// <summary>
/// Specifies the tick rate for a simulation system.
/// Systems with Interval > 1 only tick when (frame - Offset) % Interval == 0.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class TickRateAttribute : Attribute
{
    /// <summary>
    /// Tick interval in frames. Default is 1 (every frame).
    /// </summary>
    public int Interval { get; }

    /// <summary>
    /// Frame offset for phased execution. Default is 0.
    /// Different systems with same interval but different offsets will run on different frames,
    /// spreading work across frames to avoid spikes.
    /// </summary>
    public int Offset { get; }

    public TickRateAttribute(int interval = 1, int offset = 0)
    {
        Interval = Math.Max(1, interval);
        Offset = Math.Clamp(offset, 0, Math.Max(0, interval - 1));
    }
}
