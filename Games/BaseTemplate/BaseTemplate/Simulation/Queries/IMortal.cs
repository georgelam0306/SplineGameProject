using SimTable;

namespace BaseTemplate.Simulation.Components;

/// <summary>
/// Multi-table query interface for entities with health and death tracking.
/// Auto-discovers tables with: Health (int), DeathFrame (int).
/// Currently matches: CombatUnitRow.
///
/// Death is handled in two phases:
/// 1. Mark dead: When Health &lt;= 0, set IsDead flag and DeathFrame
/// 2. Cleanup: After DeathDelayFrames, actually Free() the entity
/// </summary>
[MultiTableQuery]
public interface IMortal
{
    ref int Health { get; }
    ref int DeathFrame { get; }
}
