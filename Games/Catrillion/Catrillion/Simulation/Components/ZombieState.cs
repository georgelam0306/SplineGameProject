namespace Catrillion.Simulation.Components;

/// <summary>
/// Zombie behavioral state for state machine AI.
/// Values are byte-sized for efficient storage in ZombieRow.
/// </summary>
public enum ZombieState : byte
{
    /// <summary>
    /// Standing still, not moving. Transitions to Wander after idle timer expires.
    /// Entry: No movement input. Exit: Timer expires OR threat detected.
    /// </summary>
    Idle = 0,

    /// <summary>
    /// Moving randomly within local area. Low threat awareness.
    /// Entry: From Idle timeout. Exit: Threat threshold reached OR timer expires (back to Idle).
    /// </summary>
    Wander = 1,

    /// <summary>
    /// Actively pursuing a threat source. Uses flow field navigation.
    /// Entry: Threat exceeds chase threshold. Exit: Target in attack range OR threat lost.
    /// </summary>
    Chase = 2,

    /// <summary>
    /// Attacking target within range. Stationary with attack cooldown.
    /// Entry: Target in attack range. Exit: Target destroyed OR target moves out of range.
    /// </summary>
    Attack = 3,

    /// <summary>
    /// Wave-spawned zombie persistently moving toward command center.
    /// Uses center flow field, never transitions to Idle/Wander.
    /// Entry: Spawned by wave system. Exit: Target in attack range (to Attack).
    /// </summary>
    WaveChase = 4,
}
