using System.Reflection;
using BaseTemplate.Simulation.Components;
using Core;

namespace BaseTemplate.Simulation.Systems.SimTable;

public abstract class SimTableSystem
{
    protected readonly SimWorld World;

    /// <summary>
    /// Tick interval in frames. Systems with Interval > 1 only tick when
    /// (frame - Offset) % Interval == 0.
    /// </summary>
    public int TickInterval { get; }

    /// <summary>
    /// Frame offset for phased execution. Different systems with same interval
    /// but different offsets run on different frames, spreading work.
    /// </summary>
    public int TickOffset { get; }

    /// <summary>
    /// Effective delta time in seconds for this system, accounting for TickInterval.
    /// Use this to scale per-second rates.
    /// </summary>
    protected Fixed64 DeltaSeconds { get; }

    /// <summary>
    /// Effective delta time in seconds (Fixed32) for this system.
    /// Use for timer decrements where Fixed32 precision is sufficient.
    /// </summary>
    protected Fixed32 DeltaSeconds32 { get; }

    protected SimTableSystem(SimWorld world)
    {
        World = world;

        var attr = GetType().GetCustomAttribute<TickRateAttribute>();
        TickInterval = attr?.Interval ?? 1;
        TickOffset = attr?.Offset ?? 0;
        DeltaSeconds = SimulationConfig.GetDeltaSeconds(TickInterval);
        DeltaSeconds32 = SimulationConfig.GetDeltaSeconds32(TickInterval);
    }

    public virtual void Initialize()
    {
    }

    public abstract void Tick(in SimulationContext context);
}

