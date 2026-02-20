using Catrillion.Core;
using Catrillion.GameData.Schemas;
using Catrillion.Simulation.Components;
using Catrillion.Simulation.Systems.SimTable;
using SimTable;

namespace Catrillion.Simulation.Systems.RTS;

/// <summary>
/// Processes building repair and cancel repair commands from input.
/// When a player clicks repair, validates affordability and starts the repair process.
/// Repair cost is proportional to damage: (MissingHP / MaxHP) * BuildCost.
/// </summary>
public sealed class RepairCommandSystem : SimTableSystem
{
    private readonly GameDataManager<GameDocDb> _gameData;

    public RepairCommandSystem(SimWorld world, GameDataManager<GameDocDb> gameData) : base(world)
    {
        _gameData = gameData;
    }

    public override void Tick(in SimulationContext context)
    {
        if (!World.IsPlaying()) return;

        var buildings = World.BuildingRows;
        var gameResources = World.GameResourcesRows;

        for (int playerId = 0; playerId < context.PlayerCount; playerId++)
        {
            ref readonly var input = ref context.GetInput(playerId);

            // Handle cancel repair command first
            if (input.HasCancelRepairCommand)
            {
                ProcessCancelRepair(playerId, input.CancelRepairBuildingHandle, buildings);
            }

            // Handle start repair command
            if (input.HasRepairCommand)
            {
                ProcessRepairCommand(playerId, input.RepairBuildingHandle, buildings, gameResources);
            }
        }
    }

    private void ProcessRepairCommand(
        int playerId,
        SimHandle buildingHandle,
        BuildingRowTable buildings,
        GameResourcesRowTable gameResources)
    {
        int buildingSlot = buildings.GetSlot(buildingHandle);
        if (buildingSlot < 0) return;

        var building = buildings.GetRowBySlot(buildingSlot);

        // Validate building state
        if (building.OwnerPlayerId != playerId) return;
        if (building.Flags.IsDead()) return;
        if (!building.Flags.HasFlag(BuildingFlags.IsActive)) return;
        if (building.Flags.HasFlag(BuildingFlags.IsRepairing)) return;  // Already repairing
        if (building.Health >= building.MaxHealth) return;  // Already at full health

        // Get building type data
        var buildingDb = _gameData.Db.BuildingTypeData;
        int typeId = (int)building.TypeId;
        if (typeId < 0 || typeId >= buildingDb.Count) return;

        ref readonly var typeData = ref buildingDb.FindById(typeId);

        // Calculate repair cost (proportional to damage)
        int missingHealth = building.MaxHealth - building.Health;
        int maxHealth = building.MaxHealth;
        if (maxHealth <= 0) return;

        // Use integer math to avoid floating point: cost = buildCost * missingHealth / maxHealth
        int repairCostGold = typeData.CostGold * missingHealth / maxHealth;
        int repairCostWood = typeData.CostWood * missingHealth / maxHealth;
        int repairCostStone = typeData.CostStone * missingHealth / maxHealth;
        int repairCostIron = typeData.CostIron * missingHealth / maxHealth;
        int repairCostOil = typeData.CostOil * missingHealth / maxHealth;

        // Validate affordability
        var resources = gameResources.GetRowBySlot(0);
        if (resources.Gold < repairCostGold) return;
        if (resources.Wood < repairCostWood) return;
        if (resources.Stone < repairCostStone) return;
        if (resources.Iron < repairCostIron) return;
        if (resources.Oil < repairCostOil) return;

        // Deduct repair costs upfront
        resources.Gold -= repairCostGold;
        resources.Wood -= repairCostWood;
        resources.Stone -= repairCostStone;
        resources.Iron -= repairCostIron;
        resources.Oil -= repairCostOil;

        // Start repair
        building.Flags |= BuildingFlags.IsRepairing;
        building.RepairProgress = 0;
        building.RepairTargetHealth = building.MaxHealth;
    }

    private void ProcessCancelRepair(int playerId, SimHandle buildingHandle, BuildingRowTable buildings)
    {
        int buildingSlot = buildings.GetSlot(buildingHandle);
        if (buildingSlot < 0) return;

        var building = buildings.GetRowBySlot(buildingSlot);

        // Validate ownership and repair state
        if (building.OwnerPlayerId != playerId) return;
        if (!building.Flags.HasFlag(BuildingFlags.IsRepairing)) return;

        // Stop repair (no refund for simplicity - cost was paid upfront)
        building.Flags &= ~BuildingFlags.IsRepairing;
        building.RepairProgress = 0;
        building.RepairTargetHealth = 0;
    }
}
