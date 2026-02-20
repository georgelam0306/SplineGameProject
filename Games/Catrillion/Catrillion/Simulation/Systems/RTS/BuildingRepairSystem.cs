using Catrillion.Simulation.Components;
using Catrillion.Simulation.Systems.SimTable;

namespace Catrillion.Simulation.Systems.RTS;

/// <summary>
/// Handles the gradual healing of buildings that are being repaired.
/// Buildings marked with IsRepairing flag heal over time until reaching RepairTargetHealth.
/// </summary>
public sealed class BuildingRepairSystem : SimTableSystem
{
    /// <summary>
    /// HP healed per simulation frame.
    /// At 60 FPS: 5 HP/frame = 300 HP/second.
    /// A 1000 HP building fully damaged takes ~3.3 seconds to repair.
    /// </summary>
    private const int RepairRatePerFrame = 5;

    public BuildingRepairSystem(SimWorld world) : base(world)
    {
    }

    public override void Tick(in SimulationContext context)
    {
        if (!World.IsPlaying()) return;

        var buildings = World.BuildingRows;

        for (int slot = 0; slot < buildings.Count; slot++)
        {
            if (!buildings.TryGetRow(slot, out var building)) continue;

            // Skip buildings not being repaired
            if (!building.Flags.HasFlag(BuildingFlags.IsRepairing)) continue;

            // Heal building
            building.Health += RepairRatePerFrame;
            building.RepairProgress++;

            // Check if repair is complete
            if (building.Health >= building.RepairTargetHealth)
            {
                building.Health = building.RepairTargetHealth;
                building.Flags &= ~BuildingFlags.IsRepairing;
                building.RepairProgress = 0;
                building.RepairTargetHealth = 0;
            }
        }
    }
}
