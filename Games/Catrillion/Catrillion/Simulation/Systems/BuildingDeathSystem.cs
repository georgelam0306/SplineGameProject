using Catrillion.Simulation.Components;
using Catrillion.Simulation.Grids;
using Catrillion.Simulation.Services;
using Core;
using FlowField;
using SimTable;

namespace Catrillion.Simulation.Systems.SimTable;

/// <summary>
/// Handles death for buildings when Health drops to 0.
/// Marks buildings as dead and frees them after a delay.
/// Also invalidates flow field tiles when buildings are destroyed.
/// </summary>
public sealed class BuildingDeathSystem : SimTableSystem
{
    private readonly IZoneFlowService _flowService;
    private readonly TerrainDataService _terrainData;
    private readonly PowerNetworkService _powerNetwork;

    /// <summary>
    /// Number of frames to wait before freeing a dead building.
    /// This gives rendering time to show destruction effects.
    /// </summary>
    private const int DeathDelayFrames = 30;

    public BuildingDeathSystem(
        SimWorld world,
        IZoneFlowService flowService,
        TerrainDataService terrainData,
        PowerNetworkService powerNetwork) : base(world)
    {
        _flowService = flowService;
        _terrainData = terrainData;
        _powerNetwork = powerNetwork;
    }

    public override void Tick(in SimulationContext context)
    {
        int currentFrame = context.CurrentFrame;

        var buildings = World.BuildingRows;

        // Iterate backwards for safe removal
        for (int slot = buildings.Count - 1; slot >= 0; slot--)
        {
            if (!buildings.TryGetRow(slot, out var building)) continue;

            // Skip inactive buildings
            if (!building.Flags.HasFlag(BuildingFlags.IsActive)) continue;

            // Phase 1: Mark as dead if health <= 0
            if (building.Health <= 0 && !building.Flags.HasFlag(BuildingFlags.IsDead))
            {
                // Eject garrisoned units before marking as dead
                EjectGarrisonedUnits(ref building);

                building.Flags |= BuildingFlags.IsDead;
                building.DeathFrame = currentFrame;
                continue;
            }

            // Phase 2: Free dead buildings after delay
            if (building.Flags.HasFlag(BuildingFlags.IsDead))
            {
                if (currentFrame - building.DeathFrame >= DeathDelayFrames)
                {
                    // Mark flow field tiles dirty for the building footprint
                    int tileX = building.TileX;
                    int tileY = building.TileY;
                    for (int tx = tileX; tx < tileX + building.Width; tx++)
                    {
                        for (int ty = tileY; ty < tileY + building.Height; ty++)
                        {
                            _flowService.MarkTileDirty(tx, ty);
                        }
                    }

                    // Mark tiles as unblocked in occupancy grid
                    _terrainData.MarkBuildingTiles(tileX, tileY, building.Width, building.Height, blocked: false);

                    buildings.Free(buildings.GetHandle(slot));

                    // Rebuild spatial hash and invalidate power network
                    buildings.SpatialSort();
                    _powerNetwork.InvalidateNetwork();
                }
            }
        }
    }

    /// <summary>
    /// Ejects all garrisoned units from a dying building.
    /// Units take 50% HP damage when forcibly ejected.
    /// </summary>
    private void EjectGarrisonedUnits(ref BuildingRowRowRef building)
    {
        if (building.GarrisonCount == 0) return;

        var units = World.CombatUnitRows;
        var slots = building.GarrisonSlotArray;

        // Eject each slot using span accessor
        for (int i = 0; i < slots.Length; i++)
        {
            EjectFromSlot(ref slots[i], building.Position, units);
        }

        building.GarrisonCount = 0;
    }

    private void EjectFromSlot(ref SimHandle slotHandle, Fixed64Vec2 buildingPos, CombatUnitRowTable units)
    {
        if (!slotHandle.IsValid) return;

        int unitSlot = units.GetSlot(slotHandle);
        if (units.TryGetRow(unitSlot, out var unit))
        {
            // Apply 50% HP damage on forced eject
            unit.Health = unit.Health / 2;
            unit.GarrisonedInHandle = SimHandle.Invalid;
            unit.Position = buildingPos;  // Eject at building center
            unit.Velocity = Fixed64Vec2.Zero;
        }

        slotHandle = SimHandle.Invalid;
    }
}
