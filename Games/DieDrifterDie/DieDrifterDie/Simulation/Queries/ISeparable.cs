using Core;
using SimTable;

namespace DieDrifterDie.Simulation.Components;

/// <summary>
/// Multi-table query interface for entities that need separation/RVO avoidance.
/// Auto-discovers: CombatUnitRow.
/// Use .ByTable() for optimal chunked iteration (~0% overhead).
/// </summary>
[MultiTableQuery]
public interface ISeparable
{
    ref Fixed64Vec2 Position { get; }
    ref Fixed64Vec2 Velocity { get; }
    ref Fixed64Vec2 PreferredVelocity { get; }  // Desired velocity before RVO
    ref Fixed64Vec2 SmoothedSeparation { get; }
    ref Fixed64 AgentRadius { get; }            // Collision radius for RVO
    ref Fixed64 MoveSpeed { get; }
}
