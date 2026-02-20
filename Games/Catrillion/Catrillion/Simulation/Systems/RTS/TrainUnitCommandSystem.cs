using Catrillion.Core;
using Catrillion.GameData.Schemas;
using Catrillion.Simulation.Components;
using SimTable;

namespace Catrillion.Simulation.Systems.SimTable;

/// <summary>
/// Processes player input to queue unit training at production buildings.
/// Validates tech requirements, resources, and population before starting production.
/// </summary>
public sealed class TrainUnitCommandSystem : SimTableSystem
{
    private readonly GameDataManager<GameDocDb> _gameData;

    public TrainUnitCommandSystem(SimWorld world, GameDataManager<GameDocDb> gameData)
        : base(world)
    {
        _gameData = gameData;
    }

    public override void Tick(in SimulationContext context)
    {
        if (!World.IsPlaying()) return;

        for (int playerId = 0; playerId < context.PlayerCount; playerId++)
        {
            ref readonly var input = ref context.GetInput(playerId);

            // Process cancel training command first
            if (input.HasCancelTrainingCommand)
            {
                ProcessCancelCommand(playerId, input.CancelTrainingBuildingHandle, input.CancelTrainingSlotIndex);
            }

            // Process train command
            if (input.HasTrainUnitCommand)
            {
                ProcessTrainCommand(playerId, input.TrainUnitBuildingHandle, input.TrainUnitTypeId);
            }
        }
    }

    private void ProcessTrainCommand(int playerId, SimHandle buildingHandle, byte unitTypeId)
    {
        var buildings = World.BuildingRows;

        // Validate building exists and is active
        var slot = buildings.GetSlot(buildingHandle);
        if (!buildings.TryGetRow(slot, out var building)) return;
        if (!building.Flags.HasFlag(BuildingFlags.IsActive)) return;

        // Validate building is powered (if it requires power)
        ref readonly var buildingTypeData = ref _gameData.Db.BuildingTypeData.FindById((int)building.TypeId);
        if (buildingTypeData.RequiresPower && !building.Flags.HasFlag(BuildingFlags.IsPowered))
        {
            return;  // Building not powered
        }

        // Find first empty queue slot (255 = empty)
        var queue = building.ProductionQueueArray;
        int emptySlot = -1;
        for (int i = 0; i < queue.Length; i++)
        {
            if (queue[i] == 255)
            {
                emptySlot = i;
                break;
            }
        }

        if (emptySlot < 0)
        {
            return;  // Queue is full
        }

        // Validate unit type exists and can be trained at this building
        if (unitTypeId >= _gameData.Db.UnitTypeData.Count)
        {
            return;  // Invalid unit type
        }

        ref readonly var unitData = ref _gameData.Db.UnitTypeData.FindById(unitTypeId);

        // Validate this building type can train this unit
        if (unitData.TrainedAtBuildingType != (int)building.TypeId)
        {
            return;  // Wrong building type
        }

        // Validate tech requirements
        var resources = World.GameResourcesRows.GetRowBySlot(0);
        if (unitData.RequiredTechId > 0 && ((resources.UnlockedTech >> unitData.RequiredTechId) & 1) != 1)
        {
            return;  // Tech not unlocked
        }

        // Validate affordability
        if (resources.Gold < unitData.CostGold ||
            resources.Wood < unitData.CostWood ||
            resources.Stone < unitData.CostStone ||
            resources.Iron < unitData.CostIron ||
            resources.Oil < unitData.CostOil)
        {
            return;  // Can't afford
        }

        // Validate population capacity
        if (resources.Population + unitData.PopulationCost > resources.MaxPopulation)
        {
            return;  // No population space
        }

        // All validations passed - deduct costs and add to queue
        resources.Gold -= unitData.CostGold;
        resources.Wood -= unitData.CostWood;
        resources.Stone -= unitData.CostStone;
        resources.Iron -= unitData.CostIron;
        resources.Oil -= unitData.CostOil;

        // Add to queue
        queue[emptySlot] = unitTypeId;

        // If this is slot 0, start production timer
        if (emptySlot == 0)
        {
            building.ProductionProgress = 0;
            building.ProductionBuildTime = unitData.BuildTime;
        }
    }

    private void ProcessCancelCommand(int playerId, SimHandle buildingHandle, byte slotIndex)
    {
        var buildings = World.BuildingRows;

        // Validate building exists and is active
        var slot = buildings.GetSlot(buildingHandle);
        if (!buildings.TryGetRow(slot, out var building)) return;
        if (!building.Flags.HasFlag(BuildingFlags.IsActive)) return;

        var queue = building.ProductionQueueArray;

        // Validate slot index
        if (slotIndex >= queue.Length) return;

        // Validate there's something to cancel (255 = empty)
        if (queue[slotIndex] == 255) return;

        // Get unit data for refund
        byte unitTypeId = queue[slotIndex];
        ref readonly var unitData = ref _gameData.Db.UnitTypeData.FindById(unitTypeId);

        // Refund resources (full refund)
        var resources = World.GameResourcesRows.GetRowBySlot(0);
        resources.Gold += unitData.CostGold;
        resources.Wood += unitData.CostWood;
        resources.Stone += unitData.CostStone;
        resources.Iron += unitData.CostIron;
        resources.Oil += unitData.CostOil;

        // Shift remaining items forward in queue
        for (int i = slotIndex; i < queue.Length - 1; i++)
        {
            queue[i] = queue[i + 1];
        }
        queue[queue.Length - 1] = 255;  // Last slot is now empty

        // If we canceled slot 0, reset progress and set build time for new front item
        if (slotIndex == 0)
        {
            building.ProductionProgress = 0;
            if (queue[0] != 255)
            {
                ref readonly var nextUnitData = ref _gameData.Db.UnitTypeData.FindById(queue[0]);
                building.ProductionBuildTime = nextUnitData.BuildTime;
            }
            else
            {
                building.ProductionBuildTime = 0;
            }
        }
    }
}
