using Catrillion.Simulation.Components;
using Catrillion.Simulation.Services;
using SimTable;

namespace Catrillion.Simulation.Systems.SimTable;

/// <summary>
/// Ticks construction progress for buildings under construction.
/// When construction completes, transitions building from IsUnderConstruction to IsActive.
/// </summary>
public sealed class BuildingConstructionSystem : SimTableSystem
{
    private readonly PowerNetworkService _powerNetwork;

    public BuildingConstructionSystem(SimWorld world, PowerNetworkService powerNetwork)
        : base(world)
    {
        _powerNetwork = powerNetwork;
    }

    public override void Tick(in SimulationContext context)
    {
        if (!World.IsPlaying()) return;

        var buildings = World.BuildingRows;
        bool anyCompleted = false;

        for (int slot = 0; slot < buildings.Count; slot++)
        {
            if (!buildings.TryGetRow(slot, out var building)) continue;
            if (!building.Flags.HasFlag(BuildingFlags.IsUnderConstruction)) continue;

            // Increment construction progress
            building.ConstructionProgress++;

            // Check if construction complete
            if (building.ConstructionProgress >= building.ConstructionBuildTime)
            {
                // Transition to active state
                building.Flags &= ~BuildingFlags.IsUnderConstruction;
                building.Flags |= BuildingFlags.IsActive;
                // IsPowered will be set by ModifierApplicationSystem based on PowerGrid

                anyCompleted = true;
            }
        }

        // Update power network if any buildings completed construction
        if (anyCompleted)
        {
            // Rebuild spatial hash so newly-active buildings are included in QueryRadius
            World.BuildingRows.SpatialSort();

            _powerNetwork.InvalidateNetwork();
        }
    }
}
