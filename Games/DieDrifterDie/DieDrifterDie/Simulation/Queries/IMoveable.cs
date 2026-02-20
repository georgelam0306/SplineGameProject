using Core;
using SimTable;

namespace DieDrifterDie.Simulation.Components;

/// <summary>
/// Multi-table query interface for entities with position and velocity.
/// Auto-discovers: CombatUnitRow, ProjectileRow.
/// Use .ByTable() for optimal chunked iteration (~0% overhead).
/// </summary>
[MultiTableQuery]
public interface IMoveable
{
    ref Fixed64Vec2 Position { get; }
    ref Fixed64Vec2 Velocity { get; }
}
