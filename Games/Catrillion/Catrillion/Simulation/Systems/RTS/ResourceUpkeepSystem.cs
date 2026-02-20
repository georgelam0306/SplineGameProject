using Catrillion.Simulation.Components;
using Catrillion.Simulation.Systems.SimTable;
using Core;

namespace Catrillion.Simulation.Systems.RTS;

/// <summary>
/// Deducts upkeep costs from players based on building maintenance requirements.
/// Runs after ResourceGenerationSystem.
///
/// Behavior:
/// - Deducts upkeep every second (60 frames)
/// - If player can't afford upkeep, gold can go negative (debt)
/// - Future: may disable buildings or use priority-based deduction
/// </summary>
public sealed class ResourceUpkeepSystem : SimTableSystem
{
    private const int FramesPerSecond = SimulationConfig.TickRate;

    public ResourceUpkeepSystem(SimWorld world) : base(world) { }

    public override void Tick(in SimulationContext context)
    {
        if (!World.IsPlaying()) return;

        // Only process upkeep once per second
        int frame = context.CurrentFrame;
        if ((frame % FramesPerSecond) != 0) return;

        var buildings = World.BuildingRows;
        var resources = World.GameResourcesRows.GetRowBySlot(0);

        // Accumulate total upkeep from all active buildings
        int goldUpkeep = 0;
        int woodUpkeep = 0;
        int stoneUpkeep = 0;
        int ironUpkeep = 0;
        int oilUpkeep = 0;
        int foodUpkeep = 0;

        for (int slot = 0; slot < buildings.Count; slot++)
        {
            var building = buildings.GetRowBySlot(slot);

            // Skip inactive or dead buildings (no upkeep for dead buildings)
            if (building.Flags.IsDead()) continue;
            if (!building.Flags.HasFlag(BuildingFlags.IsActive)) continue;

            // Use base upkeep values (from CachedStat)
            goldUpkeep += building.UpkeepGold;
            woodUpkeep += building.UpkeepWood;
            stoneUpkeep += building.UpkeepStone;
            ironUpkeep += building.UpkeepIron;
            oilUpkeep += building.UpkeepOil;
            foodUpkeep += building.UpkeepFood;
        }

        // Deduct upkeep from global resources (allow gold to go negative for debt)
        resources.Gold -= goldUpkeep;
        resources.Wood = ClampSubtract(resources.Wood, woodUpkeep);
        resources.Stone = ClampSubtract(resources.Stone, stoneUpkeep);
        resources.Iron = ClampSubtract(resources.Iron, ironUpkeep);
        resources.Oil = ClampSubtract(resources.Oil, oilUpkeep);
        resources.Food = ClampSubtract(resources.Food, foodUpkeep);
    }

    private static int ClampSubtract(int current, int amount)
    {
        int result = current - amount;
        return result < 0 ? 0 : result;
    }
}
