using Catrillion.Core;
using Catrillion.GameData.Schemas;
using Catrillion.Simulation.Components;
using Catrillion.Simulation.Systems.SimTable;

namespace Catrillion.Simulation.Systems.RTS;

/// <summary>
/// Processes building upgrade commands from input.
/// When a player clicks the upgrade button, validates and performs the upgrade.
/// The building is instantly transformed into the upgraded version.
/// </summary>
public sealed class UpgradeCommandSystem : SimTableSystem
{
    private readonly GameDataManager<GameDocDb> _gameData;

    public UpgradeCommandSystem(SimWorld world, GameDataManager<GameDocDb> gameData) : base(world)
    {
        _gameData = gameData;
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

            if (!input.HasUpgradeCommand) continue;

            // Find the building to upgrade
            int buildingSlot = buildings.GetSlot(input.UpgradeBuildingHandle);
            if (buildingSlot < 0) continue;

            var building = buildings.GetRowBySlot(buildingSlot);

            // Validate ownership
            if (building.OwnerPlayerId != playerId) continue;
            if (building.Flags.IsDead()) continue;
            if (!building.Flags.HasFlag(BuildingFlags.IsActive)) continue;

            // Get current building type data
            int currentTypeId = (int)building.TypeId;
            if (currentTypeId < 0 || currentTypeId >= buildingDb.Count) continue;

            ref readonly var currentType = ref buildingDb.FindById(currentTypeId);

            // Check if building can upgrade
            int upgradeToId = currentType.UpgradesTo;
            if (upgradeToId < 0 || upgradeToId >= buildingDb.Count) continue;

            ref readonly var upgradeType = ref buildingDb.FindById(upgradeToId);

            // Check tech requirements for the upgrade target
            var resources = gameResources.GetRowBySlot(0);
            if (upgradeType.RequiredTechId > 0)
            {
                ulong techMask = 1UL << upgradeType.RequiredTechId;
                if ((resources.UnlockedTech & techMask) == 0) continue;
            }

            // Check affordability (upgrade cost is the target building's cost)
            if (resources.Gold < upgradeType.CostGold) continue;
            if (resources.Wood < upgradeType.CostWood) continue;
            if (resources.Stone < upgradeType.CostStone) continue;
            if (resources.Iron < upgradeType.CostIron) continue;
            if (resources.Oil < upgradeType.CostOil) continue;

            // Deduct costs
            resources.Gold -= upgradeType.CostGold;
            resources.Wood -= upgradeType.CostWood;
            resources.Stone -= upgradeType.CostStone;
            resources.Iron -= upgradeType.CostIron;
            resources.Oil -= upgradeType.CostOil;

            // Perform upgrade - transform the building type
            building.TypeId = (BuildingTypeId)upgradeToId;
            building.MaxHealth = upgradeType.Health;
            building.Health = upgradeType.Health; // Full health after upgrade

            // Update resource generation rates
            building.EffectiveGeneratesGold = upgradeType.GeneratesGold;
            building.EffectiveGeneratesWood = upgradeType.GeneratesWood;
            building.EffectiveGeneratesStone = upgradeType.GeneratesStone;
            building.EffectiveGeneratesIron = upgradeType.GeneratesIron;
            building.EffectiveGeneratesOil = upgradeType.GeneratesOil;
            building.EffectiveGeneratesFood = upgradeType.GeneratesFood;

            // Update population capacity difference
            int oldPopCap = currentType.ProvidesMaxPopulation;
            int newPopCap = upgradeType.ProvidesMaxPopulation;
            resources.MaxPopulation += (newPopCap - oldPopCap);

            // Update garrison capacity if changed
            building.GarrisonCapacity = (byte)upgradeType.GarrisonCapacity;
        }
    }
}
