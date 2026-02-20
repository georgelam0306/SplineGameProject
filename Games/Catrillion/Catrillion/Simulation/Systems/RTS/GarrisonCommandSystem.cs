using Catrillion.Simulation.Components;
using Catrillion.Simulation.Grids;
using Core;
using SimTable;

namespace Catrillion.Simulation.Systems.SimTable;

/// <summary>
/// Processes garrison enter commands from input.
/// When a player right-clicks on a friendly building with garrison capacity,
/// assigns EnterGarrison order to selected infantry units.
/// Also detects move commands that land on friendly buildings and converts them to garrison commands.
/// </summary>
public sealed class GarrisonCommandSystem : SimTableSystem
{

    public GarrisonCommandSystem(SimWorld world) : base(world)
    {
    }

    public override void Tick(in SimulationContext context)
    {
        // Commands not processed during countdown
        if (!World.IsPlaying()) return;

        for (int playerId = 0; playerId < context.PlayerCount; playerId++)
        {
            ref readonly var input = ref context.GetInput(playerId);

            // Explicit garrison command (if UI provides building handle directly)
            if (input.HasEnterGarrisonCommand && input.GarrisonTargetHandle.IsValid)
            {
                IssueGarrisonCommand(playerId, input.GarrisonTargetHandle);
                continue;
            }

            // Check if move command lands on a friendly building with garrison capacity
            if (input.HasMoveCommand)
            {
                var buildingHandle = FindBuildingAtPosition(playerId, input.MoveTarget);
                if (buildingHandle.IsValid)
                {
                    IssueGarrisonCommand(playerId, buildingHandle);
                }
            }
        }
    }

    /// <summary>
    /// Find a friendly building with garrison capacity at the given world position.
    /// </summary>
    private SimHandle FindBuildingAtPosition(int playerId, Fixed64Vec2 worldPos)
    {
        var buildings = World.BuildingRows;

        // Convert world position to tile coordinates using generated API
        var tile = WorldTile.FromPixel(worldPos);
        int tileX = tile.X;
        int tileY = tile.Y;

        for (int slot = 0; slot < buildings.Count; slot++)
        {
            if (!buildings.TryGetRow(slot, out var building)) continue;
            if (!building.Flags.HasFlag(BuildingFlags.IsActive)) continue;
            if (building.Flags.IsDead()) continue;

            // Check if tile is within building footprint
            if (tileX >= building.TileX && tileX < building.TileX + building.Width &&
                tileY >= building.TileY && tileY < building.TileY + building.Height)
            {
                // Must be friendly building with garrison capacity
                if (building.OwnerPlayerId == playerId && building.GarrisonCapacity > 0)
                {
                    return buildings.GetHandle(slot);
                }
            }
        }

        return SimHandle.Invalid;
    }

    private void IssueGarrisonCommand(int playerId, SimHandle targetBuildingHandle)
    {
        var units = World.CombatUnitRows;
        var buildings = World.BuildingRows;

        // Validate target building
        int buildingSlot = buildings.GetSlot(targetBuildingHandle);
        if (!buildings.TryGetRow(buildingSlot, out var building)) return;

        // Must be a friendly building with garrison capacity
        if (building.OwnerPlayerId != playerId) return;
        if (building.GarrisonCapacity == 0) return;
        if (!building.HasGarrisonSpace()) return;

        // Assign garrison order to all selected infantry units
        for (int slot = 0; slot < units.Count; slot++)
        {
            if (!units.TryGetRow(slot, out var unit)) continue;
            if (!unit.Flags.HasFlag(MortalFlags.IsActive)) continue;
            if (unit.SelectedByPlayerId != playerId) continue;

            // Skip units that are already garrisoned
            if (unit.GarrisonedInHandle.IsValid) continue;

            // TODO: Check if unit type can garrison (CanGarrison flag)
            // For now, we allow all units to garrison

            unit.CurrentOrder = OrderType.EnterGarrison;
            // Set target to nearest edge of building (center is blocked)
            unit.OrderTarget = CalculateGarrisonApproachPosition(unit.Position, building);
            unit.OrderTargetHandle = targetBuildingHandle;
        }
    }

    /// <summary>
    /// Calculate a position just outside the building edge, nearest to the unit.
    /// Building tiles are blocked, so we need to pathfind to an adjacent tile.
    /// </summary>
    private Fixed64Vec2 CalculateGarrisonApproachPosition(Fixed64Vec2 unitPos, BuildingRowRowRef building)
    {
        // Building bounds in world coordinates
        Fixed64 buildingLeft = Fixed64.FromInt(building.TileX * WorldTile.TileSize);
        Fixed64 buildingRight = Fixed64.FromInt((building.TileX + building.Width) * WorldTile.TileSize);
        Fixed64 buildingTop = Fixed64.FromInt(building.TileY * WorldTile.TileSize);
        Fixed64 buildingBottom = Fixed64.FromInt((building.TileY + building.Height) * WorldTile.TileSize);

        // Clamp unit position to building bounds to find closest edge point
        Fixed64 clampedX = unitPos.X;
        Fixed64 clampedY = unitPos.Y;

        // Determine which edge the unit should approach from
        Fixed64 distToLeft = unitPos.X - buildingLeft;
        Fixed64 distToRight = buildingRight - unitPos.X;
        Fixed64 distToTop = unitPos.Y - buildingTop;
        Fixed64 distToBottom = buildingBottom - unitPos.Y;

        // Use absolute values for comparison
        if (distToLeft < Fixed64.Zero) distToLeft = -distToLeft;
        if (distToRight < Fixed64.Zero) distToRight = -distToRight;
        if (distToTop < Fixed64.Zero) distToTop = -distToTop;
        if (distToBottom < Fixed64.Zero) distToBottom = -distToBottom;

        // Find minimum distance to edge
        Fixed64 minDist = distToLeft;
        Fixed64 targetX = buildingLeft - Fixed64.FromInt(16);  // Just outside left edge
        Fixed64 targetY = building.Position.Y;

        if (distToRight < minDist)
        {
            minDist = distToRight;
            targetX = buildingRight + Fixed64.FromInt(16);  // Just outside right edge
            targetY = building.Position.Y;
        }

        if (distToTop < minDist)
        {
            minDist = distToTop;
            targetX = building.Position.X;
            targetY = buildingTop - Fixed64.FromInt(16);  // Just outside top edge
        }

        if (distToBottom < minDist)
        {
            targetX = building.Position.X;
            targetY = buildingBottom + Fixed64.FromInt(16);  // Just outside bottom edge
        }

        return new Fixed64Vec2(targetX, targetY);
    }
}
