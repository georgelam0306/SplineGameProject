using Catrillion.Simulation.Components;
using Catrillion.Simulation.Grids;
using Core;
using SimTable;

namespace Catrillion.Simulation.Systems.SimTable;

/// <summary>
/// Monitors units with EnterGarrison order and handles them entering buildings
/// when they arrive at the destination.
/// </summary>
public sealed class GarrisonEnterSystem : SimTableSystem
{

    // Distance from building edge to trigger garrison entry (squared)
    private static readonly Fixed64 EdgeThresholdSq = Fixed64.FromInt(32 * 32);  // 32 units from edge

    public GarrisonEnterSystem(SimWorld world) : base(world)
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
            if (unit.CurrentOrder != OrderType.EnterGarrison) continue;
            if (!unit.OrderTargetHandle.IsValid) continue;

            // Get target building
            int buildingSlot = buildings.GetSlot(unit.OrderTargetHandle);
            if (!buildings.TryGetRow(buildingSlot, out var building))
            {
                // Building no longer exists, cancel order
                unit.CurrentOrder = OrderType.None;
                unit.OrderTargetHandle = SimHandle.Invalid;
                continue;
            }

            // Check if building is dead
            if (building.Flags.IsDead())
            {
                unit.CurrentOrder = OrderType.None;
                unit.OrderTargetHandle = SimHandle.Invalid;
                continue;
            }

            // Check if unit is close enough to building edge to enter
            Fixed64 distToEdgeSq = CalculateDistanceToEdgeSq(unit.Position, building);

            if (distToEdgeSq > EdgeThresholdSq) continue;

            // Check if building still has space
            if (!building.HasGarrisonSpace())
            {
                // Building is full, cancel order
                unit.CurrentOrder = OrderType.None;
                unit.OrderTargetHandle = SimHandle.Invalid;
                continue;
            }

            // Enter garrison
            if (building.TryAddToGarrison(units.GetHandle(slot)))
            {
                unit.GarrisonedInHandle = unit.OrderTargetHandle;
                unit.Velocity = Fixed64Vec2.Zero;
                unit.CurrentOrder = OrderType.None;
                unit.OrderTarget = Fixed64Vec2.Zero;
                unit.OrderTargetHandle = SimHandle.Invalid;
                unit.SelectedByPlayerId = -1;  // Clear selection when garrisoned
            }
        }
    }

    /// <summary>
    /// Calculate squared distance from unit position to nearest edge of building.
    /// </summary>
    private Fixed64 CalculateDistanceToEdgeSq(Fixed64Vec2 unitPos, BuildingRowRowRef building)
    {
        // Building bounds in world coordinates
        Fixed64 buildingLeft = Fixed64.FromInt(building.TileX * WorldTile.TileSize);
        Fixed64 buildingRight = Fixed64.FromInt((building.TileX + building.Width) * WorldTile.TileSize);
        Fixed64 buildingTop = Fixed64.FromInt(building.TileY * WorldTile.TileSize);
        Fixed64 buildingBottom = Fixed64.FromInt((building.TileY + building.Height) * WorldTile.TileSize);

        // Find closest point on building AABB to unit
        Fixed64 closestX = unitPos.X;
        if (closestX < buildingLeft) closestX = buildingLeft;
        else if (closestX > buildingRight) closestX = buildingRight;

        Fixed64 closestY = unitPos.Y;
        if (closestY < buildingTop) closestY = buildingTop;
        else if (closestY > buildingBottom) closestY = buildingBottom;

        // Distance from unit to closest point
        Fixed64 dx = unitPos.X - closestX;
        Fixed64 dy = unitPos.Y - closestY;

        return dx * dx + dy * dy;
    }
}
