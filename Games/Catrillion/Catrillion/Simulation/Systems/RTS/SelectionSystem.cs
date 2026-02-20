using Catrillion.Core;
using Catrillion.GameData.Schemas;
using Catrillion.Simulation.Components;
using Core;

namespace Catrillion.Simulation.Systems.SimTable;

/// <summary>
/// Handles box selection of combat units and buildings in shared-base co-op mode.
/// On LMB release, selects all team units/buildings within the selection box.
/// Selection locking: if another player has a unit selected, it cannot be selected.
/// If nothing is selected, defaults to selecting the Command Center.
/// </summary>
public sealed class SelectionSystem : SimTableSystem
{
    private readonly int _tileSize;

    // Click detection threshold - if selection box is smaller than this, treat as single click
    private static readonly Fixed64 ClickThreshold = Fixed64.FromInt(8);
    // Radius for point selection (how close a unit needs to be to click point)
    private static readonly Fixed64 ClickRadius = Fixed64.FromInt(16);

    public SelectionSystem(SimWorld world, GameDataManager<GameDocDb> gameData) : base(world)
    {
        ref readonly var mapConfig = ref gameData.Db.MapConfigData.FindById(0);
        _tileSize = mapConfig.TileSize;
    }

    public override void Tick(in SimulationContext context)
    {
        for (int playerId = 0; playerId < context.PlayerCount; playerId++)
        {
            ref readonly var input = ref context.GetInput(playerId);

            // HasSelectionComplete is set in GameInputManager when LMB is released
            if (!input.HasSelectionComplete) continue;

            ClearPlayerSelection(playerId);

            // Determine if this is a click or a drag based on selection box size
            Fixed64 boxWidth = Fixed64.Abs(input.SelectEnd.X - input.SelectStart.X);
            Fixed64 boxHeight = Fixed64.Abs(input.SelectEnd.Y - input.SelectStart.Y);
            bool isClick = boxWidth < ClickThreshold && boxHeight < ClickThreshold;

            bool selectedAnything;
            if (isClick)
            {
                // Single click - select closest entity to click point
                selectedAnything = SelectEntityAtPoint(playerId, input.SelectEnd);
            }
            else
            {
                // Drag - use box selection
                selectedAnything = SelectEntitiesInBox(playerId, in input);
            }

            // If nothing was selected, default to Command Center
            if (!selectedAnything)
            {
                SelectCommandCenter(playerId);
            }
        }
    }

    private void ClearPlayerSelection(int playerId)
    {
        // Clear unit selection
        var units = World.CombatUnitRows;
        for (int slot = 0; slot < units.Count; slot++)
        {
            if (!units.TryGetRow(slot, out var row)) continue;
            if (!row.Flags.HasFlag(MortalFlags.IsActive)) continue;
            if (row.SelectedByPlayerId != playerId) continue;

            row.SelectedByPlayerId = -1;
        }

        // Clear building selection (include both active and under-construction)
        var buildings = World.BuildingRows;
        for (int slot = 0; slot < buildings.Count; slot++)
        {
            if (!buildings.TryGetRow(slot, out var row)) continue;
            if (!row.Flags.HasFlag(BuildingFlags.IsActive) &&
                !row.Flags.HasFlag(BuildingFlags.IsUnderConstruction)) continue;
            if (row.SelectedByPlayerId != playerId) continue;

            row.SelectedByPlayerId = -1;
        }
    }

