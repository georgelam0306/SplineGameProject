using Catrillion.Core;
using Catrillion.GameData.Schemas;
using Catrillion.Simulation.Components;
using Catrillion.Simulation.Systems.SimTable;

namespace Catrillion.Simulation.Systems.RTS;

/// <summary>
/// Ticks research progress for workshop buildings.
/// When research completes, unlocks the corresponding tech bit.
/// </summary>
public sealed class ResearchSystem : SimTableSystem
{
    private readonly GameDataManager<GameDocDb> _gameData;

    public ResearchSystem(SimWorld world, GameDataManager<GameDocDb> gameData) : base(world)
    {
        _gameData = gameData;
    }

    public override void Tick(in SimulationContext context)
    {
        if (!World.IsPlaying()) return;

        var buildings = World.BuildingRows;
        var gameResources = World.GameResourcesRows;
        var researchDb = _gameData.Db.ResearchItemData;

        for (int slot = 0; slot < buildings.Count; slot++)
        {
            var building = buildings.GetRowBySlot(slot);
            if (building.Flags.IsDead()) continue;
            if (!building.Flags.HasFlag(BuildingFlags.IsActive)) continue;

            // Skip buildings not researching anything
            if (building.CurrentResearchId == 0) continue;

            // Get research data (CurrentResearchId is 1-based)
            int researchId = building.CurrentResearchId - 1;
            if (researchId < 0 || researchId >= researchDb.Count) continue;

            ref readonly var research = ref researchDb.FindById(researchId);

            // Tick progress
            building.ResearchProgress++;

            // Check completion
            if (building.ResearchProgress >= research.ResearchTime)
            {
                // Unlock tech
                var resources = gameResources.GetRowBySlot(0);
                resources.UnlockedTech |= (1UL << research.UnlocksTechId);

                // Reset research state
                building.CurrentResearchId = 0;
                building.ResearchProgress = 0;
            }
        }
    }
}
