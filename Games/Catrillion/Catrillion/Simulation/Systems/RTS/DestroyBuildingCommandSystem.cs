using Catrillion.Core;
using Catrillion.GameData.Schemas;
using Catrillion.Simulation.Components;
using Catrillion.Simulation.Grids;
using Catrillion.Simulation.Services;
using Catrillion.Simulation.Systems.SimTable;
using Core;
using FlowField;
using SimTable;

namespace Catrillion.Simulation.Systems.RTS;

/// <summary>
/// Processes building destroy commands from input.
/// When a player confirms destruction of their own building, validates and performs the destruction.
/// Returns a partial refund (50% of original build cost).
/// </summary>
public sealed class DestroyBuildingCommandSystem : SimTableSystem
{
    private readonly GameDataManager<GameDocDb> _gameData;
    private readonly IZoneFlowService _flowService;
    private readonly TerrainDataService _terrainData;
    private readonly PowerNetworkService _powerNetwork;

    /// <summary>Refund percentage of original build cost when self-destructing.</summary>
    private const int RefundPercentage = 50;

    public DestroyBuildingCommandSystem(
        SimWorld world,
        GameDataManager<GameDocDb> gameData,
        IZoneFlowService flowService,
        TerrainDataService terrainData,
        PowerNetworkService powerNetwork) : base(world)
    {
        _gameData = gameData;
        _flowService = flowService;
        _terrainData = terrainData;
        _powerNetwork = powerNetwork;
    }

    public override void Tick(in SimulationContext context)
    {
        if (!World.IsPlaying()) return;

        var buildings = World.BuildingRows;
        var gameResources = World.GameResourcesRows;
        var buildingDb = _gameData.Db.BuildingTypeData;

        for (int playerId = 0; playerId < context.PlayerCount; playerId++)
        {
            ref readonly var input = ref context.GetInput(playerId);

            if (!input.HasDestroyCommand) continue;

            // Find the building to destroy
            int buildingSlot = buildings.GetSlot(input.DestroyBuildingHandle);
            if (buildingSlot < 0) continue;

            var building = buildings.GetRowBySlot(buildingSlot);

            // Validate ownership
            if (building.OwnerPlayerId != playerId) continue;
            if (building.Flags.IsDead()) continue;
            if (!building.Flags.HasFlag(BuildingFlags.IsActive)) continue;

            // Command Center is not destroyable
            if (building.TypeId == BuildingTypeId.CommandCenter) continue;

            // Get building type data
            int typeId = (int)building.TypeId;
            if (typeId < 0 || typeId >= buildingDb.Count) continue;

            ref readonly var typeData = ref buildingDb.FindById(typeId);

            // Calculate and grant refund (50% of original cost)
            var resources = gameResources.GetRowBySlot(0);
            resources.Gold += typeData.CostGold * RefundPercentage / 100;
            resources.Wood += typeData.CostWood * RefundPercentage / 100;
            resources.Stone += typeData.CostStone * RefundPercentage / 100;
            resources.Iron += typeData.CostIron * RefundPercentage / 100;
            resources.Oil += typeData.CostOil * RefundPercentage / 100;

            // Reduce max population if building provided housing
            if (typeData.ProvidesMaxPopulation > 0)
            {
                resources.MaxPopulation -= typeData.ProvidesMaxPopulation;
            }

            // Eject garrisoned units before destroying (without damage for self-destruct)
            EjectGarrisonedUnits(ref building);

            // Mark building as dead - BuildingDeathSystem will handle cleanup after delay
            building.Health = 0;
            building.Flags |= BuildingFlags.IsDead;
            building.DeathFrame = context.CurrentFrame;
        }
    }

    /// <summary>
    /// Ejects all garrisoned units from a building being destroyed.
    /// Unlike forced eject during combat death, self-destruct ejection doesn't damage units.
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
            // No HP damage on voluntary self-destruct eject (unlike combat death)
            unit.GarrisonedInHandle = SimHandle.Invalid;
            unit.Position = buildingPos;  // Eject at building center
            unit.Velocity = Fixed64Vec2.Zero;
        }

        slotHandle = SimHandle.Invalid;
    }
}
