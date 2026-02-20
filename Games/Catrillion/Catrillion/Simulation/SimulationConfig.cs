using Core;

namespace Catrillion.Simulation;

/// <summary>
/// Global simulation timing configuration.
/// All time-based rates should be defined per-second and multiplied by DeltaSeconds.
/// </summary>
public static class SimulationConfig
{
    /// <summary>
    /// Simulation tick rate in frames per second.
    /// </summary>
    public const int TickRate = 30;

    /// <summary>
    /// Fixed delta time per frame in seconds (as Fixed64 for determinism).
    /// </summary>
    public static readonly Fixed64 FixedDeltaTime = Fixed64.FromInt(1) / TickRate;

    /// <summary>
    /// Fixed delta time per frame in seconds (as Fixed32 for timers).
    /// </summary>
    public static readonly Fixed32 FixedDeltaTime32 = Fixed32.FromInt(1) / TickRate;

    /// <summary>
    /// Compute effective delta time for a system with the given tick interval.
    /// </summary>
    public static Fixed64 GetDeltaSeconds(int tickInterval)
    {
        return FixedDeltaTime * Fixed64.FromInt(tickInterval);
    }

    /// <summary>
    /// Compute effective delta time (Fixed32) for a system with the given tick interval.
    /// </summary>
    public static Fixed32 GetDeltaSeconds32(int tickInterval)
    {
        return FixedDeltaTime32 * tickInterval;
    }
}
