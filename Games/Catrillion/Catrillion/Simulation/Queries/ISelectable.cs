using Core;
using SimTable;

namespace Catrillion.Simulation.Components;

/// <summary>
/// Multi-table query for selectable entities (buildings and units).
/// Used for unified priority-based selection in drag-select.
/// </summary>
[MultiTableQuery(typeof(BuildingRow), typeof(CombatUnitRow))]
public interface ISelectable
{
    ref Fixed64Vec2 Position { get; }
    ref int SelectedByPlayerId { get; }
    ref int SelectionPriority { get; }
}
