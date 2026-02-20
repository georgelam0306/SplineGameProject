using Catrillion.Simulation.Components;
using Catrillion.Simulation.Systems.SimTable;
using Core;

namespace Catrillion.Simulation.Systems.RTS;

/// <summary>
/// Generates resources for players using per-building production cycles.
/// Each production building has its own timer that fills up independently.
/// When the timer completes, resources are delivered in a batch.
///
/// Similar to They Are Billions style economy:
/// - Sawmill fills up over 5 seconds, then delivers 75 wood at once
/// - Quarry fills up over 5 seconds, then delivers 60 stone at once
/// </summary>
public sealed class ResourceGenerationSystem : SimTableSystem
{
    // Default production cycle: 5 seconds
    private const int DefaultCycleDuration = SimulationConfig.TickRate * 5;
    private const int FramesPerSecond = SimulationConfig.TickRate;

    public ResourceGenerationSystem(SimWorld world) : base(world) { }

    public override void Tick(in SimulationContext context)
    {
        if (!World.IsPlaying()) return;

        var buildings = World.BuildingRows;
        var resources = World.GameResourcesRows.GetRowBySlot(0);

        // Process each building's production cycle
        for (int slot = 0; slot < buildings.Count; slot++)
        {
            var building = buildings.GetRowBySlot(slot);

            // Skip inactive or dead buildings
            if (building.Flags.IsDead()) continue;
            if (!building.Flags.HasFlag(BuildingFlags.IsActive)) continue;

            // Skip non-production buildings
            bool isProduction = building.EffectiveGeneratesGold > 0 ||
                               building.EffectiveGeneratesWood > 0 ||
                               building.EffectiveGeneratesStone > 0 ||
                               building.EffectiveGeneratesIron > 0 ||
                               building.EffectiveGeneratesOil > 0 ||
                               building.EffectiveGeneratesFood > 0;
            if (!isProduction) continue;

            // Get cycle duration (0 = use default)
            int cycleDuration = building.ProductionCycleDuration > 0
                ? building.ProductionCycleDuration
                : DefaultCycleDuration;

            // Increment progress each frame
            building.ResourceAccumulator++;

            // Check for cycle completion
            if (building.ResourceAccumulator >= cycleDuration)
            {
                // Calculate batch sizes: effective rate per second * cycle duration / 60
                int batchGold = building.EffectiveGeneratesGold * cycleDuration / FramesPerSecond;
                int batchWood = building.EffectiveGeneratesWood * cycleDuration / FramesPerSecond;
                int batchStone = building.EffectiveGeneratesStone * cycleDuration / FramesPerSecond;
                int batchIron = building.EffectiveGeneratesIron * cycleDuration / FramesPerSecond;
                int batchOil = building.EffectiveGeneratesOil * cycleDuration / FramesPerSecond;
                int batchFood = building.EffectiveGeneratesFood * cycleDuration / FramesPerSecond;

                // Add resources to global pool, clamped to max storage
                resources.Gold = ClampAdd(resources.Gold, batchGold, resources.MaxGold);
                resources.Wood = ClampAdd(resources.Wood, batchWood, resources.MaxWood);
                resources.Stone = ClampAdd(resources.Stone, batchStone, resources.MaxStone);
                resources.Iron = ClampAdd(resources.Iron, batchIron, resources.MaxIron);
                resources.Oil = ClampAdd(resources.Oil, batchOil, resources.MaxOil);
                resources.Food = ClampAdd(resources.Food, batchFood, resources.MaxFood);

                // Track resources gathered for stats
                int totalGathered = batchGold + batchWood + batchStone + batchIron + batchOil + batchFood;
                if (totalGathered > 0)
                {
                    UpdateResourceStats(totalGathered);
                }

                // Reset progress for next cycle
                building.ResourceAccumulator = 0;
            }
        }
    }

    private void UpdateResourceStats(int amount)
    {
        var stats = World.MatchStatsRows;
        if (stats.TryGetRow(0, out var row))
        {
            row.ResourcesGathered += amount;
        }
    }

    private static int ClampAdd(int current, int amount, int max)
    {
        int result = current + amount;
        return result > max ? max : result;
    }
}