    private bool SelectEntitiesInBox(int playerId, in Rollback.GameInput input)
    {
        // Create bounding box from start/end (handle any drag direction)
        Fixed64 minX = Fixed64.Min(input.SelectStart.X, input.SelectEnd.X);
        Fixed64 maxX = Fixed64.Max(input.SelectStart.X, input.SelectEnd.X);
        Fixed64 minY = Fixed64.Min(input.SelectStart.Y, input.SelectEnd.Y);
        Fixed64 maxY = Fixed64.Max(input.SelectStart.Y, input.SelectEnd.Y);

        // Convert to int for comparisons
        int minXInt = minX.ToInt();
        int maxXInt = maxX.ToInt();
        int minYInt = minY.ToInt();
        int maxYInt = maxY.ToInt();

        var units = World.CombatUnitRows;
        var buildings = World.BuildingRows;

        // ===== PASS 1: Find highest priority among valid candidates =====
        int highestPriority = int.MinValue;

        // Check units
        foreach (int slot in units.QueryBox(minX, maxX, minY, maxY))
        {
            if (!units.TryGetRow(slot, out var row)) continue;
            if (!row.Flags.HasFlag(MortalFlags.IsActive)) continue;
            if (row.SelectedByPlayerId >= 0 && row.SelectedByPlayerId != playerId) continue;

            if (row.SelectionPriority > highestPriority)
                highestPriority = row.SelectionPriority;
        }

        // Check buildings
        for (int slot = 0; slot < buildings.Count; slot++)
        {
            if (!buildings.TryGetRow(slot, out var row)) continue;
            if (!row.Flags.HasFlag(BuildingFlags.IsActive) &&
                !row.Flags.HasFlag(BuildingFlags.IsUnderConstruction)) continue;
            if (row.SelectedByPlayerId >= 0 && row.SelectedByPlayerId != playerId) continue;

            // Convert tile coords to world coords and check intersection
            int buildingMinX = row.TileX * _tileSize;
            int buildingMinY = row.TileY * _tileSize;
            int buildingMaxX = buildingMinX + row.Width * _tileSize;
            int buildingMaxY = buildingMinY + row.Height * _tileSize;

            if (buildingMaxX > minXInt && buildingMinX < maxXInt &&
                buildingMaxY > minYInt && buildingMinY < maxYInt)
            {
                if (row.SelectionPriority > highestPriority)
                    highestPriority = row.SelectionPriority;
            }
        }

        // No valid candidates found
        if (highestPriority == int.MinValue)
            return false;

        // ===== PASS 2: Select only entities with highest priority =====
        bool selectedAnything = false;

        // Select units with highest priority
        foreach (int slot in units.QueryBox(minX, maxX, minY, maxY))
        {
            if (!units.TryGetRow(slot, out var row)) continue;
            if (!row.Flags.HasFlag(MortalFlags.IsActive)) continue;
            if (row.SelectedByPlayerId >= 0 && row.SelectedByPlayerId != playerId) continue;

            if (row.SelectionPriority == highestPriority)
            {
                row.SelectedByPlayerId = playerId;
                selectedAnything = true;
            }
        }

        // Select buildings with highest priority
        for (int slot = 0; slot < buildings.Count; slot++)
        {
            if (!buildings.TryGetRow(slot, out var row)) continue;
            if (!row.Flags.HasFlag(BuildingFlags.IsActive) &&
                !row.Flags.HasFlag(BuildingFlags.IsUnderConstruction)) continue;
            if (row.SelectedByPlayerId >= 0 && row.SelectedByPlayerId != playerId) continue;

            int buildingMinX = row.TileX * _tileSize;
            int buildingMinY = row.TileY * _tileSize;
            int buildingMaxX = buildingMinX + row.Width * _tileSize;
            int buildingMaxY = buildingMinY + row.Height * _tileSize;

            if (buildingMaxX > minXInt && buildingMinX < maxXInt &&
                buildingMaxY > minYInt && buildingMinY < maxYInt)
            {
                if (row.SelectionPriority == highestPriority)
                {
                    row.SelectedByPlayerId = playerId;
                    selectedAnything = true;
                }
            }
        }

        return selectedAnything;
    }

    private bool SelectEntityAtPoint(int playerId, Fixed64Vec2 clickPoint)
    {
        // Find closest unit within click radius
        var units = World.CombatUnitRows;
        int closestUnitSlot = -1;
        Fixed64 closestDistSq = ClickRadius * ClickRadius;

        // Use spatial query with click radius
        foreach (int slot in units.QueryRadius(clickPoint, ClickRadius))
        {
            if (!units.TryGetRow(slot, out var row)) continue;
            if (!row.Flags.HasFlag(MortalFlags.IsActive)) continue;

            // Selection locking: skip if already selected by another player
            if (row.SelectedByPlayerId >= 0 && row.SelectedByPlayerId != playerId) continue;

            // Calculate distance to click point
            Fixed64 dx = row.Position.X - clickPoint.X;
            Fixed64 dy = row.Position.Y - clickPoint.Y;
            Fixed64 distSq = dx * dx + dy * dy;

            if (distSq < closestDistSq)
            {
                closestDistSq = distSq;
                closestUnitSlot = slot;
            }
        }

        // If we found a unit, select it
        if (closestUnitSlot >= 0 && units.TryGetRow(closestUnitSlot, out var selectedUnit))
        {
            selectedUnit.SelectedByPlayerId = playerId;
            return true;
        }

        // No unit found, try buildings
        var buildings = World.BuildingRows;
        int clickX = clickPoint.X.ToInt();
        int clickY = clickPoint.Y.ToInt();

        for (int slot = 0; slot < buildings.Count; slot++)
        {
            if (!buildings.TryGetRow(slot, out var row)) continue;
            if (!row.Flags.HasFlag(BuildingFlags.IsActive) &&
                !row.Flags.HasFlag(BuildingFlags.IsUnderConstruction)) continue;

            // Selection locking: skip if already selected by another player
            if (row.SelectedByPlayerId >= 0 && row.SelectedByPlayerId != playerId) continue;

            // Check if click is within building bounds
            int buildingMinX = row.TileX * _tileSize;
            int buildingMinY = row.TileY * _tileSize;
            int buildingMaxX = buildingMinX + row.Width * _tileSize;
            int buildingMaxY = buildingMinY + row.Height * _tileSize;

            if (clickX >= buildingMinX && clickX < buildingMaxX &&
                clickY >= buildingMinY && clickY < buildingMaxY)
            {
                row.SelectedByPlayerId = playerId;
                return true;
            }
        }

        return false;
    }

    private void SelectCommandCenter(int playerId)
    {
        var buildings = World.BuildingRows;
        for (int slot = 0; slot < buildings.Count; slot++)
        {
            if (!buildings.TryGetRow(slot, out var row)) continue;
            if (!row.Flags.HasFlag(BuildingFlags.IsActive)) continue;
            if (row.TypeId != BuildingTypeId.CommandCenter) continue;

            // Selection locking: skip if already selected by another player
            if (row.SelectedByPlayerId >= 0 && row.SelectedByPlayerId != playerId) continue;

            row.SelectedByPlayerId = playerId;
            return; // Only select first CC found
        }
    }
}
