using Core;
using SimTable;

namespace Catrillion.Simulation.Components;

/// <summary>
/// Multi-table query for entities that can be targeted by zombies.
/// Explicitly includes BuildingRow and CombatUnitRow only (excludes ZombieRow).
/// Used for unified spatial queries in zombie target acquisition.
/// </summary>
[MultiTableQuery(typeof(BuildingRow), typeof(CombatUnitRow))]
public interface IPlayerTargetableUnits
{
    ref Fixed64Vec2 Position { get; }
    ref int Health { get; }
}
