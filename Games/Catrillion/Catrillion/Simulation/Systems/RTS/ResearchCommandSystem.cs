using Catrillion.Core;
using Catrillion.GameData.Schemas;
using Catrillion.Simulation.Components;
using Catrillion.Simulation.Systems.SimTable;

namespace Catrillion.Simulation.Systems.RTS;

/// <summary>
/// Processes research commands from input.
/// When a player clicks a research item in a workshop building, validates and starts the research.
/// </summary>
public sealed class ResearchCommandSystem : SimTableSystem
{
    private readonly GameDataManager<GameDocDb> _gameData;

    public ResearchCommandSystem(SimWorld world, GameDataManager<GameDocDb> gameData) : base(world)
    {
        _gameData = gameData;
    }

    public override void Tick(in SimulationContext context)
    {
        if (!World.IsPlaying()) return;

        var buildings = World.BuildingRows;
        var gameResources = World.GameResourcesRows;
        var researchDb = _gameData.Db.ResearchItemData;
        var buildingResearchDb = _gameData.Db.BuildingResearchData;

        for (int playerId = 0; playerId < context.PlayerCount; playerId++)
        {
            ref readonly var input = ref context.GetInput(playerId);

            if (!input.HasResearchCommand) continue;

            // Handle cancel research (ResearchCommand = 0)
            if (input.ResearchCommand == 0)
            {
                CancelResearch(playerId, buildings);
                continue;
            }

            // Find selected workshop building owned by this player
            int buildingSlot = FindSelectedWorkshop(playerId, buildings);
            if (buildingSlot < 0) continue;

            var building = buildings.GetRowBySlot(buildingSlot);

            // Already researching - can't start another
            if (building.CurrentResearchId != 0) continue;

            // Get research data (ResearchCommand is 1-based)
            int researchId = input.ResearchCommand - 1;
            if (researchId < 0 || researchId >= researchDb.Count) continue;

            ref readonly var research = ref researchDb.FindById(researchId);

            // Validate research is available at this building type
            if (!IsResearchAvailableAtBuilding(building.TypeId, researchId, buildingResearchDb)) continue;

            // Check prerequisites
            var resources = gameResources.GetRowBySlot(0);
            if (research.PrerequisiteTechId >= 0)
            {
                ulong prereqMask = 1UL << research.PrerequisiteTechId;
                if ((resources.UnlockedTech & prereqMask) == 0) continue;
            }

            // Check if already researched
            ulong techMask = 1UL << research.UnlocksTechId;
            if ((resources.UnlockedTech & techMask) != 0) continue;

            // Check affordability
            if (resources.Gold < research.CostGold) continue;
            if (resources.Wood < research.CostWood) continue;
            if (resources.Stone < research.CostStone) continue;
            if (resources.Iron < research.CostIron) continue;
            if (resources.Oil < research.CostOil) continue;

            // Deduct costs
            resources.Gold -= research.CostGold;
            resources.Wood -= research.CostWood;
            resources.Stone -= research.CostStone;
            resources.Iron -= research.CostIron;
            resources.Oil -= research.CostOil;

            // Start research (1-based)
            building.CurrentResearchId = (byte)(researchId + 1);
            building.ResearchProgress = 0;
        }
    }

    private int FindSelectedWorkshop(int playerId, BuildingRowTable buildings)
    {
        for (int slot = 0; slot < buildings.Count; slot++)
        {
            var building = buildings.GetRowBySlot(slot);
            if (building.Flags.IsDead()) continue;
            if (!building.Flags.HasFlag(BuildingFlags.IsActive)) continue;
            if (building.SelectedByPlayerId != playerId) continue;
            if (building.OwnerPlayerId != playerId) continue;

            // Check if this building type has research items
            if (IsWorkshopBuilding(building.TypeId))
            {
                return slot;
            }
        }
        return -1;
    }

    private bool IsWorkshopBuilding(BuildingTypeId typeId)
    {
        // Check if this building type has any research items via BuildingResearchData
        int buildingTypeId = (int)typeId;
        var buildingResearchDb = _gameData.Db.BuildingResearchData;
        for (int i = 0; i < buildingResearchDb.Count; i++)
        {
            ref readonly var mapping = ref buildingResearchDb.GetAtIndex(i);
            if (mapping.BuildingTypeId == buildingTypeId)
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsResearchAvailableAtBuilding(BuildingTypeId buildingType, int researchId, BuildingResearchDataTable buildingResearchDb)
    {
        int buildingTypeId = (int)buildingType;
        for (int i = 0; i < buildingResearchDb.Count; i++)
        {
            ref readonly var mapping = ref buildingResearchDb.GetAtIndex(i);
            if (mapping.BuildingTypeId == buildingTypeId && mapping.ResearchItemId == researchId)
            {
                return true;
            }
        }
        return false;
    }

    private void CancelResearch(int playerId, BuildingRowTable buildings)
    {
        // Find the selected building and cancel its research
        int buildingSlot = FindSelectedWorkshop(playerId, buildings);
        if (buildingSlot < 0) return;

        var building = buildings.GetRowBySlot(buildingSlot);
        if (building.CurrentResearchId == 0) return;

        // Note: We could optionally refund partial costs here based on progress
        // For now, canceling loses all invested resources (like most RTS games)

        building.CurrentResearchId = 0;
        building.ResearchProgress = 0;
    }
}
