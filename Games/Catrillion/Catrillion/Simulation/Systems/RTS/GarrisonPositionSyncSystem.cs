using Catrillion.Simulation.Components;
using Catrillion.Simulation.Grids;
using Core;

namespace Catrillion.Simulation.Systems.SimTable;

/// <summary>
/// Syncs garrisoned unit positions to their building's center for rendering.
/// Runs at the end of simulation tick so EndFrame() syncs the correct position to ECS.
/// Units retain building position while garrisoned; actual exit position is calculated on eject.
/// </summary>
public sealed class GarrisonPositionSyncSystem : SimTableSystem
{
    public GarrisonPositionSyncSystem(SimWorld world) : base(world)
    {
    }

    public override void Tick(in SimulationContext context)
    {
        var units = World.CombatUnitRows;
        var buildings = World.BuildingRows;

        for (int slot = 0; slot < units.Count; slot++)
        {
            if (!units.TryGetRow(slot, out var unit)) continue;
            if (!unit.Flags.HasFlag(MortalFlags.IsActive)) continue;

            // Skip non-garrisoned units
            if (!unit.GarrisonedInHandle.IsValid) continue;

            // Get the building this unit is garrisoned in
            int buildingSlot = buildings.GetSlot(unit.GarrisonedInHandle);
            if (!buildings.TryGetRow(buildingSlot, out var building)) continue;

            // Calculate building center position using WorldTile.TileSize
            Fixed64 centerX = Fixed64.FromInt((building.TileX * WorldTile.TileSize) + (building.Width * WorldTile.TileSize / 2));
            Fixed64 centerY = Fixed64.FromInt((building.TileY * WorldTile.TileSize) + (building.Height * WorldTile.TileSize / 2));

            // Update unit position to building center
            unit.Position = new Fixed64Vec2(centerX, centerY);
        }
    }
}
