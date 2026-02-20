using Core;
using SimTable;

namespace Catrillion.Simulation.Components;

/// <summary>
/// Multi-table query interface for entities with position and velocity.
/// Auto-discovers: ZombieRow, CombatUnitRow, ProjectileRow.
/// Use .ByTable() for optimal chunked iteration (~0% overhead).
/// </summary>
[MultiTableQuery]
public interface IMoveable
{
    ref Fixed64Vec2 Position { get; }
    ref Fixed64Vec2 Velocity { get; }
}
